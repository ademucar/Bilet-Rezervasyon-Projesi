using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Abstractions.Email;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.Password;

// ===================================================================
// 1) SIFRE DEGISTIRME -- giris yapmis kullanici
// ===================================================================

/// <summary>PDF: POST /api/v1/auth/change-password</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<Result>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Mevcut sifre zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni sifre zorunludur.")
            .MinimumLength(8).WithMessage("Sifre en az 8 karakter olmalidir.")
            .MaximumLength(72).WithMessage("Sifre en fazla 72 karakter olabilir.")
            .Matches("[A-Z]").WithMessage("Sifre en az bir buyuk harf icermelidir.")
            .Matches("[a-z]").WithMessage("Sifre en az bir kucuk harf icermelidir.")
            .Matches("[0-9]").WithMessage("Sifre en az bir rakam icermelidir.")

            // Yeni sifre eskisiyle ayni olamaz.
            //
            // Bu kontrol olmasaydi "sifreni degistir" uyarisi alan bir
            // kullanici ayni sifreyi girip uyariyi susturabilirdi --
            // yani guvenlik onlemi hicbir sey yapmamis olurdu.
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Yeni sifre mevcut sifreyle ayni olamaz.");
    }
}

internal sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUser _currentUser;

    public ChangePasswordCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ICurrentUser currentUser)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(AuthErrors.InvalidCredentials);
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UserNotFound);
        }

        // Mevcut sifreyi DOGRULUYORUZ -- token'a sahip olmak yetmez.
        //
        // Neden? Saldirgan bir sekilde access token ele gecirmisse
        // (calinmis cihaz, XSS), sifreyi degistirip hesabi kalici
        // olarak ele gecirebilirdi. Mevcut sifre sormak bu yolu kapatir.
        //
        // Bu, "hassas islem icin yeniden kimlik dogrulama" ilkesidir.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(AuthErrors.InvalidCredentials);
        }

        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword));

        // ==============================================================
        // SIFRE DEGISINCE TUM OTURUMLARI KAPAT
        // ==============================================================
        // Kullanici sifresini genelde "biri hesabima girmis olabilir"
        // supehesiyle degistirir. Eski refresh token'lar gecerli kalsaydi
        // saldirgan 7 gun daha erisebilirdi -- yani sifre degistirmek
        // hicbir ise yaramazdi.
        //
        // Bu, sifre degistirmenin ANLAMLI olmasini saglayan adimdir ve
        // cok sik atlanir.
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in activeTokens)
        {
            token.Revoke(_currentUser.IpAddress);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// 2) SIFREMI UNUTTUM
// ===================================================================

/// <summary>PDF: POST /api/v1/auth/forgot-password</summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest<Result>;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
        => RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-posta adresi zorunludur.")
            .EmailAddress().WithMessage("Gecerli bir e-posta adresi giriniz.");
}

internal sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    /// <summary>
    /// Sifirlama linkinin gecerlilik suresi.
    ///
    /// 1 saat, guvenlik ile kullanilabilirlik arasinda yaygin bir denge:
    ///   - Kullanici e-postasini gorup tiklamaya yeterli sure bulur.
    ///   - E-posta kutusuna sonradan erisen biri icin pencere dardir.
    /// </summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    private readonly IApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IDateTimeProvider _clock;
    private readonly IAppUrlProvider _urls;

    public ForgotPasswordCommandHandler(
        IApplicationDbContext context,
        ITokenService tokenService,
        IEmailService emailService,
        IDateTimeProvider clock,
        IAppUrlProvider urls)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _clock = clock;
        _urls = urls;
    }

    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, cancellationToken)
            .ConfigureAwait(false);

        // ==============================================================
        // KULLANICI BULUNAMASA BILE BASARILI DONUYORUZ
        // ==============================================================
        // Bu, Login'dekiyle AYNI sebep: kullanici numaralandirmayi
        // engellemek.
        //
        // "Bu e-posta kayitli degil" deseydik, saldirgan bir e-posta
        // listesini bu endpoint'e sokup hangilerinin sistemde oldugunu
        // ogrenirdi. Ustelik bu endpoint kimlik dogrulamasi
        // gerektirmiyor -- yani herkese acik bir tarama araci olurdu.
        //
        // Kullanici acisindan da dogru davranis: "Eger bu adres
        // kayitliysa bir e-posta gonderdik" mesaji hem dogru hem
        // guvenli.
        if (user is null)
        {
            return Result.Success();
        }

        // Refresh token uretecini yeniden kullaniyorum: ayni kriptografik
        // guvenceye ihtiyacimiz var (tahmin edilemez, yuksek entropili)
        // ve ikinci bir uretec yazmak gereksiz tekrar olurdu.
        var resetToken = _tokenService.CreateRefreshToken();

        user.SetPasswordResetToken(resetToken.HashValue, _clock.UtcNow.Add(TokenLifetime));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Linke token'in KENDISI konuyor; veritabaninda HASH'i duruyor.
        var resetLink = $"{_urls.FrontendUrl}/sifre-sifirla?token={Uri.EscapeDataString(resetToken.Value)}";

        await _emailService.SendAsync(
            user.Email,
            "Sifre Sifirlama Talebi",
            $"""
             <p>Merhaba {user.FirstName},</p>
             <p>Sifrenizi sifirlamak icin asagidaki baglantiya tiklayin:</p>
             <p><a href="{resetLink}">Sifremi Sifirla</a></p>
             <p>Bu baglanti <strong>1 saat</strong> gecerlidir.</p>
             <p>Bu talebi siz yapmadiysaniz bu e-postayi yok sayabilirsiniz;
                sifreniz degismeyecektir.</p>
             """,
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// 3) SIFRE SIFIRLAMA
// ===================================================================

/// <summary>PDF: POST /api/v1/auth/reset-password</summary>
public sealed record ResetPasswordCommand(string Token, string NewPassword) : IRequest<Result>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("Sifirlama anahtari zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni sifre zorunludur.")
            .MinimumLength(8).WithMessage("Sifre en az 8 karakter olmalidir.")
            .MaximumLength(72).WithMessage("Sifre en fazla 72 karakter olabilir.")
            .Matches("[A-Z]").WithMessage("Sifre en az bir buyuk harf icermelidir.")
            .Matches("[a-z]").WithMessage("Sifre en az bir kucuk harf icermelidir.")
            .Matches("[0-9]").WithMessage("Sifre en az bir rakam icermelidir.");
    }
}

internal sealed class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IDateTimeProvider _clock;

    public ResetPasswordCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IDateTimeProvider clock)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _clock = clock;
    }

    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashRefreshToken(request.Token);

        // Token hash'i ile kullaniciyi ariyoruz.
        // ix_users_password_reset_token partial index'i bu sorguyu karsiliyor.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        // Token yoksa veya suresi dolmussa AYNI hatayi donuyoruz.
        //
        // "Token suresi dolmus" ile "token gecersiz" ayrimini yapmiyoruz
        // cunku ikisi de saldirgana bilgi verir: "suresi dolmus" demek,
        // o token'in bir zamanlar GECERLI oldugunu itiraf etmektir.
        if (user is null || !user.IsPasswordResetTokenValid(tokenHash, _clock.UtcNow))
        {
            return Result.Failure(Error.Validation(
                "auth.invalid_reset_token",
                "Sifirlama baglantisi gecersiz veya suresi dolmus. Lutfen yeni bir talep olusturun."));
        }

        // ChangePasswordHash icinde ClearPasswordResetToken da cagriliyor,
        // yani token TEK KULLANIMLIK oluyor. Ayni link ikinci kez
        // calismaz -- e-postasi baskasinin eline gecen kullanici icin
        // onemli bir koruma.
        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword));

        // Sifre sifirlandiginda tum oturumlari kapat.
        // Sifirlamanin sebebi genelde hesabin ele gecirilmis olmasidir;
        // saldirganin mevcut oturumu devam etmemeli.
        var activeTokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in activeTokens)
        {
            token.Revoke();
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

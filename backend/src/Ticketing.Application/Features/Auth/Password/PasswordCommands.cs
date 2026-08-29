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
// 1) SIFRE DEGISTIRME -- giriş yapmış kullanıcı
// ===================================================================

/// <summary>PDF: POST /api/v1/auth/change-password</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<Result>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Mevcut şifre zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni şifre zorunludur.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .MaximumLength(72).WithMessage("Şifre en fazla 72 karakter olabilir.")
            .Matches("[A-Z]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.")

            // Yeni şifre eskisiyle aynı olamaz.
            //
            // Bu kontrol olmasaydı "sifreni degistir" uyarısı alan bir
            // kullanıcı aynı sifreyi girip uyariyi susturabilirdi --
            // yani güvenlik önlemi hiçbir sey yapmamis olurdu.
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("Yeni şifre mevcut sifreyle aynı olamaz.");
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
        // Neden? Saldirgan bir şekilde access token ele gecirmisse
        // (calinmis cihaz, XSS), sifreyi degistirip hesabi kalici
        // olarak ele gecirebilirdi. Mevcut şifre sormak bu yolu kapatır.
        //
        // Bu, "hassas işlem için yeniden kimlik doğrulama" ilkesidir.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure(AuthErrors.InvalidCredentials);
        }

        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword));

        // ==============================================================
        // SIFRE DEGISINCE TÜM OTURUMLARI KAPAT
        // ==============================================================
        // Kullanıcı sifresini genelde "biri hesabima girmis olabilir"
        // supehesiyle değiştirir. Eski refresh token'lar geçerli kalsaydi
        // saldirgan 7 gün daha erisebilirdi -- yani şifre degistirmek
        // hiçbir ise yaramazdi.
        //
        // Bu, şifre degistirmenin ANLAMLI olmasini saglayan adimdir ve
        // çok sik atlanir.
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
            .EmailAddress().WithMessage("Geçerli bir e-posta adresi giriniz.");
}

internal sealed class ForgotPasswordCommandHandler : IRequestHandler<ForgotPasswordCommand, Result>
{
    /// <summary>
    /// Sıfırlama linkinin gecerlilik süresi.
    ///
    /// 1 saat, güvenlik ile kullanilabilirlik arasında yaygin bir denge:
    ///   - Kullanıcı e-postasini gorup tiklamaya yeterli süre bulur.
    ///   - E-posta kutusuna sonradan erisen biri için pencere dardir.
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
        // Bu, Login'dekiyle AYNI sebep: kullanıcı numaralandirmayi
        // engellemek.
        //
        // "Bu e-posta kayıtlı değil" deseydik, saldirgan bir e-posta
        // listesini bu endpoint'e sokup hangilerinin sistemde olduğunu
        // ogrenirdi. Ustelik bu endpoint kimlik dogrulamasi
        // gerektirmiyor -- yani herkese açık bir tarama araci olurdu.
        //
        // Kullanıcı acisindan da doğru davranis: "Eger bu adres
        // kayıtlıysa bir e-posta gonderdik" mesaji hem doğru hem
        // güvenli.
        if (user is null)
        {
            return Result.Success();
        }

        // Refresh token uretecini yeniden kullanıyorum: aynı kriptografik
        // guvenceye ihtiyacimiz var (tahmin edilemez, yüksek entropili)
        // ve ikinci bir uretec yazmak gereksiz tekrar olurdu.
        var resetToken = _tokenService.CreateRefreshToken();

        user.SetPasswordResetToken(resetToken.HashValue, _clock.UtcNow.Add(TokenLifetime));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Linke token'in KENDISI konuyor; veritabaninda HASH'i duruyor.
        var resetLink = $"{_urls.FrontendUrl}/sifre-sifirla?token={Uri.EscapeDataString(resetToken.Value)}";

        await _emailService.SendAsync(
            user.Email,
            "Şifre Sıfırlama Talebi",
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
        RuleFor(x => x.Token).NotEmpty().WithMessage("Sıfırlama anahtari zorunludur.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Yeni şifre zorunludur.")
            .MinimumLength(8).WithMessage("Şifre en az 8 karakter olmalıdır.")
            .MaximumLength(72).WithMessage("Şifre en fazla 72 karakter olabilir.")
            .Matches("[A-Z]").WithMessage("Şifre en az bir büyük harf içermelidir.")
            .Matches("[a-z]").WithMessage("Şifre en az bir küçük harf içermelidir.")
            .Matches("[0-9]").WithMessage("Şifre en az bir rakam içermelidir.");
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

        // Token hash'i ile kullanıcıyı ariyoruz.
        // ix_users_password_reset_token partial index'i bu sorguyu karsiliyor.
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        // Token yoksa veya süresi dolmussa AYNI hatayi donuyoruz.
        //
        // "Token süresi dolmuş" ile "token geçersiz" ayrimini yapmiyoruz
        // çünkü ikisi de saldirgana bilgi verir: "süresi dolmuş" demek,
        // o token'in bir zamanlar GECERLI olduğunu itiraf etmektir.
        if (user is null || !user.IsPasswordResetTokenValid(tokenHash, _clock.UtcNow))
        {
            return Result.Failure(Error.Validation(
                "auth.invalid_reset_token",
                "Sıfırlama bağlantısı geçersiz veya süresi dolmuş. Lütfen yeni bir talep oluşturun."));
        }

        // ChangePasswordHash içinde ClearPasswordResetToken da cagriliyor,
        // yani token TEK KULLANIMLIK oluyor. Aynı link ikinci kez
        // calismaz -- e-postası baskasinin eline gecen kullanıcı için
        // önemli bir koruma.
        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword));

        // Şifre sifirlandiginda tüm oturumlari kapat.
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

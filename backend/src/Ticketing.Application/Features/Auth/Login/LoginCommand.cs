using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Options;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Auth.Login;

/// <summary>PDF: POST /api/v1/auth/login</summary>
public sealed record LoginCommand(string Email, string Password)
    : IRequest<Result<AuthResponse>>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // ==================================================================
        // LOGIN'DE SIFRE KURALLARI UYGULANMAZ -- BU KASITLI
        // ==================================================================
        // Register'da "en az bir buyuk harf" gibi kurallar var ama burada
        // YOK. Sadece "bos olmasin" diyoruz.
        //
        // Neden? Iki sebep:
        //
        // 1) Sifre politikasi zamanla degisir. Bugun 8 karakter zorunlu
        //    ama 2 yil once kayit olan kullanicinin sifresi 6 karakter
        //    olabilir. Login'de yeni kurali uygularsak o kullanici
        //    kendi hesabina GIREMEZ hale gelir.
        //
        // 2) Saldirgana bilgi vermemek. "Sifre en az bir rakam
        //    icermelidir" hatasi, saldirgana sifre politikasini ogretir
        //    ve deneme uzayini daraltmasini saglar.
        // ==================================================================
        RuleFor(x => x.Email).NotEmpty().WithMessage("E-posta adresi zorunludur.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Sifre zorunludur.");
    }
}

/// <summary>
/// Giris akisi. PDF Sprint 15'in "Brute force korumasi" maddesini de karsilar.
/// </summary>
internal sealed class LoginCommandHandler
    : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly SecurityOptions _security;

    public LoginCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IOptions<SecurityOptions> security)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _security = security.Value;
    }

    public async Task<Result<AuthResponse>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Rolleri de yukluyorum cunku token'a yazacagim.
        // Ayri sorgu yapmak yerine tek seferde alarak veritabanina
        // gidis sayisini azaltiyorum.
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // ==============================================================
            // ZAMANLAMA SALDIRISINA KARSI SAHTE HASH DOGRULAMA
            // ==============================================================
            // Burada dogrudan donseydik su acik olusurdu:
            //
            //   Kullanici YOK  -> istek ~5 ms surer (sadece DB sorgusu)
            //   Kullanici VAR  -> istek ~300 ms surer (BCrypt dogrulamasi)
            //
            // Saldirgan yanit SURESINE bakarak e-postanin kayitli olup
            // olmadigini anlayabilirdi -- hata mesajlarini ozdes yapmamiz
            // bosa giderdi. Buna "zamanlama saldirisi" (timing attack) denir.
            //
            // Cozum: kullanici bulunamasa BILE bir BCrypt dogrulamasi
            // calistiriyoruz. Boylece iki durum da ayni sureyi aliyor.
            //
            // Kullanilan hash gecerli bir BCrypt hash'i ("dummy" kelimesinin
            // hash'i); sonucu zaten kullanmiyoruz, amac sadece ayni
            // hesaplama maliyetini odemek.
            _ = _passwordHasher.Verify(
                request.Password,
                "$2a$12$C6UzMDM.H6dfI/f/IKcEe.7ZLQhO7BsLFcHy5UbfHYHmqLQ8sBEHu");

            return Result.Failure<AuthResponse>(AuthErrors.InvalidCredentials);
        }

        // Kilit kontrolu, sifre kontrolunden ONCE.
        // Kilitli hesapta sifre dogrulamasi yapmak hem gereksiz CPU
        // harcar hem de saldirganin kilit durumunu atlatmasina yarar.
        if (user.IsLockedOut())
        {
            return Result.Failure<AuthResponse>(AuthErrors.AccountLocked);
        }

        if (!user.IsActive)
        {
            return Result.Failure<AuthResponse>(AuthErrors.AccountInactive);
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            user.RegisterFailedLogin(
                _security.MaxFailedLoginAttempts,
                TimeSpan.FromMinutes(_security.LockoutMinutes));

            // Basarisiz deneme sayacini KAYDETMEK zorundayiz.
            // Kaydetmezsek sayac hic artmaz ve brute force korumasi
            // hicbir sey yapmaz -- calistigini sanip korumasiz kaliriz.
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Failure<AuthResponse>(AuthErrors.InvalidCredentials);
        }

        // Basarili giris: cezayi kaldir.
        user.ResetFailedLoginAttempts();

        var roles = await GetRoleNamesAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var refreshToken = _tokenService.CreateRefreshToken();

        _context.RefreshTokens.Add(Domain.Entities.RefreshToken.Create(
            user.Id,
            refreshToken.HashValue,
            refreshToken.ExpiresAt));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var accessToken = _tokenService.CreateAccessToken(user.Id, user.Email, roles);

        return Result.Success(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt,
            new UserSummary(
                user.Id, user.Email, user.FirstName, user.LastName,
                user.IsEmailConfirmed, roles)));
    }

    private async Task<IReadOnlyCollection<string>> GetRoleNamesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Join yerine navigation kullaniyorum; EF bunu tek sorguya cevirir.
        // AsNoTracking cunku bu veriyi yalnizca okuyup token'a yazacagiz;
        // EF'in degisiklik takibi yapmasina gerek yok (bellek tasarrufu).
        return await _context.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

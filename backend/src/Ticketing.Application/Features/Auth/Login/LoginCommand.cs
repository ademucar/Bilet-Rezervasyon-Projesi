using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Options;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Common.Security;

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
internal sealed partial class LoginCommandHandler
    : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly SecurityOptions _security;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IOptions<SecurityOptions> security,
        ILogger<LoginCommandHandler> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _security = security.Value;
        _logger = logger;
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

            // ==========================================================
            // PDF Sprint 16: "Basarisiz login" loglanmalidir.
            // ==========================================================
            // E-POSTA MASKELI (Sprint 15 gerekcesi): basarisiz giris
            // loglari saldiri sirasinda BINLERCE satir uretiyor. Acik
            // yazsaydik, saldirganin denedigi tum adresler log
            // dosyasinda toplu bir liste olusturur -- yani saldirgan
            // basarisiz olsa bile bizim loglarimiz onun ise yarardi.
            //
            // Sebebi de ayri bir alan olarak veriyorum ("kullanici yok"
            // / "sifre yanlis"). Ayni mesaji kullansaydik, uretimde
            // "hangi hesaplar VAR?" sorusunu loglardan cevaplamak
            // imkansiz olurdu -- oysa bu, bir saldirinin hedefli mi
            // yoksa korlemesine mi oldugunu anlamak icin gerekli.
            //
            // DIKKAT: bu ayrim yalnizca LOGDA var. Kullaniciya donen
            // yanit ikisinde de ayni ("E-posta veya sifre hatali") --
            // aksi halde hesap sayimi (user enumeration) yapilabilirdi.
            // ==========================================================
            LogLoginFailed(_logger, SensitiveDataMasker.MaskEmail(email), "kullanici_yok");

            return Result.Failure<AuthResponse>(AuthErrors.InvalidCredentials);
        }

        // Kilit kontrolu, sifre kontrolunden ONCE.
        // Kilitli hesapta sifre dogrulamasi yapmak hem gereksiz CPU
        // harcar hem de saldirganin kilit durumunu atlatmasina yarar.
        if (user.IsLockedOut())
        {
            // Kilitli hesaba giris denemesi, DEVAM EDEN bir saldirinin
            // en net isaretidir: hesap zaten kilitlendigi halde biri
            // hala deniyor.
            //
            // Burada kullanici KIMLIGINI (Guid) logluyorum, e-postayi
            // degil: hesap zaten belirlenmis durumda ve destek ekibi
            // Guid ile kullaniciya ulasabiliyor.
            LogLoginBlocked(_logger, user.Id, "hesap_kilitli");

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

            LogLoginFailed(_logger, SensitiveDataMasker.MaskEmail(email), "sifre_yanlis");

            // Bu deneme hesabi KILITLEDIYSE ayrica logluyorum.
            //
            // Neden ayri bir olay? Cunku bu, izleme sisteminde alarm
            // kurulacak esik: tek bir basarisiz giris gurultu, ama
            // "son 10 dakikada 50 hesap kilitlendi" bir saldiri.
            //
            // Ayni EventId'yi kullansaydik bu iki durumu birbirinden
            // ayiran bir alarm kurali yazilamazdi.
            if (user.IsLockedOut())
            {
                LogAccountLocked(_logger, user.Id, _security.MaxFailedLoginAttempts);
            }

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

        // PDF Sprint 16: "Login" loglanmalidir.
        //
        // Token'i veya e-postayi LOGLAMIYORUM. Kullanici kimligi ve
        // rolleri yeterli: "kim giris yapti" sorusunu cevapliyor ama
        // log dosyasi tek basina ne bir kullanici listesi ne de bir
        // oturum ele gecirme araci oluyor.
        LogLoginSucceeded(_logger, user.Id, roles.Count);

        return Result.Success(new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshToken.Value,
            refreshToken.ExpiresAt,
            new UserSummary(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.IsEmailConfirmed,
                roles)));
    }

    // ==============================================================
    // LOG TANIMLARI
    // ==============================================================
    // [LoggerMessage] kaynak ureteci kullaniyorum (CA1848):
    // _logger.LogInformation("...", arg) yazsaydik her cagride
    // string bicimlendirme ve kutulama (boxing) olurdu -- log
    // seviyesi kapali olsa BILE.
    //
    // Uretilen kod once IsEnabled kontrolu yapiyor; kapaliysa
    // hicbir tahsis yapmiyor.
    // ==============================================================

    [LoggerMessage(
        EventId = LogEvents.LoginBasarili,
        Level = LogLevel.Information,
        Message = "Giris basarili. Kullanici: {UserId}, Rol sayisi: {RoleCount}")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid userId, int roleCount);

    /// <remarks>
    /// Warning seviyesi, Information degil.
    ///
    /// Sebep: uretim ortamlarinda Information cogu zaman
    /// filtreleniyor. Basarisiz girisi Information yapsaydik,
    /// "son 5 dakikada 100 basarisiz giris" alarmi HIC tetiklenmezdi
    /// -- kural dogru olurdu ama besleyen veri hic gelmezdi.
    ///
    /// Bu, guvenlik acisindan en degerli log satirimiz.
    /// </remarks>
    [LoggerMessage(
        EventId = LogEvents.LoginBasarisiz,
        Level = LogLevel.Warning,
        Message = "Giris basarisiz. E-posta: {MaskedEmail}, Sebep: {Reason}")]
    private static partial void LogLoginFailed(ILogger logger, string maskedEmail, string reason);

    [LoggerMessage(
        EventId = LogEvents.LoginBasarisiz,
        Level = LogLevel.Warning,
        Message = "Giris engellendi. Kullanici: {UserId}, Sebep: {Reason}")]
    private static partial void LogLoginBlocked(ILogger logger, Guid userId, string reason);

    [LoggerMessage(
        EventId = LogEvents.HesapKilitlendi,
        Level = LogLevel.Warning,
        Message = "Hesap kilitlendi. Kullanici: {UserId}, Basarisiz deneme: {Attempts}")]
    private static partial void LogAccountLocked(ILogger logger, Guid userId, int attempts);

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

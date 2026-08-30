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
        // Login'de sifre kurallari uygulanmaz -- bu kasitli
        //
        // Register'da "en az bir büyük harf" gibi kurallar var ama burada
        // YOK. Sadece "boş olmasın" diyorum.
        //
        // Neden? Iki sebep:
        //
        // 1) Şifre politikasi zamanla degisir. Bugun 8 karakter zorunlu
        //    ama 2 yil önce kayıt olan kullanıcının sifresi 6 karakter
        //    olabilir. Login'de yeni kuralı uygularsak o kullanıcı
        //    kendi hesabina GIREMEZ hale gelir.
        //
        // 2) Saldirgana bilgi vermemek. "Şifre en az bir rakam
        //    içermelidir" hatası, saldirgana şifre politikasini ogretir
        //    ve deneme uzayini daraltmasini saglar.
        RuleFor(x => x.Email).NotEmpty().WithMessage("E-posta adresi zorunludur.");
        RuleFor(x => x.Password).NotEmpty().WithMessage("Şifre zorunludur.");
    }
}

/// <summary>
/// Giriş akışı. PDF Sprint 15'in "Brute force korumasi" maddesini de karsilar.
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

        // Rolleri de yukluyorum çünkü token'a yazacagim.
        // Ayrı sorgu yapmak yerine tek seferde alarak veritabanina
        // gidis sayisini azaltiyorum.
        var user = await _context.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            // Zamanlama saldirisina karsi sahte hash dogrulama
            //
            // Burada doğrudan donseydim su açık olusurdu:
            //
            //   Kullanıcı YOK  -> istek ~5 ms surer (sadece DB sorgusu)
            //   Kullanıcı VAR  -> istek ~300 ms surer (BCrypt dogrulamasi)
            //
            // Saldirgan yanit SURESINE bakarak e-postanin kayıtlı olup
            // olmadigini anlayabilirdi -- hata mesajlarini ozdes yapmamiz
            // bosa giderdi. Buna "zamanlama saldirisi" (timing attack) denir.
            //
            // Cozum: kullanıcı bulunamasa BILE bir BCrypt dogrulamasi
            // calistiriyoruz. Boylece iki durum da aynı süreyi aliyor.
            //
            // Kullanilan hash geçerli bir BCrypt hash'i ("dummy" kelimesinin
            // hash'i); sonucu zaten kullanmiyorum, amac sadece aynı
            // hesaplama maliyetini odemek.
            _ = _passwordHasher.Verify(
                request.Password,
                "$2a$12$C6UzMDM.H6dfI/f/IKcEe.7ZLQhO7BsLFcHy5UbfHYHmqLQ8sBEHu");

            // PDF Sprint 16: "Başarısız login" loglanmalidir.
            //
            // E-POSTA MASKELI (Sprint 15 gerekçesi): başarısız giriş
            // loglari saldiri sırasında BINLERCE satır uretiyor. Acik
            // yazsaydım, saldirganin denedigi tüm adresler log
            // dosyasinda toplu bir liste oluşturur -- yani saldirgan
            // başarısız olsa bile benim loglarim onun ise yarardi.
            //
            // Sebebi de ayrı bir alan olarak veriyorum ("kullanıcı yok"
            // / "şifre yanlış"). Aynı mesaji kullansaydım, uretimde
            // "hangi hesaplar VAR?" sorusunu loglardan cevaplamak
            // imkansiz olurdu -- oysa bu, bir saldirinin hedefli mi
            // yoksa korlemesine mi olduğunu anlamak için gerekli.
            //
            // DIKKAT: bu ayrim yalnızca LOGDA var. Kullanıcıya donen
            // yanit ikisinde de aynı ("E-posta veya şifre hatalı") --
            // aksi halde hesap sayimi (user enumeration) yapilabilirdi.
            LogLoginFailed(_logger, SensitiveDataMasker.MaskEmail(email), "kullanici_yok");

            return Result.Failure<AuthResponse>(AuthErrors.InvalidCredentials);
        }

        // Kilit kontrolü, şifre kontrolunden ONCE.
        // Kilitli hesapta şifre dogrulamasi yapmak hem gereksiz CPU
        // harcar hem de saldirganin kilit durumunu atlatmasina yarar.
        if (user.IsLockedOut())
        {
            // Kilitli hesaba giriş denemesi, DEVAM EDEN bir saldirinin
            // en net isaretidir: hesap zaten kilitlendigi halde biri
            // hâlâ deniyor.
            //
            // Burada kullanıcı KIMLIGINI (Guid) logluyorum, e-postayi
            // değil: hesap zaten belirlenmis durumda ve destek ekibi
            // Guid ile kullanıcıya ulasabiliyor.
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

            // Başarısız deneme sayacini KAYDETMEK zorundayız.
            // Kaydetmezsek sayaç hiç artmaz ve brute force korumasi
            // hiçbir sey yapmaz -- calistigini sanip korumasiz kaliriz.
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            LogLoginFailed(_logger, SensitiveDataMasker.MaskEmail(email), "sifre_yanlis");

            // Bu deneme hesabi KILITLEDIYSE ayrıca logluyorum.
            //
            // Neden ayrı bir olay? Çünkü bu, izleme sisteminde alarm
            // kurulacak esik: tek bir başarısız giriş gurultu, ama
            // "son 10 dakikada 50 hesap kilitlendi" bir saldiri.
            //
            // Aynı EventId'yi kullansaydım bu iki durumu birbirinden
            // ayiran bir alarm kuralı yazilamazdi.
            if (user.IsLockedOut())
            {
                LogAccountLocked(_logger, user.Id, _security.MaxFailedLoginAttempts);
            }

            return Result.Failure<AuthResponse>(AuthErrors.InvalidCredentials);
        }

        // Başarılı giriş: cezayi kaldir.
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
        // Token'i veya e-postayi LOGLAMIYORUM. Kullanıcı kimliği ve
        // rolleri yeterli: "kim giriş yapti" sorusunu cevapliyor ama
        // log dosyasi tek başına ne bir kullanıcı listesi ne de bir
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

    // Log tanimlari
    //
    // [LoggerMessage] kaynak ureteci kullanıyorum (CA1848):
    // _logger.LogInformation("...", arg) yazsaydım her cagride
    // string bicimlendirme ve kutulama (boxing) olurdu -- log
    // seviyesi kapalı olsa BILE.
    //
    // Uretilen kod önce IsEnabled kontrolü yapiyor; kapaliysa
    // hiçbir tahsis yapmiyor.

    [LoggerMessage(
        EventId = LogEvents.LoginBasarili,
        Level = LogLevel.Information,
        Message = "Giriş başarılı. Kullanıcı: {UserId}, Rol sayısı: {RoleCount}")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid userId, int roleCount);

    /// <remarks>
    /// Warning seviyesi, Information değil.
    ///
    /// Sebep: üretim ortamlarinda Information çoğu zaman
    /// filtreleniyor. Başarısız girişi Information yapsaydim,
    /// "son 5 dakikada 100 başarısız giriş" alarmi HİÇ tetiklenmezdi
    /// -- kural doğru olurdu ama besleyen veri hiç gelmezdi.
    ///
    /// Bu, güvenlik acisindan en degerli log satirim.
    /// </remarks>
    [LoggerMessage(
        EventId = LogEvents.LoginBasarisiz,
        Level = LogLevel.Warning,
        Message = "Giriş başarısız. E-posta: {MaskedEmail}, Sebep: {Reason}")]
    private static partial void LogLoginFailed(ILogger logger, string maskedEmail, string reason);

    [LoggerMessage(
        EventId = LogEvents.LoginBasarisiz,
        Level = LogLevel.Warning,
        Message = "Giriş engellendi. Kullanıcı: {UserId}, Sebep: {Reason}")]
    private static partial void LogLoginBlocked(ILogger logger, Guid userId, string reason);

    [LoggerMessage(
        EventId = LogEvents.HesapKilitlendi,
        Level = LogLevel.Warning,
        Message = "Hesap kilitlendi. Kullanıcı: {UserId}, Başarısız deneme: {Attempts}")]
    private static partial void LogAccountLocked(ILogger logger, Guid userId, int attempts);

    private async Task<IReadOnlyCollection<string>> GetRoleNamesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Join yerine navigation kullanıyorum; EF bunu tek sorguya cevirir.
        // AsNoTracking çünkü bu veriyi yalnızca okuyup token'a yazacagim;
        // EF'in degisiklik takibi yapmasina gerek yok (bellek tasarrufu).
        return await _context.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.Role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

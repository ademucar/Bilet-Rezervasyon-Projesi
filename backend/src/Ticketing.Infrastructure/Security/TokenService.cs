using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;

namespace Ticketing.Infrastructure.Security;

/// <summary>
/// JWT access token ve refresh token üretimi.
/// PDF Sprint 3'un token gereksinimlerini karsilar.
/// </summary>
internal sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _clock;
    private readonly SigningCredentials _signingCredentials;

    public TokenService(IOptions<JwtOptions> options, IDateTimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;

        // Imzalama anahtarini bir kez olusturup saklıyorum.
        //
        // Her token uretiminde yeniden olusturmak, her seferinde byte
        // dizisi ayirmak ve kriptografi nesnesi kurmak demek olurdu.
        // Login yogun bir endpoint; bu küçük fark toplamda hissedilir.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public AccessToken CreateAccessToken(Guid userId, string email, IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var now = _clock.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            // "sub" (subject) = token'in sahibi. Standart JWT claim'i.
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),

            new(JwtRegisteredClaimNames.Email, email),

            // "jti" (JWT ID) = bu token'in benzersiz kimliği.
            //
            // Neden gerekli? Ileride token kara listesi (blacklist)
            // uygulamak istersek, iptal edilen token'in jti degerini
            // Redis'e yazip her istekte kontrol edebiliriz.
            // Simdiden koymak, sonradan eklemekten çok daha kolay:
            // aksi halde eski token'larda bu alan olmazdi.
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N"))
        };

        // Roller ayrı ayrı claim olarak ekleniyor.
        // [Authorize(Roles = "Admin")] bu claim'lere bakacak.
        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,

            // UtcDateTime kullanıyorum: JWT spec'i zamanlari Unix epoch
            // (UTC) olarak saklar. DateTimeOffset'i doğrudan verseydim
            // kutuphane yine cevirirdi ama acikca yazmak, saat dilimi
            // hatalarina karsi niyeti belgeliyor.
            NotBefore = _clock.UtcNow.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            SigningCredentials = _signingCredentials
        };

        // JsonWebTokenHandler, eski JwtSecurityTokenHandler'a göre
        // belirgin şekilde daha hizli ve daha az bellek kullaniyor.
        // Microsoft yeni projelerde bunu oneriyor.
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new AccessToken(token, expiresAt);
    }

    public RefreshTokenResult CreateRefreshToken()
    {
        // kriptografik rastgelelik -- Random sinifi kullanilmaz
        //
        // System.Random tahmin edilebilir bir dizidir: tohumunu (seed)
        // bilen veya birkaç ciktisini goren biri sonraki değerleri
        // hesaplayabilir.
        //
        // Refresh token pratikte sifreye esdegerdir. Tahmin edilebilir
        // olsaydı saldirgan geçerli token uretip herkesin hesabina girerdi.
        //
        // RandomNumberGenerator isletim sisteminin kriptografik
        // rastgelelik kaynagini kullanir.
        //
        // 64 byte = 512 bit entropi. Kaba kuvvetle tahmin edilmesi
        // fiziksel olarak imkansiz.
        var bytes = RandomNumberGenerator.GetBytes(64);

        // Base64Url: '+', '/' ve '=' karakterlerini kullanmaz.
        // Bu karakterler URL'de ve HTTP header'inda ozel anlam tasir;
        // kacis (escaping) gerektirir ve hatalara yol acar.
        var value = Base64UrlEncoder.Encode(bytes);

        return new RefreshTokenResult(
            Value: value,
            HashValue: HashRefreshToken(value),
            ExpiresAt: _clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    public string HashRefreshToken(string refreshToken)
    {
        // neden SHA-256, neden BCrypt değil?
        //
        // Şifreler için BCrypt kullanıyorum çünkü sifreler TAHMIN
        // EDILEBILIR ("123456", "şifre123"). Yavas algoritma, sozluk
        // saldirisini pratikte imkansiz kilar.
        //
        // Refresh token ise 512 bit RASTGELE bir degerdir. Sozluk
        // saldirisina konu olamaz -- tahmin edilecek bir kalip yok.
        // Yavas algoritma kullanmak sadece her token yenilemesini
        // yavaslatirdi, hiçbir güvenlik kazandirmazdi.
        //
        // SHA-256 burada doğru tercih: hizli ve geri cevrilemez.
        //
        // Salt kullanmiyorum çünkü aynı sebeple gereksiz: salt'in amaci
        // aynı girdinin aynı hash'i uretmesini engellemektir; rastgele
        // token'larda zaten aynı girdi iki kez olusmaz.
        // Ayrıca salt'siz olmasını, gelen token'i doğrudan hash'leyip
        // veritabaninda ARAYABILMEMIZI sagliyor (index kullanilabiliyor).
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));

        return Convert.ToHexString(bytes);
    }
}

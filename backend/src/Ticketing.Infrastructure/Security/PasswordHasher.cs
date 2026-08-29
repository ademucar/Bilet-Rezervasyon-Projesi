using Ticketing.Application.Abstractions.Security;

namespace Ticketing.Infrastructure.Security;

/// <summary>
/// BCrypt tabanli şifre hash'leme.
///
/// PDF Sprint 3: "Şifreler güvenli bicimde hashlenmelidir."
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// BCrypt maliyet faktoru (work factor).
    ///
    /// ==================================================================
    /// BU SAYI NEDEN ONEMLI?
    /// ==================================================================
    /// BCrypt kasitli olarak YAVAS bir algoritmadir. Maliyet faktoru her
    /// 1 arttiginda hesaplama süresi IKIYE KATLANIR (2^12 = 4096 tur).
    ///
    /// Neden yavaslik istiyoruz? Çünkü veritabani sizarsa saldirgan
    /// hash'leri kaba kuvvetle kirmaya çalışır. SHA-256 gibi HIZLI bir
    /// algoritma kullansaydık, modern bir GPU saniyede milyarlarca
    /// deneme yapabilirdi. BCrypt ile bu sayi saniyede birkaç bine duser.
    ///
    /// 12 değeri, 2024-2026 donanimlarinda yaklasik 250-400 ms surer:
    ///   - Kullanıcı için fark edilmez (girişte bir kez).
    ///   - Saldirgan için kirma suresini yillara cikarir.
    ///
    /// DIKKAT: Bu deger çok yüksek olursa (örneğin 16) login endpoint'i
    /// kendi başına bir DoS acigina donusur -- saldirgan yuzlerce
    /// esZamanli login denemesiyle CPU'yu tuketebilir.
    ///
    /// Neden appsettings'ten okumuyorum? Çünkü bu deger hash'in ICINE
    /// gomulur. Yarin 13'e cikarirsak eski hash'ler yine 12 ile
    /// dogrulanmaya devam eder; kullanıcı sifresini degistirdiginde
    /// yeni maliyetle yeniden hash'lenir. Yani degistirmek güvenli ama
    /// yapilandirmadan değil, bilinçli bir kod degisikligiyle olmalı.
    /// </summary>
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Şifre boş olamaz.", nameof(password));
        }

        // BCrypt her hash için RASTGELE bir "salt" üretir ve önü hash'in
        // icine gomer. Bu yüzden aynı şifre iki kez hash'lendiginde
        // FARKLI ciktilar verir.
        //
        // Neden önemli? Salt olmasaydı, aynı sifreyi kullanan iki kullanıcı
        // aynı hash'e sahip olurdu. Saldirgan onceden hesaplanmis bir
        // tablo (rainbow table) ile ikisini birden kirardi.
        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            // BCrypt.Verify, maliyet faktorunu ve salt'i hash'in icinden
            // okur; bize ayrıca saklamamiza gerek yok.
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Veritabanindaki hash bozuksa veya farklı bir formattaysa.
            //
            // Exception'i disari SIZDIRMIYORUM: cagiran taraf için
            // "şifre yanlış" ile "hash bozuk" aynı sonuca varmali --
            // giriş başarısız. Ayrimi disari vermek, saldirgana hesap
            // hakkında bilgi verir.
            return false;
        }
    }
}

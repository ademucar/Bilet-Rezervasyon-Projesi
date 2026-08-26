using Ticketing.Application.Abstractions.Security;

namespace Ticketing.Infrastructure.Security;

/// <summary>
/// BCrypt tabanli sifre hash'leme.
///
/// PDF Sprint 3: "Sifreler guvenli bicimde hashlenmelidir."
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
    /// 1 arttiginda hesaplama suresi IKIYE KATLANIR (2^12 = 4096 tur).
    ///
    /// Neden yavaslik istiyoruz? Cunku veritabani sizarsa saldirgan
    /// hash'leri kaba kuvvetle kirmaya calisir. SHA-256 gibi HIZLI bir
    /// algoritma kullansaydik, modern bir GPU saniyede milyarlarca
    /// deneme yapabilirdi. BCrypt ile bu sayi saniyede birkac bine duser.
    ///
    /// 12 degeri, 2024-2026 donanimlarinda yaklasik 250-400 ms surer:
    ///   - Kullanici icin fark edilmez (giriste bir kez).
    ///   - Saldirgan icin kirma suresini yillara cikarir.
    ///
    /// DIKKAT: Bu deger cok yuksek olursa (ornegin 16) login endpoint'i
    /// kendi basina bir DoS acigina donusur -- saldirgan yuzlerce
    /// esZamanli login denemesiyle CPU'yu tuketebilir.
    ///
    /// Neden appsettings'ten okumuyorum? Cunku bu deger hash'in ICINE
    /// gomulur. Yarin 13'e cikarirsak eski hash'ler yine 12 ile
    /// dogrulanmaya devam eder; kullanici sifresini degistirdiginde
    /// yeni maliyetle yeniden hash'lenir. Yani degistirmek guvenli ama
    /// yapilandirmadan degil, bilincli bir kod degisikligiyle olmali.
    /// </summary>
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Sifre bos olamaz.", nameof(password));
        }

        // BCrypt her hash icin RASTGELE bir "salt" uretir ve onu hash'in
        // icine gomer. Bu yuzden ayni sifre iki kez hash'lendiginde
        // FARKLI ciktilar verir.
        //
        // Neden onemli? Salt olmasaydi, ayni sifreyi kullanan iki kullanici
        // ayni hash'e sahip olurdu. Saldirgan onceden hesaplanmis bir
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
            // okur; bize ayrica saklamamiza gerek yok.
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // Veritabanindaki hash bozuksa veya farkli bir formattaysa.
            //
            // Exception'i disari SIZDIRMIYORUM: cagiran taraf icin
            // "sifre yanlis" ile "hash bozuk" ayni sonuca varmali --
            // giris basarisiz. Ayrimi disari vermek, saldirgana hesap
            // hakkinda bilgi verir.
            return false;
        }
    }
}

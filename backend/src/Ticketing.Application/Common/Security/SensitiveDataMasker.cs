using System.Text.RegularExpressions;

namespace Ticketing.Application.Common.Security;

/// <summary>
/// Loglara ve hata mesajlarina hassas veri sizmasini engeller.
/// PDF Sprint 15: "Hassas veri maskeleme".
/// </summary>
/// <remarks>
/// Bu sinif neden gerekli? -- somut sizinti yollari
///
/// Loglar çoğu zaman "güvenli" sanilir ama degildir:
///
///   - Log dosyalari yedeklenir ve yedekler başka yerde durur
///   - Merkezi log sistemlerine (Seq, ELK) gonderilir ve oraya
///     gelistirici disindaki kisiler de erisir
///   - Hata ayiklama sırasında ekran goruntusu alinip paylasilir
///   - Destek talebine eklenir
///
/// Bir JWT veya şifre sıfırlama token'i loga duserse, ona erisen
/// herkes o kullanıcının hesabina girebilir.
///
/// Ne maskeleniyor?
///
///   JWT              -> oturum ele gecirme
///   Şifre alanlari   -> doğrudan hesap erişimi
///   Kart numarasi    -> PCI-DSS ihlali (simulasyonda yok ama
///                       gerçek entegrasyonda gelebilir)
///   E-posta          -> KISMEN maskeleniyor (aşağıda gerekce)
/// </remarks>
public static partial class SensitiveDataMasker
{
    private const string Maske = "***MASKELENDI***";

    // NEDEN [GeneratedRegex]?
    //
    // Regex'ler derleme zamaninda kaynak ureteci ile oluşturuluyor.
    //
    // new Regex(...) her cagrida yeniden ayristirir; static readonly
    // Regex ise calisma zamaninda derlenir. GeneratedRegex ise C# kodu
    // olarak URETILIYOR -- en hizlisi ve tahsis yapmiyor.
    //
    // Maskeleme her log satirinda calisabilecegi için bu önemli.

    /// <summary>JWT: uc bolumlu, nokta ile ayrilmis Base64.</summary>
    [GeneratedRegex(
        @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex JwtRegex();

    /// <summary>JSON icindeki şifre benzeri alanlar.</summary>
    /// <remarks>
    /// Alan adlarini GENIS tutuyorum: password, currentPassword,
    /// newPassword, token, refreshToken, secret, apiKey.
    ///
    /// Fazla maskelemek, eksik maskelemekten iyidir: yanlislikla
    /// maskelenen zararsiz bir alan yalnızca hata ayiklamayi
    /// zorlastirir; kacirilan bir token ise hesap kaybi demektir.
    /// </remarks>
    [GeneratedRegex(
        @"(""(?:\w*[Pp]assword|[Tt]oken|[Ss]ecret|[Aa]pi[Kk]ey|refreshToken)""\s*:\s*)""[^""]*""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex JsonSecretRegex();

    /// <summary>Kart numarasi bicimi (13-19 hane, arada bosluk/tire olabilir).</summary>
    [GeneratedRegex(
        @"\b(?:\d[ -]*?){13,19}\b",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex CardRegex();

    /// <summary>
    /// Metindeki hassas verileri maskeler.
    /// </summary>
    public static string Mask(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var sonuc = JwtRegex().Replace(input, Maske);

        // $1 İle alan adini koruyor, yalnızca degeri maskeliyorum.
        //
        // Alan adını da silseydim logdan "hangi alan vardi" bilgisi
        // kaybolurdu ve hata ayiklamak imkansizlasirdi.
        sonuc = JsonSecretRegex().Replace(sonuc, @"$1""" + Maske + @"""");

        sonuc = CardRegex().Replace(sonuc, Maske);

        return sonuc;
    }

    /// <summary>
    /// E-postayi KISMEN maskeler: "adem@ornek.com" -> "ade***@ornek.com"
    /// </summary>
    /// <remarks>
    /// Neden tamamen gizlemiyoruz?
    ///
    /// E-posta kisisel veri (KVKK/GDPR kapsaminda) ama aynı zamanda
    /// destek ve hata ayiklama için GEREKLI: "hangi kullanıcı?"
    /// sorusunun en pratik cevabi.
    ///
    /// Tamamen maskeleseydik loglar destek için kullanilamaz hale
    /// gelirdi ve gelistiriciler maskelemeyi kapatmanin yolunu
    /// ararlardi -- ki bu daha kötü bir sonuç.
    ///
    /// İlk uc harf + alan adı, destek için yeterli ipucu veriyor ama
    /// adresi TOPLU olarak toplamayi (spam listesi olusturmayi)
    /// engelliyor.
    /// </remarks>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "-";
        }

        var atIndex = email.IndexOf('@', StringComparison.Ordinal);

        // @ yoksa geçerli bir e-posta değil; tamamen maskele.
        //
        // Kismi maskeleme mantığı burada calismaz ve yanlislikla
        // tamamini loglamaktansa hiçbir sey loglamak daha güvenli.
        if (atIndex <= 0)
        {
            return Maske;
        }

        var yerelKisim = email[..atIndex];
        var alanAdi = email[atIndex..];

        // Çok kisa yerel kisimlarda (a@x.com) ilk uc harf zaten
        // tamamini açık eder. O durumda tek harf birakiyorum.
        var gorunenUzunluk = yerelKisim.Length <= 3 ? 1 : 3;

        return $"{yerelKisim[..gorunenUzunluk]}***{alanAdi}";
    }
}

using System.Text.RegularExpressions;

namespace Ticketing.Application.Common.Security;

/// <summary>
/// Loglara ve hata mesajlarina hassas veri sizmasini engeller.
/// PDF Sprint 15: "Hassas veri maskeleme".
/// </summary>
/// <remarks>
/// ==================================================================
/// BU SINIF NEDEN GEREKLI? -- SOMUT SIZINTI YOLLARI
/// ==================================================================
/// Loglar cogu zaman "guvenli" sanilir ama degildir:
///
///   - Log dosyalari yedeklenir ve yedekler baska yerde durur
///   - Merkezi log sistemlerine (Seq, ELK) gonderilir ve oraya
///     gelistirici disindaki kisiler de erisir
///   - Hata ayiklama sirasinda ekran goruntusu alinip paylasilir
///   - Destek talebine eklenir
///
/// Bir JWT veya sifre sifirlama token'i loga duserse, ona erisen
/// herkes o kullanicinin hesabina girebilir.
///
/// ------------------------------------------------------------------
/// NE MASKELENIYOR?
/// ------------------------------------------------------------------
///   JWT              -> oturum ele gecirme
///   Sifre alanlari   -> dogrudan hesap erisimi
///   Kart numarasi    -> PCI-DSS ihlali (simulasyonda yok ama
///                       gercek entegrasyonda gelebilir)
///   E-posta          -> KISMEN maskeleniyor (asagida gerekce)
/// ------------------------------------------------------------------
/// </remarks>
public static partial class SensitiveDataMasker
{
    private const string Maske = "***MASKELENDI***";

    // ==================================================================
    // NEDEN [GeneratedRegex]?
    // ==================================================================
    // Regex'ler derleme zamaninda kaynak ureteci ile olusturuluyor.
    //
    // new Regex(...) her cagrida yeniden ayristirir; static readonly
    // Regex ise calisma zamaninda derlenir. GeneratedRegex ise C# kodu
    // olarak URETILIYOR -- en hizlisi ve tahsis yapmiyor.
    //
    // Maskeleme HER LOG SATIRINDA calisabilecegi icin bu onemli.
    // ==================================================================

    /// <summary>JWT: uc bolumlu, nokta ile ayrilmis Base64.</summary>
    [GeneratedRegex(
        @"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex JwtRegex();

    /// <summary>JSON icindeki sifre benzeri alanlar.</summary>
    /// <remarks>
    /// Alan adlarini GENIS tutuyorum: password, currentPassword,
    /// newPassword, token, refreshToken, secret, apiKey.
    ///
    /// Fazla maskelemek, eksik maskelemekten iyidir: yanlislikla
    /// maskelenen zararsiz bir alan yalnizca hata ayiklamayi
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

        // $1 ile alan ADINI koruyor, yalnizca DEGERI maskeliyoruz.
        //
        // Alan adini da silseydik logdan "hangi alan vardi" bilgisi
        // kaybolurdu ve hata ayiklamak imkansizlasirdi.
        sonuc = JsonSecretRegex().Replace(sonuc, @"$1""" + Maske + @"""");

        sonuc = CardRegex().Replace(sonuc, Maske);

        return sonuc;
    }

    /// <summary>
    /// E-postayi KISMEN maskeler: "adem@ornek.com" -> "ade***@ornek.com"
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// NEDEN TAMAMEN GIZLEMIYORUZ?
    /// ==============================================================
    /// E-posta kisisel veri (KVKK/GDPR kapsaminda) ama ayni zamanda
    /// destek ve hata ayiklama icin GEREKLI: "hangi kullanici?"
    /// sorusunun en pratik cevabi.
    ///
    /// Tamamen maskeleseydik loglar destek icin kullanilamaz hale
    /// gelirdi ve gelistiriciler maskelemeyi kapatmanin yolunu
    /// ararlardi -- ki bu daha kotu bir sonuc.
    ///
    /// Ilk uc harf + alan adi, destek icin yeterli ipucu veriyor ama
    /// adresi TOPLU olarak toplamayi (spam listesi olusturmayi)
    /// engelliyor.
    /// ==============================================================
    /// </remarks>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "-";
        }

        var atIndex = email.IndexOf('@', StringComparison.Ordinal);

        // @ yoksa gecerli bir e-posta degil; tamamen maskele.
        //
        // Kismi maskeleme mantigi burada calismaz ve yanlislikla
        // tamamini loglamaktansa hicbir sey loglamak daha guvenli.
        if (atIndex <= 0)
        {
            return Maske;
        }

        var yerelKisim = email[..atIndex];
        var alanAdi = email[atIndex..];

        // Cok kisa yerel kisimlarda (a@x.com) ilk uc harf zaten
        // tamamini acik eder. O durumda tek harf birakiyoruz.
        var gorunenUzunluk = yerelKisim.Length <= 3 ? 1 : 3;

        return $"{yerelKisim[..gorunenUzunluk]}***{alanAdi}";
    }
}

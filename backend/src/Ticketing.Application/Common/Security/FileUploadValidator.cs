using System.Globalization;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Common.Security;

/// <summary>
/// Yuklenen dosyalari dogrular.
/// PDF Sprint 15: "File type kontrolu", "MIME type kontrolu",
/// "Guvenli dosya adi".
/// </summary>
/// <remarks>
/// ==================================================================
/// UC KONTROL VAR VE UCU DE GEREKLI -- BIRI DIGERININ YERINE GECMEZ
/// ==================================================================
/// Dosya yukleme, bir web uygulamasindaki EN TEHLIKELI ozelliktir:
/// kullanicinin sunucumuza VERI degil DOSYA yazmasina izin veriyoruz.
///
///   1) UZANTI (file type)  -- kullanici verir, KOLAYCA yalan soyler
///   2) MIME type           -- tarayici/istemci verir, YINE yalan
///   3) IMZA (magic number) -- dosyanin ICERIGI, yalan soyleyemez
///
/// Neden hepsi lazim?
///
///   Yalnizca (1): "zararli.exe" dosyasini "afis.jpg" diye yeniden
///   adlandirmak bir saniye surer.
///
///   Yalnizca (2): Content-Type basligini istemci gonderiyor. curl
///   ile istedigini yazabilir.
///
///   Yalnizca (3): imza dogru olsa bile uzanti .html ise, dosya
///   sunuldugunda tarayici HTML olarak calistirabilir. Ayrica
///   "polyglot" dosyalar hem gecerli JPEG hem gecerli script olabilir.
///
/// Ucu birden: uzanti VE MIME VE icerik AYNI turu gostermeli.
/// Uyusmazlik varsa reddediyoruz -- mesru kullanicida bu uc bilgi
/// zaten uyusur; uyusmuyorsa ya bozuk ya kotu niyetli.
/// ==================================================================
/// </remarks>
public static class FileUploadValidator
{
    /// <summary>
    /// Izin verilen dosya turleri: uzanti -> beklenen MIME turleri.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// BEYAZ LISTE, KARA LISTE DEGIL
    /// ==============================================================
    /// "Sunlar yasak" (kara liste) yazmak cazip ama YANLIS: unuttugun
    /// her uzanti bir aciktir. .exe engellersin, .bat unutursun;
    /// .php engellersin, .phtml unutursun.
    ///
    /// Beyaz listede unutmanin bedeli yalnizca "bu dosya turu
    /// desteklenmiyor" hatasidir -- guvenlik acigi degil.
    ///
    /// SVG BILINCLI OLARAK YOK: SVG bir XML belgesidir ve icine
    /// script etiketi gomulebilir. Tarayicida acildiginda o script
    /// BIZIM alan adimizda calisir (saklanmis XSS). "Resim" gibi
    /// gorunmesi aldaticidir.
    /// ==============================================================
    /// </remarks>
    private static readonly Dictionary<string, string[]> IzinliTurler =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = ["image/jpeg"],
            [".jpeg"] = ["image/jpeg"],
            [".png"] = ["image/png"],
            [".webp"] = ["image/webp"],
            [".pdf"] = ["application/pdf"],
        };

    /// <summary>
    /// Dosya imzalari (magic number): uzanti -> dosyanin ilk baytlari.
    /// </summary>
    /// <remarks>
    /// Bu baytlar dosya bicimi standardinin parcasidir ve dosyanin EN
    /// BASINDA bulunur. Kullanici bunu degistirirse dosya artik
    /// gecerli bir resim olmaz -- yani yalan soylemek, dosyayi bozmak
    /// anlamina geliyor. Iste bu yuzden guvenilir.
    /// </remarks>
    private static readonly Dictionary<string, byte[]> Imzalar =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // JPEG: FF D8 FF
            [".jpg"] = [0xFF, 0xD8, 0xFF],
            [".jpeg"] = [0xFF, 0xD8, 0xFF],

            // PNG: 89 "PNG" CR LF 1A LF
            [".png"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],

            // WebP: "RIFF" ile baslar. Bu imza WAV ve AVI ile ORTAK --
            // bu yuzden asagida 8. bayttan itibaren "WEBP" de aranyor.
            [".webp"] = [0x52, 0x49, 0x46, 0x46],

            // PDF: "%PDF-"
            [".pdf"] = [0x25, 0x50, 0x44, 0x46, 0x2D],
        };

    /// <summary>Izin verilen en buyuk dosya boyutu: 5 MB.</summary>
    /// <remarks>
    /// Etkinlik afisi icin 5 MB fazlasiyla yeterli. Sinir olmasaydi
    /// saldirgan diski doldurup uygulamayi durdurabilirdi -- ve bu
    /// KALICI bir hasar olurdu: bellek gibi kendiliginden bosalmaz,
    /// birinin gidip elle silmesi gerekir.
    /// </remarks>
    public const long MaksimumBoyut = 5 * 1024 * 1024;

    /// <summary>
    /// Imza kontrolu icin okumamiz gereken bayt sayisi.
    /// En uzun imza 8 bayt; WebP dogrulamasi 12. bayta kadar bakiyor.
    /// </summary>
    public const int ImzaIcinGerekenBayt = 12;

    /// <summary>Swagger ve istemci icin izin verilen uzantilar.</summary>
    public static IReadOnlyCollection<string> IzinliUzantilar => IzinliTurler.Keys;

    /// <summary>
    /// Uzanti + MIME + icerik imzasini BIRLIKTE dogrular ve basarili
    /// olursa uretilmis guvenli dosya adini doner.
    /// </summary>
    /// <param name="fileName">Kullanicidan gelen orijinal dosya adi.</param>
    /// <param name="contentType">Istemcinin bildirdigi MIME turu.</param>
    /// <param name="sizeInBytes">Dosya boyutu.</param>
    /// <param name="ilkBaytlar">Dosyanin ilk baytlari.</param>
    public static Result<string> Dogrula(
        string? fileName,
        string? contentType,
        long sizeInBytes,
        ReadOnlySpan<byte> ilkBaytlar)
    {
        // ----------------------------------------------------------
        // 0) Boyut
        // ----------------------------------------------------------
        if (sizeInBytes <= 0)
        {
            return Result.Failure<string>(Error.Validation(
                "file.empty", "Bos dosya yuklenemez."));
        }

        if (sizeInBytes > MaksimumBoyut)
        {
            return Result.Failure<string>(Error.Validation(
                "file.too_large",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Dosya boyutu en fazla {0} MB olabilir.",
                    MaksimumBoyut / (1024 * 1024))));
        }

        // ----------------------------------------------------------
        // 1) FILE TYPE (uzanti) -- PDF maddesi
        // ----------------------------------------------------------
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result.Failure<string>(Error.Validation(
                "file.name_required", "Dosya adi gereklidir."));
        }

        // ==========================================================
        // ONCE GetFileName, SONRA GetExtension
        // ==========================================================
        // Kullanici "../../appsettings.json.jpg" gonderebilir.
        // GetFileName once dizin kismini atiyor -- dizin gecisi
        // (path traversal) saldirisina karsi ilk siperimiz.
        //
        // Ikinci siper, asagida uretilen dosya adi. Iki siper de
        // olmali: birincisi platformlar arasinda farkli davraniyor
        // (ters bolu Windows'ta ayirici, Linux'ta gecerli bir
        // dosya adi karakteri).
        // ==========================================================
        var guvenliAd = Path.GetFileName(fileName);
        var uzanti = Path.GetExtension(guvenliAd);

        if (string.IsNullOrEmpty(uzanti)
            || !IzinliTurler.TryGetValue(uzanti, out var izinliMimeler))
        {
            return Result.Failure<string>(Error.Validation(
                "file.type_not_allowed",
                "Bu dosya turu desteklenmiyor. Izin verilenler: "
                    + string.Join(", ", IzinliTurler.Keys)));
        }

        // ----------------------------------------------------------
        // 2) MIME TYPE -- PDF maddesi
        // ----------------------------------------------------------
        // Uzanti ile bildirilen MIME turu UYUSMALI.
        //
        // "afis.jpg" adiyla application/x-msdownload gonderen bir
        // istek, en hafif tabiriyle supheli.
        if (string.IsNullOrWhiteSpace(contentType)
            || !izinliMimeler.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(Error.Validation(
                "file.mime_mismatch",
                "Dosya turu ile icerik turu uyusmuyor."));
        }

        // ----------------------------------------------------------
        // 3) ICERIK IMZASI -- uzanti ve MIME yalanini yakalar
        // ----------------------------------------------------------
        // PDF bu maddeyi acikca istemiyor ama ilk ikisi TEK BASINA
        // neredeyse hicbir sey ifade etmiyor: ikisini de kullanici
        // gonderiyor.
        //
        // Bu kontrol olmadan "zararli.exe" -> "afis.jpg" olarak
        // yeniden adlandirilip Content-Type: image/jpeg ile
        // gonderilebilir ve ilk iki kontrolden de gecerdi.
        if (!ImzaUyuyorMu(uzanti, ilkBaytlar))
        {
            return Result.Failure<string>(Error.Validation(
                "file.content_mismatch",
                "Dosya icerigi, belirtilen dosya turuyle uyusmuyor."));
        }

        // ----------------------------------------------------------
        // 4) GUVENLI DOSYA ADI URET -- PDF maddesi
        // ----------------------------------------------------------
        // ==========================================================
        // KULLANICININ ADINI "TEMIZLEMIYORUZ", TAMAMEN ATIYORUZ
        // ==========================================================
        // Yaygin yaklasim, adi temizlemektir (tehlikeli karakterleri
        // silmek). Bu bir kedi-fare oyunu; her zaman kacirilan bir
        // durum vardir:
        //   "afis.jpg.exe"       cift uzanti
        //   "CON", "PRN", "NUL"  Windows ayrilmis adlari
        //   ustuste URL kodlamasi
        //   gorsel olarak ayni gorunen Unicode karakterler
        //
        // Guid uretmek bu SINIFIN TAMAMINI ortadan kaldiriyor:
        // kullanicidan gelen metin dosya yolunda HIC kullanilmiyor.
        // Yani "acaba her durumu yakaladim mi?" sorusunu sormaya
        // gerek kalmiyor.
        //
        // Orijinal ad yine de veritabaninda saklaniyor (indirirken
        // kullaniciya gosterebilmek icin) ama DISKE hic yazilmiyor.
        // ==========================================================
        var uretilenAd = string.Create(
            CultureInfo.InvariantCulture,
            $"{Guid.NewGuid():N}{uzanti.ToLowerInvariant()}");

        return Result.Success(uretilenAd);
    }

    private static bool ImzaUyuyorMu(string uzanti, ReadOnlySpan<byte> baytlar)
    {
        if (!Imzalar.TryGetValue(uzanti, out var beklenen))
        {
            // Beyaz listede olup imzasi tanimlanmamis bir tur.
            // Guvenli taraf: REDDET.
            //
            // "Bilmiyorsam gecir" deseydik, beyaz listeye yeni bir tur
            // eklerken imzasini yazmayi unutan gelistirici (yani
            // gelecekteki ben) sessizce bir acik birakirdi.
            return false;
        }

        if (baytlar.Length < beklenen.Length
            || !baytlar[..beklenen.Length].SequenceEqual(beklenen))
        {
            return false;
        }

        // WebP ozel durumu: "RIFF" baslangici WAV ve AVI bicimlerinde
        // de var. Gercekten WebP oldugunu 8. bayttan itibaren "WEBP"
        // yazisiyla dogruluyoruz.
        if (uzanti.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return baytlar.Length >= 12
                && baytlar[8] == 0x57   // W
                && baytlar[9] == 0x45   // E
                && baytlar[10] == 0x42  // B
                && baytlar[11] == 0x50; // P
        }

        return true;
    }
}

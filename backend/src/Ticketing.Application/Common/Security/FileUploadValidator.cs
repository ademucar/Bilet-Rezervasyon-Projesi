using System.Globalization;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Common.Security;

/// <summary>
/// Yuklenen dosyalari dogrular.
/// PDF Sprint 15: "File type kontrolü", "MIME type kontrolü",
/// "Guvenli dosya adı".
/// </summary>
/// <remarks>
/// Uc kontrol var ve ucu de gerekli -- biri digerinin yerine gecmez
///
/// Dosya yukleme, bir web uygulamasindaki en tehlikeli ozelliktir:
/// Kullanıcının sunucumuza veri değil dosya yazmasina izin veriyorum.
///
///   1) Uzanti (file type)  -- kullanıcı verir, kolayca yalan söyler
///   2) MIME type           -- tarayıcı/istemci verir, YINE yalan
///   3) imza (magic number) -- dosyanin icerigi, yalan soyleyemez
///
/// Neden hepsi lazim?
///
///   Yalnızca (1): "zararli.exe" dosyasini "afis.jpg" diye yeniden
///   adlandirmak bir saniye surer.
///
///   Yalnızca (2): Content-Type basligini istemci gönderiyor. curl
///   ile istedigini yazabilir.
///
///   Yalnızca (3): imza doğru olsa bile uzanti .html ise, dosya
///   sunuldugunda tarayıcı HTML olarak calistirabilir. Ayrıca
///   "polyglot" dosyalar hem geçerli JPEG hem geçerli script olabilir.
///
/// Ucu birden: uzanti ve MIME ve içerik ayni türü gostermeli.
/// Uyusmazlik varsa reddediyoruz -- mesru kullanicida bu uc bilgi
/// zaten uyusur; uyusmuyorsa ya bozuk ya kötü niyetli.
/// </remarks>
public static class FileUploadValidator
{
    /// <summary>
    /// Izin verilen dosya türleri: uzanti -> beklenen MIME türleri.
    /// </summary>
    /// <remarks>
    /// Beyaz liste, kara liste değil
    ///
    /// "Sunlar yasak" (kara liste) yazmak cazip ama YANLIS: unuttugun
    /// her uzanti bir aciktir. .exe engellersin, .bat unutursun;
    /// .php engellersin, .phtml unutursun.
    ///
    /// Beyaz listede unutmanin bedeli yalnızca "bu dosya türü
    /// desteklenmiyor" hatasidir -- güvenlik acigi değil.
    ///
    /// SVG bilincli olarak yok: SVG bir XML belgesidir ve icine
    /// script etiketi gomulebilir. Tarayicida acildiginda o script
    /// BENIM alan adimda çalışır (saklanmis XSS). "Resim" gibi
    /// görünmesi aldaticidir.
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
    /// BASINDA bulunur. Kullanıcı bunu degistirirse dosya artık
    /// geçerli bir resim olmaz -- yani yalan söylemek, dosyayı bozmak
    /// anlamina geliyor. Iste bu yüzden guvenilir.
    /// </remarks>
    private static readonly Dictionary<string, byte[]> Imzalar =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // JPEG: FF D8 FF
            [".jpg"] = [0xFF, 0xD8, 0xFF],
            [".jpeg"] = [0xFF, 0xD8, 0xFF],

            // PNG: 89 "PNG" CR LF 1A LF
            [".png"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],

            // WebP: "RIFF" ile başlar. Bu imza WAV ve AVI ile ortak --
            // bu yüzden aşağıda 8. bayttan itibaren "WEBP" de aranyor.
            [".webp"] = [0x52, 0x49, 0x46, 0x46],

            // PDF: "%PDF-"
            [".pdf"] = [0x25, 0x50, 0x44, 0x46, 0x2D],
        };

    /// <summary>Izin verilen en büyük dosya boyutu: 5 MB.</summary>
    /// <remarks>
    /// Etkinlik afisi için 5 MB fazlasiyla yeterli. Sinir olmasaydı
    /// saldirgan diski doldurup uygulamayi durdurabilirdi -- ve bu
    /// KALICI bir hasar olurdu: bellek gibi kendiliginden bosalmaz,
    /// birinin gidip elle silmesi gerekir.
    /// </remarks>
    public const long MaksimumBoyut = 5 * 1024 * 1024;

    /// <summary>
    /// Imza kontrolü için okumamiz gereken bayt sayısı.
    /// En uzun imza 8 bayt; WebP dogrulamasi 12. bayta kadar bakiyor.
    /// </summary>
    public const int ImzaIcinGerekenBayt = 12;

    /// <summary>Swagger ve istemci için izin verilen uzantilar.</summary>
    public static IReadOnlyCollection<string> IzinliUzantilar => IzinliTurler.Keys;

    /// <summary>
    /// Uzanti + MIME + içerik imzasini BIRLIKTE dogrular ve başarılı
    /// olursa üretilmiş güvenli dosya adını döner.
    /// </summary>
    /// <param name="fileName">Kullanicidan gelen orijinal dosya adı.</param>
    /// <param name="contentType">Istemcinin bildirdigi MIME türü.</param>
    /// <param name="sizeInBytes">Dosya boyutu.</param>
    /// <param name="ilkBaytlar">Dosyanin ilk baytlari.</param>
    public static Result<string> Dogrula(
        string? fileName,
        string? contentType,
        long sizeInBytes,
        ReadOnlySpan<byte> ilkBaytlar)
    {
        // 0) Boyut
        if (sizeInBytes <= 0)
        {
            return Result.Failure<string>(Error.Validation(
                "file.empty", "Boş dosya yuklenemez."));
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

        // 1) File type (uzanti) -- PDF maddesi
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result.Failure<string>(Error.Validation(
                "file.name_required", "Dosya adı gereklidir."));
        }

        // Once GetFileName, sonra GetExtension
        //
        // Kullanıcı "../../appsettings.json.jpg" gonderebilir.
        // GetFileName önce dizin kismini atiyor -- dizin gecisi
        // (path traversal) saldirisina karsi ilk siperimiz.
        //
        // Ikinci siper, aşağıda uretilen dosya adı. Iki siper de
        // olmalı: birincisi platformlar arasında farklı davraniyor
        // (ters bolu Windows'ta ayirici, Linux'ta geçerli bir
        // dosya adı karakteri).
        var guvenliAd = Path.GetFileName(fileName);
        var uzanti = Path.GetExtension(guvenliAd);

        if (string.IsNullOrEmpty(uzanti)
            || !IzinliTurler.TryGetValue(uzanti, out var izinliMimeler))
        {
            return Result.Failure<string>(Error.Validation(
                "file.type_not_allowed",
                "Bu dosya türü desteklenmiyor. Izin verilenler: "
                    + string.Join(", ", IzinliTurler.Keys)));
        }

        // 2) MIME TYPE -- PDF maddesi
        //
        // Uzanti ile bildirilen MIME türü UYUSMALI.
        //
        // "afis.jpg" adiyla application/x-msdownload gonderen bir
        // istek, en hafif tabiriyle supheli.
        if (string.IsNullOrWhiteSpace(contentType)
            || !izinliMimeler.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<string>(Error.Validation(
                "file.mime_mismatch",
                "Dosya türü ile içerik türü uyuşmuyor."));
        }

        // 3) İcerik imzasi -- uzanti ve MIME yalanini yakalar
        //
        // PDF bu maddeyi acikca istemiyor ama ilk ikisi tek basina
        // neredeyse hiçbir sey ifade etmiyor: ikisini de kullanıcı
        // gönderiyor.
        //
        // Bu kontrol olmadan "zararli.exe" -> "afis.jpg" olarak
        // yeniden adlandirilip Content-Type: image/jpeg ile
        // gonderilebilir ve ilk iki kontrolden de gecerdi.
        if (!ImzaUyuyorMu(uzanti, ilkBaytlar))
        {
            return Result.Failure<string>(Error.Validation(
                "file.content_mismatch",
                "Dosya icerigi, belirtilen dosya turuyle uyuşmuyor."));
        }

        // 4) Guvenli dosya adi uret -- PDF maddesi
        //
        // Kullanicinin adini "temizlemiyoruz", tamamen atiyoruz
        //
        // Yaygin yaklasim, adı temizlemektir (tehlikeli karakterleri
        // silmek). Bu bir kedi-fare oyunu; her zaman kacirilan bir
        // durum vardir:
        //   "afis.jpg.exe"       cift uzanti
        //   "con", "prn", "nul"  Windows ayrilmis adları
        //   ustuste URL kodlamasi
        //   görsel olarak aynı görünen Unicode karakterler
        //
        // Guid uretmek bu sinifin tamamini ortadan kaldiriyor:
        // kullanicidan gelen metin dosya yolunda HİÇ kullanılmıyor.
        // Yani "acaba her durumu yakaladim mi?" sorusunu sormaya
        // gerek kalmiyor.
        //
        // Orijinal ad yine de veritabaninda saklaniyor (indirirken
        // kullanıcıya gosterebilmek için) ama DISKE hiç yazilmiyor.
        var uretilenAd = string.Create(
            CultureInfo.InvariantCulture,
            $"{Guid.NewGuid():N}{uzanti.ToLowerInvariant()}");

        return Result.Success(uretilenAd);
    }

    private static bool ImzaUyuyorMu(string uzanti, ReadOnlySpan<byte> baytlar)
    {
        if (!Imzalar.TryGetValue(uzanti, out var beklenen))
        {
            // Beyaz listede olup imzasi tanımlanmamış bir tur.
            // Guvenli taraf: REDDET.
            //
            // "Bilmiyorsam gecir" deseydim, beyaz listeye yeni bir tur
            // eklerken imzasini yazmayi unutan gelistirici (yani
            // gelecekteki ben) sessizce bir açık birakirdi.
            return false;
        }

        if (baytlar.Length < beklenen.Length
            || !baytlar[..beklenen.Length].SequenceEqual(beklenen))
        {
            return false;
        }

        // WebP ozel durumu: "RIFF" baslangici WAV ve AVI bicimlerinde
        // de var. Gercekten WebP olduğunu 8. bayttan itibaren "WEBP"
        // yazisiyla dogruluyorum.
        if (uzanti.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return baytlar.Length >= 12
                && baytlar[8] == 0x57 // W
                && baytlar[9] == 0x45 // E
                && baytlar[10] == 0x42 // B
                && baytlar[11] == 0x50; // P
        }

        return true;
    }
}

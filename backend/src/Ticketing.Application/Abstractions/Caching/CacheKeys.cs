using System.Globalization;

namespace Ticketing.Application.Abstractions.Caching;

/// <summary>
/// ==================================================================
/// ONBELLEK ANAHTAR STANDARDI -- PDF Sprint 11
/// ==================================================================
/// PDF kurali: "Cache key standardi olusturulmalidir."
///
/// STANDART:  {alan}:{varlik}:{ayirt-edici}
///
/// Ornekler:
///     ref:cities                     -> sehir listesi
///     ref:categories                 -> etkinlik kategorileri
///     event:detail:{id}              -> etkinlik detayi
///     event:popular:{adet}           -> populer etkinlikler
///     layout:{id}                    -> salon oturma plani
///
/// ------------------------------------------------------------------
/// NEDEN STANDART SART? -- Iki somut sorun
/// ------------------------------------------------------------------
/// 1) CAKISMA: Anahtarlar elle yazilsaydi, birinin "event:123" digerinin
///    "events:123" yazmasi kacinilmazdi. Ikisi FARKLI anahtar olur;
///    biri temizlenir digeri bayat kalir. Kullanici bazen guncel bazen
///    eski veri gorur -- teshis edilmesi cok zor bir hata.
///
/// 2) TEMIZLEME: Onek olmadan "bu etkinlige ait tum anahtarlari sil"
///    demek imkansizdir. Iki nokta ust uste ile bolumlenmis hiyerarsi,
///    onek bazli silmeyi mumkun kiliyor.
///
/// Redis'te iki nokta ust uste (:) ayirici olarak YERLESIK bir gelenek;
/// RedisInsight gibi araclar anahtarlari bu ayirica gore agac olarak
/// gosteriyor.
///
/// ------------------------------------------------------------------
/// BURADA OLMAYAN SEY: KULLANICIYA OZEL ANAHTARLAR
/// ------------------------------------------------------------------
/// PDF kurali: "Kullaniciya ozel hassas veriler ortak cache icinde
/// tutulmamalidir."
///
/// Bu sinifta bilerek TEK BIR kullanici bazli anahtar yok. Rezervasyon,
/// bilet, odeme ve bildirim sorgulari HIC onbelleklenmiyor.
///
/// Sebep sadece gizlilik degil, DOGRULUK da: rezervasyon durumu
/// saniyeler icinde degisiyor. Bir saniye bile bayat veri, kullanicinin
/// suresi dolmus bir rezervasyona odeme yapmaya calismasi demek.
/// ==================================================================
/// </summary>
public static class CacheKeys
{
    // ---- Referans verileri (nadiren degisir) ----

    public const string Cities = "ref:cities";

    public const string Categories = "ref:categories";

    // ---- Etkinlik ----

    /// <summary>Tum etkinlik anahtarlarinin oneki. Toplu temizleme icin.</summary>
    public const string EventPrefix = "event:";

    public static string EventDetail(Guid eventId)
        => string.Create(CultureInfo.InvariantCulture, $"event:detail:{eventId}");

    public static string PopularEvents(int count)
        => string.Create(CultureInfo.InvariantCulture, $"event:popular:{count}");

    // ---- Salon oturma plani ----

    public const string SeatLayoutPrefix = "layout:";

    /// <summary>
    /// Salonun oturma PLANI -- koltuk UYGUNLUGU degil.
    /// </summary>
    /// <remarks>
    /// ==============================================================
    /// BU AYRIM KRITIK -- KARISTIRILIRSA KOLTUK IKI KISIYE SATILIR
    /// ==============================================================
    /// PDF "Salon oturma plani" cache edilebilir diyor. Dogru, ama
    /// hangi veri oldugu cok onemli:
    ///
    ///   OTURMA PLANI (bu anahtar): Salonun fiziksel yapisi --
    ///   bolumler, sira etiketleri, koltuk numaralari, koordinatlar.
    ///   Yilda belki bir kez degisir. Onbelleklenmesi ideal.
    ///
    ///   KOLTUK UYGUNLUGU (ASLA onbelleklenmez): Hangi koltuk bos,
    ///   hangisi kilitli, hangisi satildi. SANIYELER icinde degisir.
    ///
    /// Ikincisini onbelleklesevdik, iki kullanici ayni "bos" koltugu
    /// gorur ve ikisi de secmeye calisirdi. Sprint 6-7'de kurdugumuz
    /// tum eszamanlilik savunmasi (xmin, partial unique index) yine
    /// koltugun iki kez satilmasini engellerdi -- ama kullanici
    /// deneyimi berbat olurdu: herkes surekli 409 alirdi.
    ///
    /// Ustelik Sprint 10'da SignalR ile koltuk durumunu GERCEK ZAMANLI
    /// yayinliyoruz. Ayni veriyi hem onbellekten (eski) hem SignalR'dan
    /// (guncel) beslemek, birbiriyle celisen iki kaynak demek olurdu.
    /// ==============================================================
    /// </remarks>
    public static string SeatLayout(Guid seatLayoutId)
        => string.Create(CultureInfo.InvariantCulture, $"layout:{seatLayoutId}");
}

/// <summary>
/// Onbellek yasam sureleri. PDF kurali: "Cache expiration tanimlanmalidir."
///
/// ==================================================================
/// SURELER NEYE GORE SECILDI?
/// ==================================================================
/// Tek soru: "Bu veri degistikten sonra kullanicinin eski halini
/// gormesi ne kadar sure kabul edilebilir?"
///
/// Cevap ne kadar uzunsa sure o kadar uzun. Ayrica her veri icin
/// ACIK temizleme (invalidation) de var; sure yalnizca EMNIYET AGI:
/// bir temizleme cagrisi unutulursa veya basarisiz olursa, veri en
/// gec bu sure sonunda kendini yeniler.
///
/// Yani "sonsuza kadar bayat kalma" ihtimalini ortadan kaldiriyor.
/// ==================================================================
/// </summary>
public static class CacheDurations
{
    /// <summary>
    /// Sehirler ve kategoriler: 24 saat.
    ///
    /// Turkiye'de 81 il var ve bu sayi yillardir degismedi. Kategoriler
    /// de admin tarafindan cok nadir eklenir. Kisa tutmanin hicbir
    /// faydasi yok, sadece gereksiz veritabani sorgusu olurdu.
    /// </summary>
    public static readonly TimeSpan ReferenceData = TimeSpan.FromHours(24);

    /// <summary>
    /// Etkinlik detayi: 5 dakika.
    ///
    /// Neden bu kadar kisa? Cunku detay yaniti etkinligin DURUMUNU
    /// tasiyor. Organizator satisi kapatirsa veya admin etkinligi
    /// askiya alirsa, kullanicilarin bunu saatlerce gec ogrenmesi
    /// kabul edilemez.
    ///
    /// Acik temizleme zaten var (durum degisince siliniyor); 5 dakika
    /// onun emniyet agi.
    /// </summary>
    public static readonly TimeSpan EventDetail = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Populer etkinlikler: 10 dakika.
    ///
    /// Bu bir SIRALAMA ve siralamanin aninda guncel olmasi gerekmiyor.
    /// Ustelik hesabi pahali (bilet sayimi + gruplama), yani
    /// onbellekten en cok kazanan sorgu bu.
    /// </summary>
    public static readonly TimeSpan PopularEvents = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Oturma plani: 1 saat.
    ///
    /// Salon yapisi neredeyse hic degismez. Yine de sonsuz vermiyorum:
    /// admin bir bolum eklerse en gec bir saat icinde gorunsun.
    /// (Acik temizleme de var.)
    /// </summary>
    public static readonly TimeSpan SeatLayout = TimeSpan.FromHours(1);
}

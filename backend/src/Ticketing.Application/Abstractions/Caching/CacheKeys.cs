using System.Globalization;

namespace Ticketing.Application.Abstractions.Caching;

/// <summary>
/// ONBELLEK ANAHTAR STANDARDI -- PDF Sprint 11
///
/// PDF kuralı: "Cache key standardi olusturulmalidir."
///
/// STANDART:  {alan}:{varlik}:{ayirt-edici}
///
/// Ornekler:
///     ref:cities                     -> şehir listesi
///     ref:categories                 -> etkinlik kategorileri
///     event:detail:{id}              -> etkinlik detayı
///     event:popular:{adet}           -> popüler etkinlikler
///     layout:{id}                    -> salon oturma planı
///
/// NEDEN STANDART ŞART? -- Iki somut sorun
///
/// 1) CAKISMA: Anahtarlar elle yazilsaydi, birinin "event:123" digerinin
///    "events:123" yazmasi kacinilmazdi. Ikisi FARKLI anahtar olur;
///    biri temizlenir digeri bayat kalır. Kullanıcı bazen güncel bazen
///    eski veri görür -- teshis edilmesi çok zor bir hata.
///
/// 2) TEMIZLEME: Onek olmadan "bu etkinlige ait tüm anahtarlari sil"
///    demek imkansizdir. Iki nokta ust uste ile bolumlenmis hiyerarsi,
///    onek bazlı silmeyi mumkun kiliyor.
///
/// Redis'te iki nokta ust uste (:) ayirici olarak YERLESIK bir gelenek;
/// RedisInsight gibi araclar anahtarlari bu ayirica göre agac olarak
/// gosteriyor.
///
/// Burada olmayan sey: kullaniciya ozel anahtarlar
///
/// PDF kuralı: "Kullanıcıya ozel hassas veriler ortak cache içinde
/// tutulmamalidir."
///
/// Bu sinifta bilerek TEK BIR kullanıcı bazlı anahtar yok. Rezervasyon,
/// bilet, ödeme ve bildirim sorgulari HİÇ onbelleklenmiyor.
///
/// Sebep sadece gizlilik değil, DOGRULUK da: rezervasyon durumu
/// saniyeler içinde değişiyor. Bir saniye bile bayat veri, kullanıcının
/// süresi dolmuş bir rezervasyona ödeme yapmaya calismasi demek.
/// </summary>
public static class CacheKeys
{
    // ---- Referans verileri (nadiren degisir) ----

    public const string Cities = "ref:cities";

    public const string Categories = "ref:categories";

    // ---- Etkinlik ----

    /// <summary>Tüm etkinlik anahtarlarinin oneki. Toplu temizleme için.</summary>
    public const string EventPrefix = "event:";

    public static string EventDetail(Guid eventId)
        => string.Create(CultureInfo.InvariantCulture, $"event:detail:{eventId}");

    public static string PopularEvents(int count)
        => string.Create(CultureInfo.InvariantCulture, $"event:popular:{count}");

    // ---- Salon oturma planı ----

    public const string SeatLayoutPrefix = "layout:";

    /// <summary>
    /// Salonun oturma PLANI -- koltuk UYGUNLUGU değil.
    /// </summary>
    /// <remarks>
    /// Bu ayrim kritik -- karistirilirsa koltuk iki kisiye satilir
    ///
    /// PDF "Salon oturma planı" cache edilebilir diyor. Dogru, ama
    /// hangi veri olduğu çok önemli:
    ///
    ///   OTURMA PLANI (bu anahtar): Salonun fiziksel yapısı --
    ///   bölümler, sıra etiketleri, koltuk numaralari, koordinatlar.
    ///   Yilda belki bir kez degisir. Onbelleklenmesi ideal.
    ///
    ///   KOLTUK UYGUNLUGU (ASLA onbelleklenmez): Hangi koltuk boş,
    ///   hangisi kilitli, hangisi satıldı. SANIYELER içinde degisir.
    ///
    /// Ikincisini onbelleklesevdik, iki kullanıcı aynı "boş" koltuğu
    /// görür ve ikisi de secmeye calisirdi. Sprint 6-7'de kurdugum
    /// tüm eszamanlilik savunmasi (xmin, partial unique index) yine
    /// koltuğun iki kez satilmasini engellerdi -- ama kullanıcı
    /// deneyimi berbat olurdu: herkes surekli 409 alırdı.
    ///
    /// Ustelik Sprint 10'da SignalR ile koltuk durumunu GERCEK ZAMANLI
    /// yayinliyorum. Aynı veriyi hem onbellekten (eski) hem SignalR'dan
    /// (güncel) beslemek, birbiriyle celisen iki kaynak demek olurdu.
    /// </remarks>
    public static string SeatLayout(Guid seatLayoutId)
        => string.Create(CultureInfo.InvariantCulture, $"layout:{seatLayoutId}");
}

/// <summary>
/// Onbellek yasam sureleri. PDF kuralı: "Cache expiration tanimlanmalidir."
///
/// Sureler neye gore secildi?
///
/// Tek soru: "Bu veri degistikten sonra kullanıcının eski halini
/// gormesi ne kadar süre kabul edilebilir?"
///
/// Cevap ne kadar uzunsa süre o kadar uzun. Ayrıca her veri için
/// ACIK temizleme (invalidation) de var; süre yalnızca EMNIYET AGI:
/// bir temizleme cagrisi unutulursa veya başarısız olursa, veri en
/// geç bu süre sonunda kendini yeniler.
///
/// Yani "sonsuza kadar bayat kalma" ihtimalini ortadan kaldiriyor.
/// </summary>
public static class CacheDurations
{
    /// <summary>
    /// Şehirler ve kategoriler: 24 saat.
    ///
    /// Turkiye'de 81 il var ve bu sayi yillardir degismedi. Kategoriler
    /// de admin tarafından çok nadir eklenir. Kisa tutmanin hiçbir
    /// faydasi yok, sadece gereksiz veritabani sorgusu olurdu.
    /// </summary>
    public static readonly TimeSpan ReferenceData = TimeSpan.FromHours(24);

    /// <summary>
    /// Etkinlik detayı: 5 dakika.
    ///
    /// Neden bu kadar kisa? Çünkü detay yaniti etkinliğin DURUMUNU
    /// tasiyor. Organizatör satışı kapatirsa veya admin etkinligi
    /// askiya alirsa, kullanicilarin bunu saatlerce geç ogrenmesi
    /// kabul edilemez.
    ///
    /// Acik temizleme zaten var (durum değişince siliniyor); 5 dakika
    /// onun emniyet agi.
    /// </summary>
    public static readonly TimeSpan EventDetail = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Popüler etkinlikler: 10 dakika.
    ///
    /// Bu bir SIRALAMA ve siralamanin anında güncel olmasını gerekmiyor.
    /// Ustelik hesabi pahali (bilet sayimi + gruplama), yani
    /// onbellekten en çok kazanan sorgu bu.
    /// </summary>
    public static readonly TimeSpan PopularEvents = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Oturma planı: 1 saat.
    ///
    /// Salon yapısı neredeyse hiç degismez. Yine de sonsuz vermiyorum:
    /// admin bir bölüm eklerse en geç bir saat içinde gorunsun.
    /// (Acik temizleme de var.)
    /// </summary>
    public static readonly TimeSpan SeatLayout = TimeSpan.FromHours(1);
}

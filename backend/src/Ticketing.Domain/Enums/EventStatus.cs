namespace Ticketing.Domain.Enums;

/// <summary>
/// Etkinligin yasam dongusu. PDF sayfa 6.
///
/// ---------------------------------------------------------------
/// NEDEN DEGERLERI ELLE YAZIYORUM (= 1, = 2 ...)?
/// ---------------------------------------------------------------
/// Bu enum'lari veritabanina int olarak yazacagiz. Sayilari elle
/// vermezsem C# bunlari sirayla 0, 1, 2... diye atar.
///
/// O zaman su olur: Alti ay sonra listenin ORTASINA yeni bir durum
/// eklerim (mesela Draft ile PendingApproval arasina "Rejected").
/// Sonraki TUM degerler bir kayar. Veritabanindaki eski kayitlar
/// artik yanlis duruma isaret eder. Published olan etkinlikler bir
/// anda SalesOpen gorunur.
///
/// Bu hatanin en kotu yani: derleme hatasi vermez, test kirmizi
/// yanmaz, sadece veriler sessizce yanlis olur. Fark ettiginde
/// duzeltmek icin migration yazman gerekir.
///
/// Sayilari sabitleyerek bu riski tamamen ortadan kaldiriyorum.
/// Yeni durum eklerken SONA ekle ve yeni bir sayi ver.
///
/// ---------------------------------------------------------------
/// NEDEN 0'DAN DEGIL 1'DEN BASLIYORUM?
/// ---------------------------------------------------------------
/// C#'ta bir enum alaninin varsayilan degeri her zaman 0'dir.
/// Eger Draft = 0 olsaydi, birisi Status alanini hic set etmeden
/// kayit olusturdugunda o kayit sessizce Draft olurdu ve hata
/// gorunmezdi.
///
/// 0 hicbir duruma karsilik gelmediginde, "atanmamis" durumu
/// hemen ortaya cikar ve validation yakalar.
/// </summary>
public enum EventStatus
{
    /// <summary>Organizator olusturdu, henuz kimse gormuyor.</summary>
    Draft = 1,

    /// <summary>Admin onayi bekliyor.</summary>
    PendingApproval = 2,

    /// <summary>Onaylandi, sitede gorunuyor. Ama satis henuz baslamadi.</summary>
    Published = 3,

    /// <summary>Bilet satisi acik. Rezervasyon SADECE bu durumda yapilabilir.</summary>
    SalesOpen = 4,

    /// <summary>Satis kapandi, etkinlik henuz gerceklesmedi.</summary>
    SalesClosed = 5,

    /// <summary>Etkinlik gerceklesti. Yorum yapilabilmesi icin bu durum gerekli.</summary>
    Completed = 6,

    /// <summary>Iptal edildi. Biletler iade sureci isletilir. Son durum.</summary>
    Cancelled = 7,

    /// <summary>Admin uygunsuz buldu ve askiya aldi. Published'a geri donebilir.</summary>
    Suspended = 8
}

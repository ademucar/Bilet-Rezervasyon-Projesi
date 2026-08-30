namespace Ticketing.Domain.Enums;

/// <summary>
/// Etkinligin yasam dongusu. PDF sayfa 6.
///
/// Neden degerleri elle yaziyorum (= 1, = 2 ...)?
///
/// Bu enum'lari veritabanina int olarak yazacagim. Sayilari elle
/// vermezsem C# bunlari sirayla 0, 1, 2... diye atar.
///
/// O zaman su olur: Alti ay sonra listenin ORTASINA yeni bir durum
/// eklerim (mesela Draft ile PendingApproval arasina "Rejected").
/// Sonraki TÜM degerler bir kayar. Veritabanindaki eski kayitlar
/// artık yanlış duruma isaret eder. Published olan etkinlikler bir
/// anda SalesOpen görünür.
///
/// Bu hatanin en kötü yani: derleme hatası vermez, test kırmızı
/// yanmaz, sadece veriler sessizce yanlış olur. Fark ettiginde
/// duzeltmek için migration yazman gerekir.
///
/// Sayilari sabitleyerek bu riski tamamen ortadan kaldiriyorum.
/// Yeni durum eklerken SONA ekle ve yeni bir sayi ver.
///
/// Neden 0'dan değil 1'den basliyorum?
///
/// C#'ta bir enum alaninin varsayılan değeri her zaman 0'dir.
/// Eger Draft = 0 olsaydı, birisi Status alanini hiç set etmeden
/// kayıt olusturdugunda o kayıt sessizce Draft olurdu ve hata
/// gorunmezdi.
///
/// 0 hiçbir duruma karsilik gelmediginde, "atanmamis" durumu
/// hemen ortaya çıkar ve validation yakalar.
/// </summary>
public enum EventStatus
{
    /// <summary>Organizatör olusturdu, henüz kimse gormuyor.</summary>
    Draft = 1,

    /// <summary>Admin onayı bekliyor.</summary>
    PendingApproval = 2,

    /// <summary>Onaylandı, sitede görünüyor. Ama satış henüz baslamadi.</summary>
    Published = 3,

    /// <summary>Bilet satışı açık. Rezervasyon SADECE bu durumda yapilabilir.</summary>
    SalesOpen = 4,

    /// <summary>Satış kapandı, etkinlik henüz gerceklesmedi.</summary>
    SalesClosed = 5,

    /// <summary>Etkinlik gerceklesti. Yorum yapilabilmesi için bu durum gerekli.</summary>
    Completed = 6,

    /// <summary>İptal edildi. Biletler iade sureci isletilir. Son durum.</summary>
    Cancelled = 7,

    /// <summary>Admin uygunsuz buldu ve askiya aldi. Published'a geri donebilir.</summary>
    Suspended = 8,
}

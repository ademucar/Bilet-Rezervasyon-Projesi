using System.ComponentModel.DataAnnotations;

namespace Ticketing.Application.Common.Options;

/// <summary>
/// Rezervasyon ayarlari. appsettings "Reservation" bolumunden okunur.
///
/// PDF Sprint 7: "Rezervasyon süresi örneğin 10 dakika olmalıdır."
/// Bu deger KODA GOMULMEDI -- yapilandirmadan geliyor ki farklı
/// etkinlik tiplerinde veya yogunluk donemlerinde degistirilebilsin.
/// </summary>
public sealed class ReservationOptions
{
    public const string SectionName = "Reservation";

    /// <summary>
    /// Koltuk kilit süresi (dakika).
    ///
    /// 10 dakika bilinçli bir denge:
    ///   - Çok kisa olsaydı (2 dk) kullanıcı kart bilgisini girerken
    ///     süresi dolar ve koltuğunu kaybederdi.
    ///   - Çok uzun olsaydı (60 dk) popüler bir etkinlikte koltuklar
    ///     saatlerce bloke kalır, gerçek alicilar bulamazdi.
    /// </summary>
    [Range(1, 120)]
    public int LockDurationMinutes { get; set; } = 10;

    /// <summary>Bir uzatmada eklenecek süre (dakika).</summary>
    [Range(1, 60)]
    public int MaxExtensionMinutes { get; set; } = 5;

    /// <summary>
    /// Izin verilen maksimum uzatma sayısı.
    ///
    /// Sinirsiz uzatma olsaydı bir kullanıcı popüler bir etkinlikte
    /// koltukları SURESIZ bloke edip satışı sabote edebilirdi.
    /// </summary>
    [Range(0, 5)]
    public int MaxExtensionCount { get; set; } = 1;
}

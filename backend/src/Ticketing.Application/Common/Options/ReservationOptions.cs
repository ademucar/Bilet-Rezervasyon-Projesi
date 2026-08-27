using System.ComponentModel.DataAnnotations;

namespace Ticketing.Application.Common.Options;

/// <summary>
/// Rezervasyon ayarlari. appsettings "Reservation" bolumunden okunur.
///
/// PDF Sprint 7: "Rezervasyon suresi ornegin 10 dakika olmalidir."
/// Bu deger KODA GOMULMEDI -- yapilandirmadan geliyor ki farkli
/// etkinlik tiplerinde veya yogunluk donemlerinde degistirilebilsin.
/// </summary>
public sealed class ReservationOptions
{
    public const string SectionName = "Reservation";

    /// <summary>
    /// Koltuk kilit suresi (dakika).
    ///
    /// 10 dakika bilincli bir denge:
    ///   - Cok kisa olsaydi (2 dk) kullanici kart bilgisini girerken
    ///     suresi dolar ve koltugunu kaybederdi.
    ///   - Cok uzun olsaydi (60 dk) populer bir etkinlikte koltuklar
    ///     saatlerce bloke kalir, gercek alicilar bulamazdi.
    /// </summary>
    [Range(1, 120)]
    public int LockDurationMinutes { get; set; } = 10;

    /// <summary>Bir uzatmada eklenecek sure (dakika).</summary>
    [Range(1, 60)]
    public int MaxExtensionMinutes { get; set; } = 5;

    /// <summary>
    /// Izin verilen maksimum uzatma sayisi.
    ///
    /// Sinirsiz uzatma olsaydi bir kullanici populer bir etkinlikte
    /// koltuklari SURESIZ bloke edip satisi sabote edebilirdi.
    /// </summary>
    [Range(0, 5)]
    public int MaxExtensionCount { get; set; } = 1;
}

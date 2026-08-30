using Ticketing.Domain.Entities;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// Testler icin hazir nesne kurucu.
///
/// Neden bu sinif var?
/// Bir EventSeat uretmek icin once Event, sonra EventSession, sonra
/// SeatLayout, SeatSection ve Seat uretmek gerekiyor. Bu zinciri her
/// testin basinda tekrarlarsam:
///   - testler okunmaz hale gelir (asil test edilen sey 20 satir
///     kurulum kodunun altinda kaybolur)
///   - domain'de bir imza degisirse 30 testi birden duzeltmem gerekir
///
/// Burada topladigimda tek yerden duzeltiyorum ve testler
/// "ne test ediliyor" sorusuna odaklanmis kaliyor.
/// </summary>
internal static class TestVeriKurucu
{
    public static readonly DateTimeOffset Simdi = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset EtkinlikTarihi = Simdi.AddDays(30);

    public static Money Fiyat(decimal tutar = 250m) => new(tutar, "TRY");

    public static Event Etkinlik() => Event.Create(
        title: "Rock Konseri",
        description: "Aciklama",
        categoryId: Guid.CreateVersion7(),
        organizerId: Guid.CreateVersion7(),
        cityId: Guid.CreateVersion7(),
        venueId: Guid.CreateVersion7(),
        hallId: Guid.CreateVersion7(),
        eventDate: EtkinlikTarihi,
        salesStartDate: Simdi,
        salesEndDate: EtkinlikTarihi.AddHours(-1),
        durationMinutes: 120);

    /// <summary>
    /// Koltuklari uretilmis bir oturum dondurur.
    /// </summary>
    /// <param name="koltukSayisi">Uretilecek koltuk sayisi.</param>
    public static (EventSession Oturum, IReadOnlyList<EventSeat> Koltuklar) OturumVeKoltuklar(
        int koltukSayisi = 5,
        decimal birimFiyat = 250m)
    {
        var etkinlik = Etkinlik();
        var oturum = etkinlik.AddSession(
            EtkinlikTarihi, EtkinlikTarihi.AddHours(2), Guid.CreateVersion7(), Guid.CreateVersion7());

        var biletTuru = etkinlik.AddTicketType("Standard", Fiyat(birimFiyat));

        // Fiziksel koltuklari uret
        var plan = SeatLayout.Create(Guid.CreateVersion7(), "Konser Duzeni");
        var bolum = plan.AddSection("Orta Blok", 1);
        bolum.GenerateSeats(rowCount: 1, seatsPerRow: koltukSayisi, rowLabels: ["A"]);

        // Fiyatlandirma fonksiyonu: bu testte tek bolum var, hepsi ayni
        // bilet turune ait. Gercek senaryoda bolum bazinda degisir.
        var koltuklar = oturum.GenerateSeats(
            bolum.Seats.ToList(),
            _ => (biletTuru.Id, Fiyat(birimFiyat)));

        return (oturum, koltuklar);
    }
}

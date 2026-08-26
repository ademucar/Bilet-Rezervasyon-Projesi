using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;

namespace Ticketing.UnitTests.Domain;

/// <summary>
/// PDF sayfa 11'deki oturma plani is kurallarinin testleri.
/// </summary>
public class SeatLayoutTests
{
    private static SeatLayout GecerliPlan()
        => SeatLayout.Create(Guid.CreateVersion7(), "Konser Duzeni");

    // ---------------------------------------------------------------
    // PDF: "Ayni salonda ayni isimde iki oturma plani bulunmamalidir."
    //      (bolum seviyesindeki karsiligi)
    // ---------------------------------------------------------------

    [Fact]
    public void AddSection_AyniIsimdeIkinciBolum_DomainExceptionFirlatmali()
    {
        var plan = GecerliPlan();
        plan.AddSection("Orta Blok", displayOrder: 1);

        var eylem = () => plan.AddSection("Orta Blok", displayOrder: 2);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat_layout.duplicate_section");
    }

    [Fact]
    public void AddSection_FarkliBuyukKucukHarf_YineDeCakismaSayilmali()
    {
        // "Orta Blok" ile "ORTA BLOK" ayni bolumdur. Buyuk/kucuk harf
        // farkiyla ayni bolumun iki kez eklenmesi kullaniciyi sasirtir
        // ve koltuk haritasini bozar.
        var plan = GecerliPlan();
        plan.AddSection("Orta Blok", 1);

        var eylem = () => plan.AddSection("ORTA BLOK", 2);

        eylem.Should().Throw<DomainException>();
    }

    // ---------------------------------------------------------------
    // Koltuk uretimi
    // ---------------------------------------------------------------

    [Fact]
    public void GenerateSeats_SiraVeKoltukSayisiyla_DogruSayidaKoltukUretmeli()
    {
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Orta Blok", 1);

        bolum.GenerateSeats(rowCount: 10, seatsPerRow: 20);

        bolum.Seats.Should().HaveCount(200);
        plan.GetTotalSeatCount().Should().Be(200);
    }

    [Fact]
    public void GenerateSeats_SiraEtiketleriyle_EtiketleriKullanmali()
    {
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Balkon", 1);

        bolum.GenerateSeats(rowCount: 3, seatsPerRow: 2, rowLabels: ["A", "B", "C"]);

        bolum.Seats.Should().HaveCount(6);
        bolum.Seats.Select(s => s.GetDisplayLabel())
             .Should().BeEquivalentTo(["A-1", "A-2", "B-1", "B-2", "C-1", "C-2"]);
    }

    [Fact]
    public void GenerateSeats_EtiketSayisiSiraSayisiylaUyusmuyorsa_HataFirlatmali()
    {
        // Bu kontrol olmasaydi IndexOutOfRangeException alirdik --
        // kullaniciya hicbir sey anlatmayan teknik bir hata.
        // DomainException ile ne yapmasi gerektigini soyluyoruz.
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Balkon", 1);

        var eylem = () => bolum.GenerateSeats(rowCount: 3, seatsPerRow: 2, rowLabels: ["A", "B"]);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat_section.row_label_mismatch");
    }

    [Fact]
    public void GenerateSeats_ZatenKoltukVarsa_HataFirlatmali()
    {
        // "Ya hep ya hic": yarim uretilmis bir bolum tutarsizdir.
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Orta Blok", 1);
        bolum.GenerateSeats(2, 2);

        var eylem = () => bolum.GenerateSeats(3, 3);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat_section.seats_already_generated");
    }

    // ---------------------------------------------------------------
    // PDF: "Ayni bolumde ayni sira ve koltuk numarasi tekrar edemez."
    // ---------------------------------------------------------------

    [Fact]
    public void AddSeat_AyniSiraVeNumara_DomainExceptionFirlatmali()
    {
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Loca", 1);
        bolum.AddSeat("A", 1);

        var eylem = () => bolum.AddSeat("A", 1);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat_section.duplicate_seat");
    }

    // ---------------------------------------------------------------
    // PDF: "Koltuk kapasitesi salon kapasitesini asmamalidir."
    // ---------------------------------------------------------------

    [Fact]
    public void ValidateCapacity_KoltukSayisiKapasiteyiAsiyorsa_HataFirlatmali()
    {
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Orta Blok", 1);
        bolum.GenerateSeats(rowCount: 10, seatsPerRow: 20);   // 200 koltuk

        var eylem = () => plan.ValidateCapacity(hallCapacity: 150);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("seat_layout.capacity_exceeded");
    }

    [Fact]
    public void ValidateCapacity_KapasiteYeterliyse_HataFirlatmamali()
    {
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Orta Blok", 1);
        bolum.GenerateSeats(10, 20);   // 200 koltuk

        var eylem = () => plan.ValidateCapacity(hallCapacity: 500);

        eylem.Should().NotThrow();
    }

    [Fact]
    public void ValidateCapacity_TamSinirda_HataFirlatmamali()
    {
        // Sinir degeri testi. "<" mi "<=" mi yazdigimizi kontrol ediyor.
        // Bu tur off-by-one hatalari en sik yapilan ve en gec fark edilen
        // hatalardandir; sinir testi olmadan gozden kacar.
        var plan = GecerliPlan();
        var bolum = plan.AddSection("Orta Blok", 1);
        bolum.GenerateSeats(10, 20);   // tam 200

        var eylem = () => plan.ValidateCapacity(hallCapacity: 200);

        eylem.Should().NotThrow();
    }
}

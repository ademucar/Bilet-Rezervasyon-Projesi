using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.UnitTests.Domain;

public class EventSessionTests
{
    private static readonly DateTimeOffset Baslangic = new(2026, 3, 15, 20, 0, 0, TimeSpan.Zero);

    private static EventSession GecerliOturum()
    {
        // EventSession.Create internal oldugu icin Event uzerinden uretiyorum.
        // Bu kasitli: bir oturum her zaman bir etkinlige ait olmali,
        // basibos bir EventSession olusturulamamali.
        var evt = Event.Create(
            "Konser", "aciklama",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            eventDate: Baslangic,
            salesStartDate: Baslangic.AddDays(-30),
            salesEndDate: Baslangic.AddHours(-1),
            durationMinutes: 120);

        return evt.AddSession(Baslangic, Baslangic.AddHours(2), Guid.CreateVersion7(), Guid.CreateVersion7());
    }

    // ARALIK CAKISMA FORMULU
    // Formul: a1 < b2 VE b1 < a2
    // Oturum: 20:00 - 22:00

    [Theory]
    // --- CAKISANLAR ---
    [InlineData(19, 21, true, "oncesinden baslayip icine giriyor")]
    [InlineData(21, 23, true, "icinden baslayip sonrasina tasiyor")]
    [InlineData(20, 22, true, "tamamen ayni")]
    [InlineData(19, 23, true, "tamamen kapsiyor")]
    [InlineData(20, 21, true, "tamamen icinde")]
    [InlineData(21, 22, true, "sonuna kadar icinde")]
    // --- CAKISMAYANLAR ---
    [InlineData(17, 19, false, "tamamen once biter")]
    [InlineData(23, 25, false, "tamamen sonra baslar")]
    [InlineData(18, 20, false, "tam bitiste baslar - arka arkaya seans")]
    [InlineData(22, 24, false, "tam bitisten baslar - arka arkaya seans")]
    public void OverlapsWith_TumSinirDurumlari(int baslangicSaat, int bitisSaat, bool beklenen, string senaryo)
    {
        var oturum = GecerliOturum();   // 20:00 - 22:00

        var digerBaslangic = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero).AddHours(baslangicSaat);
        var digerBitis = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero).AddHours(bitisSaat);

        var sonuc = oturum.OverlapsWith(digerBaslangic, digerBitis);

        sonuc.Should().Be(beklenen, senaryo);
    }

    // Tarih doğrulama

    [Fact]
    public void Create_BitisBaslangictanOnceyse_HataFirlatmali()
    {
        var evt = Event.Create(
            "Konser", "aciklama",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            Baslangic, Baslangic.AddDays(-30), Baslangic.AddHours(-1), 120);

        var eylem = () => evt.AddSession(
            Baslangic, Baslangic.AddHours(-1), Guid.CreateVersion7(), Guid.CreateVersion7());

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event_session.invalid_dates");
    }

    [Fact]
    public void Create_BaslangicVeBitisAyniysa_HataFirlatmali()
    {
        // Sifir suren oturum anlamsiz. ">=" yerine ">" yazsaydik
        // bu durum gecerdi -- klasik off-by-one hatasi.
        var evt = Event.Create(
            "Konser", "aciklama",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            Baslangic, Baslangic.AddDays(-30), Baslangic.AddHours(-1), 120);

        var eylem = () => evt.AddSession(Baslangic, Baslangic, Guid.CreateVersion7(), Guid.CreateVersion7());

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event_session.invalid_dates");
    }

    // Koltuk uretimi ve plan degisikligi

    [Fact]
    public void ChangeSeatLayout_KoltuklarUretilmisse_HataFirlatmali()
    {
        // PDF: "Satisi baslamis etkinligin oturma plani degistirilemez."
        // Plan degisirse mevcut rezervasyonlarin ve biletlerin isaret ettigi
        // koltuklar ortadan kalkar -- veri butunlugu bozulur.
        var (oturum, _) = TestVeriKurucu.OturumVeKoltuklar(2);

        var eylem = () => oturum.ChangeSeatLayout(Guid.CreateVersion7());

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event_session.seats_already_generated");
    }

    [Fact]
    public void ChangeSeatLayout_KoltuklarUretilmemisse_IzinVerilmeli()
    {
        var oturum = GecerliOturum();
        var yeniPlanId = Guid.CreateVersion7();

        oturum.ChangeSeatLayout(yeniPlanId);

        oturum.SeatLayoutId.Should().Be(yeniPlanId);
    }

    [Fact]
    public void Cancel_TamamlanmisOturum_HataFirlatmali()
    {
        var oturum = GecerliOturum();
        oturum.Complete();

        var eylem = oturum.Cancel;

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event_session.already_completed");
    }

    [Fact]
    public void Create_YeniOturum_ScheduledDurumundaBaslamali()
    {
        GecerliOturum().Status.Should().Be(EventSessionStatus.Scheduled);
    }
}

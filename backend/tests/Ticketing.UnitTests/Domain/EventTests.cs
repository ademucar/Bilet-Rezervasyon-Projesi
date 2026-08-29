using FluentAssertions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.Events;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.UnitTests.Domain;

public class EventTests
{
    private static readonly DateTimeOffset Simdi = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EtkinlikTarihi = Simdi.AddDays(30);

    private static Event GecerliEtkinlik() => Event.Create(
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
    /// Onaya gonderilebilir hale getirmek icin oturum ve bilet turu ekler.
    /// </summary>
    private static Event YayinaHazirEtkinlik()
    {
        var evt = GecerliEtkinlik();
        evt.AddSession(EtkinlikTarihi, EtkinlikTarihi.AddHours(2), Guid.CreateVersion7(), Guid.CreateVersion7());
        evt.AddTicketType("Standard", new Money(250m, "TRY"));

        return evt;
    }

    // Olusturma ve tarih kurallari (PDF sayfa 13)

    [Fact]
    public void Create_YeniEtkinlik_DraftDurumundaBaslamali()
    {
        GecerliEtkinlik().Status.Should().Be(EventStatus.Draft);
    }

    [Fact]
    public void Create_SatisBaslangiciBitistenSonraysa_HataFirlatmali()
    {
        // PDF: "Satis baslangic tarihi satis bitis tarihinden sonra olamaz."
        var eylem = () => Event.Create(
            "Konser", "aciklama",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            eventDate: EtkinlikTarihi,
            salesStartDate: EtkinlikTarihi.AddDays(-1),
            salesEndDate: EtkinlikTarihi.AddDays(-10),   // baslangictan ONCE
            durationMinutes: 120);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.invalid_sales_period");
    }

    [Fact]
    public void Create_SatisBitisiEtkinliktenSonraysa_HataFirlatmali()
    {
        // PDF: "Satis bitis tarihi etkinlik baslangicindan sonra olamaz."
        // Mantikli: etkinlik basladiktan sonra bilet satmanin anlami yok.
        var eylem = () => Event.Create(
            "Konser", "aciklama",
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(),
            eventDate: EtkinlikTarihi,
            salesStartDate: Simdi,
            salesEndDate: EtkinlikTarihi.AddHours(1),   // etkinlikten SONRA
            durationMinutes: 120);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.sales_end_after_event");
    }

    // DURUM MAKINESI -- projenin en kritik testleri

    [Fact]
    public void SubmitForApproval_OturumYoksa_HataFirlatmali()
    {
        var evt = GecerliEtkinlik();

        var eylem = evt.SubmitForApproval;

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.no_sessions");
    }

    [Fact]
    public void SubmitForApproval_BiletTuruYoksa_HataFirlatmali()
    {
        var evt = GecerliEtkinlik();
        evt.AddSession(EtkinlikTarihi, EtkinlikTarihi.AddHours(2), Guid.CreateVersion7(), Guid.CreateVersion7());

        var eylem = evt.SubmitForApproval;

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.no_ticket_types");
    }

    [Fact]
    public void Publish_DraftDurumundan_HataFirlatmali()
    {
        // Bu testin anlami buyuk: onaydan GECMEDEN yayina alma girisimi.
        // Bu kontrol olmasaydi organizator admin onayini atlayabilirdi.
        var evt = YayinaHazirEtkinlik();   // hala Draft

        var eylem = evt.Publish;

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.invalid_transition");
    }

    [Fact]
    public void Publish_OnaydaBekleyenEtkinlik_YayinlanmaliVeOlayFirlatmali()
    {
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();

        evt.Publish();

        evt.Status.Should().Be(EventStatus.Published);
        evt.DomainEvents.Should().ContainSingle()
           .Which.Should().BeOfType<EventPublishedDomainEvent>();
    }

    [Fact]
    public void Cancel_IptalEdilmisEtkinlikTekrarIptal_HataFirlatmali()
    {
        // Cancelled bir SON durum. AllowedTransitions sozlugunde
        // Cancelled anahtari bilerek YOK -- yani hicbir yere gecemez.
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();
        evt.Cancel("sanatci hastalandi");

        var eylem = () => evt.Cancel("tekrar");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.invalid_transition");
    }

    [Fact]
    public void Cancel_IptalNedeniniVeZamaniniKaydetmeli()
    {
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();

        evt.Cancel("sanatci hastalandi");

        evt.Status.Should().Be(EventStatus.Cancelled);
        evt.CancellationReason.Should().Be("sanatci hastalandi");
        evt.CancelledAt.Should().NotBeNull();
        evt.DomainEvents.Should().ContainItemsAssignableTo<EventCancelledDomainEvent>();
    }

    [Fact]
    public void TamAkis_DraftTanCompletedA_TumGecislerCalismali()
    {
        var evt = YayinaHazirEtkinlik();

        evt.SubmitForApproval();
        evt.Status.Should().Be(EventStatus.PendingApproval);

        evt.Publish();
        evt.Status.Should().Be(EventStatus.Published);

        evt.OpenSales();
        evt.Status.Should().Be(EventStatus.SalesOpen);

        evt.CloseSales();
        evt.Status.Should().Be(EventStatus.SalesClosed);

        evt.Complete();
        evt.Status.Should().Be(EventStatus.Completed);
    }

    [Fact]
    public void Complete_SonDurumdanSonraHicbirGecisOlmamali()
    {
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();
        evt.Publish();
        evt.OpenSales();
        evt.CloseSales();
        evt.Complete();

        // Completed son durum: ne iptal edilebilir ne askiya alinabilir.
        evt.Invoking(e => e.Cancel("gerekce")).Should().Throw<DomainException>();
        evt.Invoking(e => e.Suspend()).Should().Throw<DomainException>();
    }

    // Guncelleme kisitlari (PDF sayfa 13)

    [Fact]
    public void UpdateDates_SatisBaslamissa_HataFirlatmali()
    {
        // PDF: "Yayina alinmis etkinligin kritik alanlari kontrolsuz
        // degistirilemez." Tarih degisirse bilet almis kullanicinin
        // plani bozulur.
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();
        evt.Publish();
        evt.OpenSales();

        var eylem = () => evt.UpdateDates(
            EtkinlikTarihi.AddDays(5), Simdi, EtkinlikTarihi.AddDays(4));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.sales_started");
    }

    [Fact]
    public void UpdateDetails_Yayindayken_BaslikDegistirilebilmeli()
    {
        // Kritik OLMAYAN alanlar yayindayken de degisebilmeli --
        // yazim hatasi duzeltmek yasak olmamali.
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();
        evt.Publish();
        evt.OpenSales();

        evt.UpdateDetails("Rock Konseri 2026", "Yeni aciklama");

        evt.Title.Should().Be("Rock Konseri 2026");
    }

    [Fact]
    public void UpdateDetails_IptalEdilmisEtkinlikte_HataFirlatmali()
    {
        var evt = YayinaHazirEtkinlik();
        evt.Cancel();

        var eylem = () => evt.UpdateDetails("Yeni", "aciklama");

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.not_editable");
    }

    [Fact]
    public void SetCancellationPolicy_SatisBaslamissa_HataFirlatmali()
    {
        // Kullanici bileti "7 gun kala tam iade" vaadiyle aldi.
        // Sonradan politikayi degistirmek sozlesme ihlalidir.
        var evt = YayinaHazirEtkinlik();
        evt.SubmitForApproval();
        evt.Publish();
        evt.OpenSales();

        var eylem = () => evt.SetCancellationPolicy(CancellationPolicy.Create(240, 72, 30));

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.sales_started");
    }

    // Oturum çakışması

    [Fact]
    public void AddSession_AyniSalondaCakisanSaat_HataFirlatmali()
    {
        var evt = GecerliEtkinlik();
        var salonId = Guid.CreateVersion7();
        var planId = Guid.CreateVersion7();

        evt.AddSession(EtkinlikTarihi, EtkinlikTarihi.AddHours(3), salonId, planId);

        // 1 saat sonra baslayan oturum -> ilkiyle cakisiyor
        var eylem = () => evt.AddSession(
            EtkinlikTarihi.AddHours(1), EtkinlikTarihi.AddHours(4), salonId, planId);

        eylem.Should().Throw<DomainException>()
             .Which.ErrorCode.Should().Be("event.session_overlap");
    }

    [Fact]
    public void AddSession_FarkliSalondaAyniSaat_IzinVerilmeli()
    {
        // Ayni etkinligin iki farkli salonda es zamanli oturumu olabilir
        // (ornegin ana sahne + yan sahne).
        var evt = GecerliEtkinlik();
        var planId = Guid.CreateVersion7();

        evt.AddSession(EtkinlikTarihi, EtkinlikTarihi.AddHours(3), Guid.CreateVersion7(), planId);

        var eylem = () => evt.AddSession(
            EtkinlikTarihi, EtkinlikTarihi.AddHours(3), Guid.CreateVersion7(), planId);

        eylem.Should().NotThrow();
        evt.Sessions.Should().HaveCount(2);
    }

    [Fact]
    public void AddSession_ArkaArkayaSeanslar_CakismaSayilmamali()
    {
        // 14:00-16:00 ve 16:00-18:00 CAKISMAZ.
        // Bu, OverlapsWith'te "<" yerine "<=" yazsaydik kirilirdi ve
        // arka arkaya seans koymak imkansiz olurdu.
        var evt = GecerliEtkinlik();
        var salonId = Guid.CreateVersion7();
        var planId = Guid.CreateVersion7();

        evt.AddSession(EtkinlikTarihi, EtkinlikTarihi.AddHours(2), salonId, planId);

        var eylem = () => evt.AddSession(
            EtkinlikTarihi.AddHours(2), EtkinlikTarihi.AddHours(4), salonId, planId);

        eylem.Should().NotThrow();
    }
}

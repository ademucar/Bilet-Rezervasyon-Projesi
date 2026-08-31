using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.Application.Abstractions.Security;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Common.Auditing;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;
using Ticketing.Application.Features.Outbox;
using Ticketing.Domain.Entities;
using EventEntity = Ticketing.Domain.Entities.Event;

namespace Ticketing.Application.Features.Events;

internal static class EventErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "event.not_found", "Etkinlik bulunamadı.");

    public static readonly Error NotOwner = Error.Forbidden(
        "event.not_owner", "Bu etkinlik uzerinde işlem yapma yetkiniz yok.");

    public static readonly Error OrganizerProfileRequired = Error.Forbidden(
        "event.organizer_profile_required",
        "Etkinlik olusturmak için organizatör profiliniz olmalıdır.");

    public static readonly Error HallNotInVenue = Error.Validation(
        "event.hall_not_in_venue", "Secilen salon, secilen mekana ait değil.");

    public static readonly Error HallOccupied = Error.Conflict(
        "event.hall_occupied",
        "Secilen salon bu tarih araliginda başka bir etkinlik tarafından kullanılıyor.");

    public static readonly Error LayoutNotInHall = Error.Validation(
        "event.layout_not_in_hall", "Secilen oturma planı, secilen salona ait değil.");

    public static readonly Error CategoryNotFound = Error.Validation(
        "event.category_not_found", "Secilen kategori bulunamadı.");
}

// Etkinlik olusturma -- PDF: POST /api/v1/events

public sealed record CreateEventCommand(
    string Title,
    string Description,
    Guid CategoryId,
    Guid CityId,
    Guid VenueId,
    Guid HallId,
    DateTimeOffset EventDate,
    DateTimeOffset SalesStartDate,
    DateTimeOffset SalesEndDate,
    int DurationMinutes,
    int MaxTicketsPerUser,
    int MinimumAge) : IRequest<Result<Guid>>;

public sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Etkinlik basligi zorunludur.")
            .MaximumLength(250);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Açıklama zorunludur.")
            .MaximumLength(5000);

        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Süre sıfırdan büyük olmalıdır.")
            .LessThanOrEqualTo(1440).WithMessage("Süre 24 saati aşamaz.");

        RuleFor(x => x.MaxTicketsPerUser)
            .InclusiveBetween(1, 50)
            .WithMessage("Kullanıcı başına bilet limiti 1 ile 50 arasında olmalıdır.");

        RuleFor(x => x.MinimumAge)
            .InclusiveBetween(0, 99).WithMessage("Yaş sınırı 0 ile 99 arasında olmalıdır.");

        // Tarih kurallari -- PDF sayfa 13
        //
        // Bu kurallar hem burada hem Event entity'sinde var.
        //
        // Tekrar gibi görünüyor ama amaclari farklı:
        //   Validator -> kullanıcıya alan bazinda anlasilir hata verir
        //                ("Satış bitisi etkinlikten sonra olamaz")
        //   Entity    -> koda hangi yoldan gelirse gelsin geçersiz bir
        //                Event olusmasini engeller (veri tasima scripti,
        //                test kodu, gelecekteki başka bir handler...)
        RuleFor(x => x.SalesStartDate)
            .LessThan(x => x.SalesEndDate)
            .WithMessage("Satış baslangici, satış bitisinden önce olmalıdır.");

        RuleFor(x => x.SalesEndDate)
            .LessThanOrEqualTo(x => x.EventDate)
            .WithMessage("Satış bitiş tarihi, etkinlik baslangicindan sonra olamaz.");

        // Gecmise etkinlik oluşturulamaz.
        //
        // Entity'de bu kural yok -- bilerek. Veri tasima sırasında
        // gecmis etkinlikleri sisteme aktarmamiz gerekebilir.
        // Kullanıcı arayuzunden ise gecmise etkinlik girmek her zaman
        // hatadir, o yüzden yalnızca burada engelliyorum.
        RuleFor(x => x.EventDate)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("Etkinlik tarihi gelecekte olmalıdır.");
    }
}

internal sealed partial class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<CreateEventCommandHandler> _logger;

    public CreateEventCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        ILogger<CreateEventCommandHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
    }

    // PDF Sprint 16: "Etkinlik oluşturma" loglanmalidir.
    //
    // Etkinlik BASLIGINI logluyorum. Bu bir istisna: başka yerlerde
    // kullanıcı metnini loglamaktan kaciniyorum. Burada güvenli
    // çünkü etkinlik başlığı zaten herkese acik bir veri --
    // yayinlandiginda ana sayfada gorunecek. Gizli bir sey değil ve
    // destek için en pratik tanimlayici.
    [LoggerMessage(
        EventId = LogEvents.EtkinlikOlusturuldu,
        Level = LogLevel.Information,
        Message = "Etkinlik oluşturuldu. Id: {EtkinlikId}, Baslik: {Title}, Organizatör: {OrganizerProfileId}")]
    private static partial void LogEventCreated(
        ILogger logger, Guid etkinlikId, string title, Guid organizerProfileId);

    public async Task<Result<Guid>> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<Guid>(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // Etkinligin sahibi organizatör profili'dir, kullanıcı değil.
        //
        // Neden? Bir organizatör sirketini temsil eder. Ileride bir
        // sirkette birden fazla kullanıcı calisabilir; hepsi aynı
        // profil üzerinden etkinlik yonetir. Event'i doğrudan User'a
        // baglasaydim bu genisleme imkansiz olurdu.
        var organizerProfileId = await _context.OrganizerProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (organizerProfileId is null)
        {
            return Result.Failure<Guid>(EventErrors.OrganizerProfileRequired);
        }

        // ---- Referans butunlugu kontrolleri ----

        var categoryExists = await _context.EventCategories
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (!categoryExists)
        {
            return Result.Failure<Guid>(EventErrors.CategoryNotFound);
        }

        // Salonun secilen MEKANA ait olduğunu dogruluyorum.
        //
        // Bu kontrol olmasaydı kullanıcı İstanbul'daki bir mekani,
        // Ankara'daki bir salonla eslestirebilirdi. Iki FK de geçerli
        // olduğu için veritabani buna izin verirdi ve etkinlik
        // "İstanbul'da, Ankara salonunda" görünürdü.
        var hallBelongsToVenue = await _context.Halls
            .AsNoTracking()
            .AnyAsync(h => h.Id == request.HallId && h.VenueId == request.VenueId, cancellationToken)
            .ConfigureAwait(false);

        if (!hallBelongsToVenue)
        {
            return Result.Failure<Guid>(EventErrors.HallNotInVenue);
        }

        var evt = EventEntity.Create(
            request.Title,
            request.Description,
            request.CategoryId,
            organizerProfileId.Value,
            request.CityId,
            request.VenueId,
            request.HallId,
            request.EventDate,
            request.SalesStartDate,
            request.SalesEndDate,
            request.DurationMinutes,
            request.MaxTicketsPerUser,
            request.MinimumAge);

        _context.Events.Add(evt);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: "Etkinlik oluşturma" loglanmalidir.
        // SaveChanges'ten sonra: log ancak gerçekten kaydedildiyse
        // atiliyor.
        LogEventCreated(_logger, evt.Id, evt.Title, organizerProfileId.Value);

        return Result.Success(evt.Id);
    }
}

// Oturum ekleme -- PDF: POST /api/v1/events/{id}/sessions

public sealed record AddEventSessionCommand(
    Guid EventId,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    Guid HallId,
    Guid SeatLayoutId) : IRequest<Result<Guid>>;

public sealed class AddEventSessionCommandValidator : AbstractValidator<AddEventSessionCommand>
{
    public AddEventSessionCommandValidator()
        => RuleFor(x => x.StartDate)
            .LessThan(x => x.EndDate)
            .WithMessage("Oturum bitisi, baslangicindan sonra olmalıdır.");
}

internal sealed class AddEventSessionCommandHandler
    : IRequestHandler<AddEventSessionCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public AddEventSessionCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(
        AddEventSessionCommand request,
        CancellationToken cancellationToken)
    {
        // Sessions'i Include ediyorum: Event.AddSession, ayni etkinlik
        // icindeki cakismayi bellekteki koleksiyona bakarak kontrol ediyor.
        var evt = await _context.Events
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure<Guid>(EventErrors.NotFound);
        }

        // Oturma planinin secilen salona ait olduğunu dogrula.
        var layoutBelongsToHall = await _context.SeatLayouts
            .AsNoTracking()
            .AnyAsync(
                sl => sl.Id == request.SeatLayoutId && sl.HallId == request.HallId && sl.IsActive,
                cancellationToken)
            .ConfigureAwait(false);

        if (!layoutBelongsToHall)
        {
            return Result.Failure<Guid>(EventErrors.LayoutNotInHall);
        }

        // PDF is kuralı (sayfa 13):
        // "Aynı salon aynı zaman araliginda iki etkinlige atanamaz."
        //
        // Event.AddSession, YALNIZCA bu etkinliğin oturumlarini kontrol
        // edebiliyor -- diger etkinliklerin oturumlari bellekte değil,
        // veritabaninda.
        //
        // Bu yüzden BASKA etkinliklerle cakismayi burada kontrol ediyorum.
        //
        // Çakışma formulu (EventSession.OverlapsWith ile aynı):
        //     a1 < b2 VE b1 < a2
        // Kati esitsizlik: 14:00-16:00 ile 16:00-18:00 CAKISMAZ.
        //
        // İptal edilmiş oturumlari haric tutuyorum -- iptal edilmiş bir
        // oturum salonu isgal etmez.
        //
        // Yaris durumu uyarisi: Bu kontrol ile INSERT arasında başka bir
        // istek aynı salonu alabilir. Kesin garanti için PostgreSQL'in
        // EXCLUDE constraint'i gerekiyor:
        //     EXCLUDE USING gist (
        //         "HallId" WITH =,
        //         tstzrange("StartDate","EndDate") WITH &&
        //     ) WHERE ("Status" <> 4)
        // EF Core bu kisit tipini fluent API ile desteklemiyor; ham SQL
        // migration'i olarak ASAGIDA ekliyorum (AddHallOverlapConstraint).
        var hasConflict = await _context.EventSessions
            .AsNoTracking()
            .AnyAsync(
                s => s.HallId == request.HallId
                  && s.EventId != request.EventId
                  && s.Status != EventSessionStatus.Cancelled
                  && s.StartDate < request.EndDate
                  && request.StartDate < s.EndDate,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasConflict)
        {
            return Result.Failure<Guid>(EventErrors.HallOccupied);
        }

        // Aynı etkinlik icindeki cakismayi entity kontrol ediyor.
        var session = evt.AddSession(
            request.StartDate, request.EndDate, request.HallId, request.SeatLayoutId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(session.Id);
    }
}

// Durum gecisleri

/// <summary>Organizatör etkinligi onaya gönderir.</summary>
public sealed record SubmitEventForApprovalCommand(Guid EventId) : IRequest<Result>;

/// <summary>Admin onaylar ve etkinlik yayina alinir. PDF: POST /events/{id}/publish</summary>
public sealed record PublishEventCommand(Guid EventId) : IRequest<Result>;

/// <summary>PDF: POST /api/v1/events/{id}/cancel</summary>
public sealed record CancelEventCommand(Guid EventId, string? Reason) : IRequest<Result>;

/// <summary>
/// Admin uygunsuz bir etkinligi askiya alir.
/// PDF sayfa 5: "Admin: Uygunsuz etkinlikleri pasiflestirebilir."
/// </summary>
/// <remarks>
/// Askiya alma ile IPTAL neden ayri iki islem?
///
/// Cancelled bir SON durum: geri donusu yok, para iadesi zinciri
/// baslar, bileti olan herkese bildirim gider. Suspended ise geri
/// alinabilir (Suspended -> Published gecisi tanimli) ve hicbir
/// zincir tetiklemez.
///
/// Admin "bu afis uygunsuz" dedigi zaman istedigi sey etkinligi yok
/// etmek degil, satisi durdurup organizatorden duzeltme beklemek.
/// Iptal kullansaydim tek cikis yolu etkinligi bastan olusturmak
/// olurdu -- ve satilmis biletler bosu bosuna iade edilirdi.
/// </remarks>
public sealed record SuspendEventCommand(Guid EventId, string Reason) : IRequest<Result>;

/// <summary>Askidaki etkinligi yayina geri alir.</summary>
public sealed record ReinstateEventCommand(Guid EventId) : IRequest<Result>;

/// <summary>
/// Askiya alma sebebi ZORUNLU.
/// </summary>
/// <remarks>
/// Iptalde (CancelEventCommand) sebep istege bagli, burada zorunlu.
/// Tutarsiz gorunuyor ama kasitli: iptali cogu zaman etkinligin
/// SAHIBI yapiyor ve kendi kararinin sebebini kimseye aciklamak
/// zorunda degil. Askiya almayi ise her zaman bir BASKASI yapiyor.
/// Sebepsiz askiya alma, organizator icin "sitem calismiyor" ile
/// ayirt edilemez.
/// </remarks>
public sealed class SuspendEventCommandValidator : AbstractValidator<SuspendEventCommand>
{
    public SuspendEventCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Askiya alma sebebi zorunludur.")
            .MaximumLength(500);
    }
}

internal sealed partial class EventStatusCommandHandler
    : IRequestHandler<SubmitEventForApprovalCommand, Result>,
      IRequestHandler<PublishEventCommand, Result>,
      IRequestHandler<CancelEventCommand, Result>,
      IRequestHandler<SuspendEventCommand, Result>,
      IRequestHandler<ReinstateEventCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ISeatNotifier _seatNotifier;
    private readonly ICacheService _cache;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<EventStatusCommandHandler> _logger;

    // PDF Sprint 16: "Etkinlik yayinlama" loglanmalidir.
    //
    // Neden olusturmadan ayri bir olay?
    //
    // Yayinlama, is acisindan donusu olmayan bir esik: o an etkinlik
    // herkese görünür olur ve bilet satışı başlar. Taslak olusturmak
    // ise sonucu olmayan bir hazirlik adimi.
    //
    // "Kim, ne zaman yayinladi?" bir DENETIM sorusudur ve cevabinin
    // başka olaylarin arasında kaybolmamasi gerekiyor.
    [LoggerMessage(
        EventId = LogEvents.EtkinlikYayinlandi,
        Level = LogLevel.Information,
        Message = "Etkinlik yayinlandi. Id: {EtkinlikId}, Baslik: {Title}")]
    private static partial void LogEventPublished(ILogger logger, Guid etkinlikId, string title);

    // İptal, Warning seviyesinde -- hata olduğu için değil,
    // GORULMESI gerektigi için.
    //
    // Bir etkinliğin iptali para iadesi zinciri baslatiyor ve
    // yuzlerce kullanıcıya bildirim gidiyor. Sprint 15'te iade için
    // verdigim kararin aynisi: is etkisi büyük olan olaylar,
    // normal trafigin arasında kaybolmamali.
    [LoggerMessage(
        EventId = LogEvents.EtkinlikIptalEdildi,
        Level = LogLevel.Warning,
        Message = "Etkinlik İPTAL edildi. Id: {EtkinlikId}, Baslik: {Title}, Sebep: {Reason}")]
    private static partial void LogEventCancelled(
        ILogger logger, Guid etkinlikId, string title, string? reason);

    // Sebep, mesajin icinde AYRI bir parametre olarak duruyor
    // ({Reason}), metne yapistirilmis degil. Serilog bunu yapisal
    // alan olarak kaydediyor; boylece "sebebinde 'telif' gecen
    // askiya almalar" gibi bir sorgu mumkun oluyor.
    [LoggerMessage(
        EventId = LogEvents.EtkinlikAskiyaAlindi,
        Level = LogLevel.Warning,
        Message = "Etkinlik ASKIYA alindi. Id: {EtkinlikId}, Baslik: {Title}, Sebep: {Reason}")]
    private static partial void LogEventSuspended(
        ILogger logger, Guid etkinlikId, string title, string reason);

    [LoggerMessage(
        EventId = LogEvents.EtkinlikAskidanCikarildi,
        Level = LogLevel.Information,
        Message = "Etkinlik askidan cikarildi. Id: {EtkinlikId}, Baslik: {Title}")]
    private static partial void LogEventReinstated(
        ILogger logger, Guid etkinlikId, string title);

    public EventStatusCommandHandler(
        IApplicationDbContext context,
        ISeatNotifier seatNotifier,
        ICacheService cache,
        ICurrentUser currentUser,
        ILogger<EventStatusCommandHandler> logger)
    {
        _context = context;
        _seatNotifier = seatNotifier;
        _cache = cache;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Etkinlik degistiginde ilgili önbellek kayitlarini temizler.
    /// </summary>
    /// <remarks>
    /// PDF KURALI: "Veri guncellendiginde ilgili cache temizlenmelidir."
    ///
    /// Neden ONEK ile siliyorum, tek anahtarla değil?
    ///
    /// Bir etkinliğin durumu degistiginde birden fazla anahtar
    /// bayatliyor:
    ///
    ///     event:detail:{id}     -> bu etkinliğin detayı
    ///     event:popular:10      -> popüler listesi (artık yayında değil)
    ///     event:popular:20      -> aynı listenin başka boyutu
    ///
    /// "popular:{n}" anahtarlarinin hangi n degerleriyle uretildigini
    /// onceden BILEMEYIZ -- istemci 10 da isteyebilir 25 de. Tek tek
    /// silmek imkansiz.
    ///
    /// "event:" oneki hepsini birden yakaliyor.
    ///
    /// Fazla silmek, eksik silmekten iyidir
    ///
    /// Bu yaklasim BASKA etkinliklerin detay anahtarlarini da siliyor.
    /// Israf gibi görünüyor ama doğru tercih:
    ///
    ///   Fazla silmenin bedeli  -> birkaç sorgu tekrar veritabanina
    ///                             gider (milisaniyeler)
    ///   Eksik silmenin bedeli  -> kullanıcı iptal edilmis etkinlige
    ///                             bilet almaya çalışır
    ///
    /// Ikisi kiyaslanamaz. Onbellekte "bayat veri" her zaman
    /// "gereksiz sorgu"dan pahalidir.
    /// </remarks>
    private Task ClearEventCacheAsync(CancellationToken cancellationToken)
        => _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken);

    public async Task<Result> Handle(SubmitEventForApprovalCommand request, CancellationToken cancellationToken)
    {
        // Sessions VE TicketTypes gerekli: SubmitForApproval ikisinin de
        // boş olmadigini kontrol ediyor. Include etmezsem koleksiyonlar
        // boş görünür ve "en az bir oturum ekleyin" hatası alırdım --
        // oysa oturum var. Sessiz ve kafa karistirici bir hata olurdu.
        var evt = await _context.Events
            .Include(e => e.Sessions)
            .Include(e => e.TicketTypes)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        // Durum makinesi ve on kosullar entity'de. Ihlal -> DomainException -> 422.
        evt.SubmitForApproval();

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearEventCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> Handle(PublishEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        evt.Publish();

        // Denetim kaydi -- PDF sayfa 5.
        //
        // Yayinlama, etkinligi herkese gorunur kilan ve bilet satisini
        // baslatan esik. Serilog'a da yaziyorum ama o kayitlar 14 gun
        // sonra donuyor; "bu etkinligi kim onaylamis?" sorusu aylar
        // sonra sorulabiliyor.
        _context.AddAudit(
            _currentUser,

            // "Event", nameof(EventEntity) DEGIL.
            //
            // Bu dosyanin basinda "using EventEntity =
            // Ticketing.Domain.Entities.Event;" takma adi var (Event
            // adi System.Event ile karisiyordu). nameof takma adi
            // aldigi icin denetim kaydina "EventEntity" yaziyordu ve
            // arayuzdeki "Etkinlik" suzgeci hicbir sey bulamiyordu --
            // suzgec "Event" ariyor.
            //
            // Bunu ancak denetim ekranini gercek veriyle deneyince
            // fark ettim. Sabit metin yazmak burada dogru tercih:
            // EntityName veriye yazilan bir DEGER, kod icindeki tip
            // adiyla ayni kalmak zorunda degil.
            "Event",
            evt.Id,
            "EventPublished",
            newValues: new { evt.Title, Status = evt.Status.ToString() });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Log, SaveChanges'ten SONRA.
        //
        // Önce loglasaydim ve kaydetme başarısız olsaydı, logda
        // "yayinlandi" yazardi ama veritabaninda yayinlanmamis
        // olurdu. Loglarin gercekle celismesi, hiç log olmamasindan
        // daha kotudur: sorun arastiran kişi yanlış yone gider.
        LogEventPublished(_logger, evt.Id, evt.Title);

        // Yayinlanan etkinlik artık herkese görünür olmalı.
        //
        // Temizlemeseydim, daha önce 404 alan bir istek yuzunden
        // onbellekte "yok" kaydı olusmus olabilirdi... aslında
        // olmazdi: null değerleri bilerek onbelleklemiyorum
        // (bkz. RedisCacheService). Yine de popüler listesi ve
        // detay anahtarlari tazelenmeli.
        await ClearEventCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> Handle(CancelEventCommand request, CancellationToken cancellationToken)
    {
        // Oturumlari da yukluyorum: iptal bildirimini oturum GRUPLARINA
        // gonderecegim ve bunun için kimliklerine ihtiyacim var.
        var evt = await _context.Events
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        evt.Cancel(request.Reason);

        // Bildirim outbox'A -- PDF Sprint 9: "Etkinlik iptal bildirimi"
        //
        // Iptali AYNI transaction içinde kuyruga aliyorum.
        //
        // Bunu tek başına önemli kilan sey olcek: 2000 kisilik bir
        // konser iptal edildiginde 2000 bildirim yazilacak. Bunu
        // burada yapsaydim, admin "iptal et" butonuna bastiktan sonra
        // tarayıcı dakikalarca beklerdi -- ve zaman asimina ugrarsa
        // iptal islemi geri alinir, etkinlik iptal edilmemis olurdu.
        //
        // Outbox'a tek satır yazmak ise anında. Bildirimlerin
        // dagitimini arka plan job'i, kimseyi bekletmeden yapiyor.
        //
        // PDF: "Job islemleri kullanıcı istegini gereksiz yere
        // bekletmemelidir."
        _context.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.EventCancelled,
            JsonSerializer.Serialize(new EventCancelledPayload(
                evt.Id,
                evt.Title,
                request.Reason))));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 10: "EventCancelled"
        //
        // Sprint 9'da AYNI olay için Outbox'a da yazıyorum. Ikisi
        // birden gereksiz gorunebilir; değil, çünkü farklı hedefleri
        // var:
        //
        //   SignalR -> su an o oturumun koltuk haritasina bakan
        //              kisiler. Bilet almak uzereler; boşuna koltuk
        //              secmelerini engelliyorum.
        //
        //   Outbox  -> bileti olan herkes. Ekranda olsun olmasın,
        //              kalici bir bildirim ve e-posta aliyorlar.
        //
        // SignalR kaybolursa telafisi var (yeniden baglantida liste
        // çekiliyor); Outbox kaybolamaz. Sprint 9 belgesinde
        // ayrintili yazdim.
        await _seatNotifier.EventCancelledAsync(
            evt.Sessions.Select(x => x.Id).ToList(),
            evt.Id,
            evt.Title,
            cancellationToken).ConfigureAwait(false);

        // PDF Sprint 16: etkinlik iptali. Warning seviyesinde --
        // yuzlerce kullanıcıyı ve para iadesi zincirini etkiliyor.
        LogEventCancelled(_logger, evt.Id, evt.Title, request.Reason);

        // Onbellek temizligi BURADA en kritik.
        //
        // İptal edilen bir etkinlik onbellekte "SalesOpen" olarak
        // kalirsa, kullanıcılar 5 dakika boyunca satışta goruntuler
        // ve koltuk secmeye calisirdi. Rezervasyon sunucuda
        // reddedilir -- ama kullanıcı neden reddedildigini anlamaz.
        await ClearEventCacheAsync(cancellationToken).ConfigureAwait(false);

        // NOT (Sprint 8): İptal edilen etkinliğin aktif rezervasyonlarinin
        // iptali ve biletlerin iadesi BURADA yapilmiyor.
        //
        // Event.Cancel bir EventCancelledDomainEvent firlatiyor; o olayi
        // isleyen handler bu isleri yapacak. Boylece Event sinifi ödeme
        // ve bildirim servislerini bilmek zorunda kalmiyor.
        //
        // Domain event dagitimi Sprint 9'da (Outbox) kurulacak.
        return Result.Success();
    }

    public async Task<Result> Handle(SuspendEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        // Gecis kurali entity'de: yalnizca Published ve SalesOpen
        // askiya alinabilir. Taslak bir etkinligi askiya almak
        // anlamsiz -- zaten kimse goremiyor. Ihlal -> DomainException
        // -> 422.
        evt.Suspend();

        // Askiya alma sebebi BURADA kalici oluyor.
        //
        // Event uzerinde sebep sutunu yok (migration acmadim) ama
        // denetim kaydinda newValues JSON'u icinde duruyor. Yani
        // "neden askiya alindi?" sorusunun cevabi artik yalnizca
        // Serilog'da degil, kalici tabloda da var.
        _context.AddAudit(
            _currentUser,
            "Event",
            evt.Id,
            "EventSuspended",
            newValues: new { evt.Title, request.Reason });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Sebep su an YALNIZCA loga yaziliyor, veritabaninda tutulmuyor.
        //
        // Bilinçli bir sinir: Event uzerinde askiya alma sebebi diye
        // bir sutun yok ve bunun icin migration acmadim. Sonucu su:
        // organizator panelinde "askiya alindi" gorunuyor ama sebebi
        // gorunmuyor; sebebi ancak loglara bakan biri okuyabiliyor.
        //
        // Ileride sebebi organizatore gostermek istersem
        // SuspensionReason sutunu + migration gerekecek. Simdilik
        // PDF'in istedigi sey (pasiflestirme) calisiyor, denetim izi
        // de duruyor. README'deki "bilinen eksikler" listesine ekledim.
        LogEventSuspended(_logger, evt.Id, evt.Title, request.Reason);

        // Onbellek temizligi burada kritik: askiya alinan etkinlik
        // onbellekte "SalesOpen" kalirsa kullanicilar dakikalarca
        // satista gorur ve koltuk secmeye calisir.
        await ClearEventCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> Handle(ReinstateEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        // Published'a geri donuyor, SalesOpen'a degil -- etkinlik
        // askiya alinmadan once satista olsa bile. Satisi yeniden
        // acmak background job'in isi: satis tarih araligini o
        // kontrol ediyor. Buradan dogrudan SalesOpen'a atsaydim,
        // satis bitis tarihi gecmis bir etkinligi tekrar satisa
        // acmis olabilirdim.
        evt.Reinstate();

        _context.AddAudit(
            _currentUser,
            "Event",
            evt.Id,
            "EventReinstated",
            newValues: new { evt.Title });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogEventReinstated(_logger, evt.Id, evt.Title);
        await ClearEventCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

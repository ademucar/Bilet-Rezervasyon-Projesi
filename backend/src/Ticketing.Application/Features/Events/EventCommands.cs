using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;
using EventEntity = Ticketing.Domain.Entities.Event;

namespace Ticketing.Application.Features.Events;

internal static class EventErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "event.not_found", "Etkinlik bulunamadi.");

    public static readonly Error NotOwner = Error.Forbidden(
        "event.not_owner", "Bu etkinlik uzerinde islem yapma yetkiniz yok.");

    public static readonly Error OrganizerProfileRequired = Error.Forbidden(
        "event.organizer_profile_required",
        "Etkinlik olusturmak icin organizator profiliniz olmalidir.");

    public static readonly Error HallNotInVenue = Error.Validation(
        "event.hall_not_in_venue", "Secilen salon, secilen mekana ait degil.");

    public static readonly Error HallOccupied = Error.Conflict(
        "event.hall_occupied",
        "Secilen salon bu tarih araliginda baska bir etkinlik tarafindan kullaniliyor.");

    public static readonly Error LayoutNotInHall = Error.Validation(
        "event.layout_not_in_hall", "Secilen oturma plani, secilen salona ait degil.");

    public static readonly Error CategoryNotFound = Error.Validation(
        "event.category_not_found", "Secilen kategori bulunamadi.");
}

// ===================================================================
// ETKINLIK OLUSTURMA -- PDF: POST /api/v1/events
// ===================================================================

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
            .NotEmpty().WithMessage("Aciklama zorunludur.")
            .MaximumLength(5000);

        RuleFor(x => x.DurationMinutes)
            .GreaterThan(0).WithMessage("Sure sifirdan buyuk olmalidir.")
            .LessThanOrEqualTo(1440).WithMessage("Sure 24 saati asamaz.");

        RuleFor(x => x.MaxTicketsPerUser)
            .InclusiveBetween(1, 50)
            .WithMessage("Kullanici basina bilet limiti 1 ile 50 arasinda olmalidir.");

        RuleFor(x => x.MinimumAge)
            .InclusiveBetween(0, 99).WithMessage("Yas siniri 0 ile 99 arasinda olmalidir.");

        // ==============================================================
        // TARIH KURALLARI -- PDF sayfa 13
        // ==============================================================
        // Bu kurallar HEM burada HEM Event entity'sinde var.
        //
        // Tekrar gibi gorunuyor ama amaclari farkli:
        //   Validator -> kullaniciya ALAN BAZINDA anlasilir hata verir
        //                ("Satis bitisi etkinlikten sonra olamaz")
        //   Entity    -> koda hangi yoldan gelirse gelsin gecersiz bir
        //                Event olusmasini engeller (veri tasima scripti,
        //                test kodu, gelecekteki baska bir handler...)
        // ==============================================================
        RuleFor(x => x.SalesStartDate)
            .LessThan(x => x.SalesEndDate)
            .WithMessage("Satis baslangici, satis bitisinden once olmalidir.");

        RuleFor(x => x.SalesEndDate)
            .LessThanOrEqualTo(x => x.EventDate)
            .WithMessage("Satis bitis tarihi, etkinlik baslangicindan sonra olamaz.");

        // Gecmise etkinlik olusturulamaz.
        //
        // Entity'de BU kural YOK -- bilerek. Veri tasima sirasinda
        // gecmis etkinlikleri sisteme aktarmamiz gerekebilir.
        // Kullanici arayuzunden ise gecmise etkinlik girmek her zaman
        // hatadir, o yuzden yalnizca burada engelliyoruz.
        RuleFor(x => x.EventDate)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("Etkinlik tarihi gelecekte olmalidir.");
    }
}

internal sealed class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CreateEventCommandHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<Guid>(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // Etkinligin sahibi ORGANIZATOR PROFILI'dir, kullanici degil.
        //
        // Neden? Bir organizator sirketini temsil eder. Ileride bir
        // sirkette birden fazla kullanici calisabilir; hepsi ayni
        // profil uzerinden etkinlik yonetir. Event'i dogrudan User'a
        // baglasaydik bu genisleme imkansiz olurdu.
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

        // Salonun secilen MEKANA ait oldugunu dogruluyorum.
        //
        // Bu kontrol olmasaydi kullanici Istanbul'daki bir mekani,
        // Ankara'daki bir salonla eslestirebilirdi. Iki FK de gecerli
        // oldugu icin veritabani buna izin verirdi ve etkinlik
        // "Istanbul'da, Ankara salonunda" gorunurdu.
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

        return Result.Success(evt.Id);
    }
}

// ===================================================================
// OTURUM EKLEME -- PDF: POST /api/v1/events/{id}/sessions
// ===================================================================

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
            .WithMessage("Oturum bitisi, baslangicindan sonra olmalidir.");
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
        // Sessions'i Include ediyorum: Event.AddSession, AYNI ETKINLIK
        // icindeki cakismayi bellekteki koleksiyona bakarak kontrol ediyor.
        var evt = await _context.Events
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure<Guid>(EventErrors.NotFound);
        }

        // Oturma planinin secilen salona ait oldugunu dogrula.
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

        // ==============================================================
        // PDF is kurali (sayfa 13):
        // "Ayni salon ayni zaman araliginda iki etkinlige atanamaz."
        // ==============================================================
        // Event.AddSession, YALNIZCA bu etkinligin oturumlarini kontrol
        // edebiliyor -- diger etkinliklerin oturumlari bellekte degil,
        // veritabaninda.
        //
        // Bu yuzden BASKA etkinliklerle cakismayi burada kontrol ediyorum.
        //
        // Cakisma formulu (EventSession.OverlapsWith ile ayni):
        //     a1 < b2 VE b1 < a2
        // Kati esitsizlik: 14:00-16:00 ile 16:00-18:00 CAKISMAZ.
        //
        // Iptal edilmis oturumlari haric tutuyorum -- iptal edilmis bir
        // oturum salonu isgal etmez.
        //
        // YARIS DURUMU UYARISI: Bu kontrol ile INSERT arasinda baska bir
        // istek ayni salonu alabilir. Kesin garanti icin PostgreSQL'in
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

        // Ayni etkinlik icindeki cakismayi entity kontrol ediyor.
        var session = evt.AddSession(
            request.StartDate, request.EndDate, request.HallId, request.SeatLayoutId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(session.Id);
    }
}

// ===================================================================
// DURUM GECISLERI
// ===================================================================

/// <summary>Organizator etkinligi onaya gonderir.</summary>
public sealed record SubmitEventForApprovalCommand(Guid EventId) : IRequest<Result>;

/// <summary>Admin onaylar ve etkinlik yayina alinir. PDF: POST /events/{id}/publish</summary>
public sealed record PublishEventCommand(Guid EventId) : IRequest<Result>;

/// <summary>PDF: POST /api/v1/events/{id}/cancel</summary>
public sealed record CancelEventCommand(Guid EventId, string? Reason) : IRequest<Result>;

internal sealed class EventStatusCommandHandler
    : IRequestHandler<SubmitEventForApprovalCommand, Result>,
      IRequestHandler<PublishEventCommand, Result>,
      IRequestHandler<CancelEventCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public EventStatusCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(SubmitEventForApprovalCommand request, CancellationToken cancellationToken)
    {
        // Sessions VE TicketTypes gerekli: SubmitForApproval ikisinin de
        // bos olmadigini kontrol ediyor. Include etmezsem koleksiyonlar
        // bos gorunur ve "en az bir oturum ekleyin" hatasi alirdik --
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

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> Handle(CancelEventCommand request, CancellationToken cancellationToken)
    {
        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        evt.Cancel(request.Reason);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // NOT (Sprint 8): Iptal edilen etkinligin aktif rezervasyonlarinin
        // iptali ve biletlerin iadesi BURADA yapilmiyor.
        //
        // Event.Cancel bir EventCancelledDomainEvent firlatiyor; o olayi
        // isleyen handler bu isleri yapacak. Boylece Event sinifi odeme
        // ve bildirim servislerini bilmek zorunda kalmiyor.
        //
        // Domain event dagitimi Sprint 9'da (Outbox) kurulacak.
        return Result.Success();
    }
}

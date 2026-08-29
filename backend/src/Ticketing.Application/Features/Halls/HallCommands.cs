using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Halls;

internal static class HallErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "hall.not_found", "Salon bulunamadı.");

    public static readonly Error VenueNotFound = Error.NotFound(
        "hall.venue_not_found", "Mekan bulunamadı.");

    public static readonly Error DuplicateName = Error.Conflict(
        "hall.duplicate_name", "Bu mekanda aynı isimde bir salon zaten var.");

    public static readonly Error HasActiveEvents = Error.Conflict(
        "hall.has_active_events",
        "Bu salonda aktif etkinlikler var. Salon silinemez.");

    public static readonly Error CapacityBelowSeats = Error.Conflict(
        "hall.capacity_below_seats",
        "Yeni kapasite, mevcut oturma planlarindaki koltuk sayisindan az olamaz.");
}

// OLUSTURMA -- PDF: POST /api/v1/venues/{venueId}/halls

public sealed record CreateHallCommand(Guid VenueId, string Name, int Capacity)
    : IRequest<Result<Guid>>;

public sealed class CreateHallCommandValidator : AbstractValidator<CreateHallCommand>
{
    public CreateHallCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Salon adı zorunludur.")
            .MaximumLength(150).WithMessage("Salon adı en fazla 150 karakter olabilir.");

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("Kapasite sıfırdan büyük olmalıdır.")
            // Ust sinir koyuyorum: dunyanin en büyük stadyumu ~150.000 kisilik.
            // 2 milyar kapasiteli bir salon yazım hatasidir ve koltuk
            // uretiminde bellegi tuketir.
            .LessThanOrEqualTo(200_000).WithMessage("Kapasite 200.000'i aşamaz.");
    }
}

internal sealed class CreateHallCommandHandler : IRequestHandler<CreateHallCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateHallCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(CreateHallCommand request, CancellationToken cancellationToken)
    {
        var venueExists = await _context.Venues
            .AsNoTracking()
            .AnyAsync(v => v.Id == request.VenueId, cancellationToken)
            .ConfigureAwait(false);

        if (!venueExists)
        {
            return Result.Failure<Guid>(HallErrors.VenueNotFound);
        }

        var name = request.Name.Trim();

        var duplicate = await _context.Halls
            .AsNoTracking()
            .AnyAsync(h => h.VenueId == request.VenueId && h.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            // Bu kontrol kullanıcıya anlamlı mesaj için.
            // Kesin garanti veritabanindaki UNIQUE (VenueId, Name)
            // partial index'inde -- iki kullanıcı aynı anda eklerse
            // ikincisi orada patlar ve 409 döner.
            return Result.Failure<Guid>(HallErrors.DuplicateName);
        }

        var hall = Hall.Create(request.VenueId, name, request.Capacity);

        _context.Halls.Add(hall);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(hall.Id);
    }
}

// GUNCELLEME

public sealed record UpdateHallCommand(Guid Id, string Name, int Capacity) : IRequest<Result>;

public sealed class UpdateHallCommandValidator : AbstractValidator<UpdateHallCommand>
{
    public UpdateHallCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Capacity).GreaterThan(0).LessThanOrEqualTo(200_000);
    }
}

internal sealed class UpdateHallCommandHandler : IRequestHandler<UpdateHallCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public UpdateHallCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(UpdateHallCommand request, CancellationToken cancellationToken)
    {
        var hall = await _context.Halls
            .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (hall is null)
        {
            return Result.Failure(HallErrors.NotFound);
        }

        // KAPASITEYI DUSURURKEN MEVCUT KOLTUKLARI KONTROL ET
        //
        // PDF is kuralı: "Koltuk kapasitesi salon kapasitesini asmamalidir."
        //
        // Bu kural genelde koltuk EKLERKEN dusunulur. Ama ters yonden
        // de ihlal edilebilir: 500 koltuklu bir plan varken kapasiteyi
        // 300'e dusurmek aynı kuralı bozar.
        //
        // Bu kontrol olmasaydı veri sessizce tutarsiz hale gelirdi ve
        // hatayi ancak aylar sonra bir raporda fark ederdik.
        if (request.Capacity < hall.Capacity)
        {
            // BU SORGU ILK YAZISIMDA CALISMADI -- HIKAYESI
            //
            // Önce soyle yazmistim:
            //
            //   _context.SeatLayouts
            //       .Where(sl => sl.HallId == id)
            //       .Select(sl => sl.Sections.Sum(seç => seç.Seats.Count(...)))
            //       .DefaultIfEmpty(0)
            //       .MaxAsync()
            //
            // Derlendi, testler gecti -- ama CALISMA ZAMANINDA patladi:
            //
            //   InvalidOperationException: The LINQ expression
            //   'DbSet<SeatLayout>()...' could not be translated.
            //
            // Sebep: IC ICE koleksiyon toplama (Sum içinde Count) EF Core
            // tarafından SQL'e cevrilemiyor.
            //
            // DERS: LINQ'in derlenmesi, SQL'e cevrilebilecegi anlamina
            // GELMEZ. IQueryable içinde yazdigin her sey bir SQL karşılığı
            // bulmak zorunda. Bu tur hatalar yalnızca gerçek veritabanina
            // karsi calistirinca ortaya çıkar -- birim testler yakalayamaz.
            // (Sprint 17'de Testcontainers ile bunu koruyacagim.)
            //
            // COZUM: Sorguyu KOLTUK tablosundan baslatip gruplamak.
            // Duz bir GROUP BY, EF'in rahatca cevirdigi bir yapi.
            var seatCountsPerLayout = await _context.Seats
                .AsNoTracking()
                .Where(s => s.SeatSection.SeatLayout.HallId == request.Id)
                .GroupBy(s => s.SeatSection.SeatLayoutId)
                .Select(g => g.Count())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Max()'i BELLEKTE alıyorum. Bir salonun plan sayısı en fazla
            // birkaç tanedir; bunu SQL'e ittirmek için ugrasmanin değeri yok.
            //
            // Not: Seat entity'sinde global query filter (!IsDeleted) zaten
            // var, yani silinmis koltuklar bu sayima girmiyor.
            var maxSeatCount = seatCountsPerLayout.Count == 0 ? 0 : seatCountsPerLayout.Max();

            if (request.Capacity < maxSeatCount)
            {
                return Result.Failure(HallErrors.CapacityBelowSeats);
            }
        }

        hall.Update(request.Name.Trim(), request.Capacity);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// SILME

public sealed record DeleteHallCommand(Guid Id) : IRequest<Result>;

internal sealed class DeleteHallCommandHandler : IRequestHandler<DeleteHallCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteHallCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(DeleteHallCommand request, CancellationToken cancellationToken)
    {
        var hall = await _context.Halls
            .FirstOrDefaultAsync(h => h.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (hall is null)
        {
            return Result.Failure(HallErrors.NotFound);
        }

        // PDF is kuralı: "Aktif etkinlik bulunan salon silinememelidir."
        //
        // Iki yerden kontrol ediyorum:
        //   - Event.HallId  -> etkinliğin ana salonu
        //   - EventSession.HallId -> oturumun salonu (farklı olabilir)
        //
        // Yalnızca birini kontrol etseydim, çok salonlu bir festivalin
        // yan sahnesi silinebilirdi.
        var hasActiveEvents = await _context.Events
            .AsNoTracking()
            .AnyAsync(
                e => e.HallId == request.Id
                  && e.Status != EventStatus.Cancelled
                  && e.Status != EventStatus.Completed,
                cancellationToken)
            .ConfigureAwait(false);

        var hasActiveSessions = await _context.EventSessions
            .AsNoTracking()
            .AnyAsync(
                s => s.HallId == request.Id
                  && s.Status != EventSessionStatus.Cancelled
                  && s.Status != EventSessionStatus.Completed,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasActiveEvents || hasActiveSessions)
        {
            return Result.Failure(HallErrors.HasActiveEvents);
        }

        hall.IsDeleted = true;
        hall.DeletedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

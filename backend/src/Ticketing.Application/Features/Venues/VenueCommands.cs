using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Venues;

/// <summary>Mekan islemlerinin ortak hatalari.</summary>
internal static class VenueErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "venue.not_found", "Mekan bulunamadi.");

    public static readonly Error CityNotFound = Error.NotFound(
        "venue.city_not_found", "Secilen sehir bulunamadi.");

    public static readonly Error HasActiveEvents = Error.Conflict(
        "venue.has_active_events",
        "Bu mekanda aktif etkinlikler var. Once etkinlikleri iptal edin.");

    public static readonly Error DuplicateName = Error.Conflict(
        "venue.duplicate_name",
        "Bu sehirde ayni isimde bir mekan zaten var.");
}

// ===================================================================
// OLUSTURMA
// ===================================================================

/// <summary>PDF Sprint 4: POST /api/v1/venues</summary>
public sealed record CreateVenueCommand(
    string Name,
    string Address,
    Guid CityId,
    decimal? Latitude,
    decimal? Longitude) : IRequest<Result<Guid>>;

public sealed class CreateVenueCommandValidator : AbstractValidator<CreateVenueCommand>
{
    public CreateVenueCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Mekan adi zorunludur.")
            .MaximumLength(200).WithMessage("Mekan adi en fazla 200 karakter olabilir.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Adres zorunludur.")
            .MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.");

        RuleFor(x => x.CityId)
            .NotEmpty().WithMessage("Sehir secilmelidir.");

        // Koordinat kontrolunu HEM burada HEM entity'de yapiyorum.
        //
        // Tekrar gibi gorunuyor ama amaclari farkli: buradaki kullaniciya
        // "enlem -90 ile 90 arasinda olmali" diye anlasilir bir form
        // hatasi verir; entity'deki ise koda hangi yoldan gelirse gelsin
        // gecersiz bir Venue olusmasini engeller (ornegin veri tasima
        // scripti FluentValidation'dan gecmez).
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Enlem -90 ile 90 arasinda olmalidir.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Boylam -180 ile 180 arasinda olmalidir.")
            .When(x => x.Longitude.HasValue);
    }
}

internal sealed class CreateVenueCommandHandler : IRequestHandler<CreateVenueCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateVenueCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(CreateVenueCommand request, CancellationToken cancellationToken)
    {
        // Sehrin var oldugunu dogruluyorum.
        //
        // Yapmasaydik ne olurdu? Foreign key ihlali -> DbUpdateException
        // -> kullanici "Veri cakismasi" gibi anlamsiz bir 409 alirdi.
        // Burada kontrol edip "Secilen sehir bulunamadi" demek cok daha
        // anlasilir.
        var cityExists = await _context.Cities
            .AsNoTracking()
            .AnyAsync(c => c.Id == request.CityId, cancellationToken)
            .ConfigureAwait(false);

        if (!cityExists)
        {
            return Result.Failure<Guid>(VenueErrors.CityNotFound);
        }

        var name = request.Name.Trim();

        // Ayni sehirde ayni isimde mekan olmasin.
        //
        // NOT: Bu kontrol yarisa aciktir -- iki kullanici ayni anda
        // olusturursa ikisi de "yok" gorup ikisi de ekleyebilir.
        // Kesin garanti icin veritabaninda UNIQUE (CityId, Name)
        // index'i olmali. Su an yok; Sprint 5'te ekleyecegim.
        //
        // Bu kontrolun degeri, YAYGIN durumda (tek kullanici, yazim
        // hatasi) anlamli bir mesaj vermek.
        var duplicate = await _context.Venues
            .AsNoTracking()
            .AnyAsync(v => v.CityId == request.CityId && v.Name == name, cancellationToken)
            .ConfigureAwait(false);

        if (duplicate)
        {
            return Result.Failure<Guid>(VenueErrors.DuplicateName);
        }

        var venue = Venue.Create(request.CityId, name, request.Address);

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            venue.SetCoordinates(request.Latitude.Value, request.Longitude.Value);
        }

        _context.Venues.Add(venue);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(venue.Id);
    }
}

// ===================================================================
// GUNCELLEME
// ===================================================================

public sealed record UpdateVenueCommand(
    Guid Id,
    string Name,
    string Address,
    decimal? Latitude,
    decimal? Longitude) : IRequest<Result>;

public sealed class UpdateVenueCommandValidator : AbstractValidator<UpdateVenueCommand>
{
    public UpdateVenueCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(500);
    }
}

internal sealed class UpdateVenueCommandHandler : IRequestHandler<UpdateVenueCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public UpdateVenueCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(UpdateVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = await _context.Venues
            .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (venue is null)
        {
            return Result.Failure(VenueErrors.NotFound);
        }

        // Sehir DEGISTIRILEMEZ -- komutda CityId alani bilerek yok.
        //
        // Bir mekan fiziksel bir binadir; sehir degistirmez. Izin
        // verseydik, gecmis etkinliklerin sehir bilgisi de degismis
        // olurdu ve "Istanbul'da izledigim konser" birden Ankara'ya
        // tasinirdi. Raporlar bozulurdu.
        //
        // Yanlis sehirle olusturulmus bir mekan varsa: sil, yeniden olustur.
        venue.Rename(request.Name);
        venue.UpdateAddress(request.Address);

        if (request.Latitude.HasValue && request.Longitude.HasValue)
        {
            venue.SetCoordinates(request.Latitude.Value, request.Longitude.Value);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// SILME
// ===================================================================

public sealed record DeleteVenueCommand(Guid Id) : IRequest<Result>;

internal sealed class DeleteVenueCommandHandler : IRequestHandler<DeleteVenueCommand, Result>
{
    private readonly IApplicationDbContext _context;

    public DeleteVenueCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result> Handle(DeleteVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = await _context.Venues
            .FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (venue is null)
        {
            return Result.Failure(VenueErrors.NotFound);
        }

        // ==============================================================
        // PDF is kurali: "Aktif etkinlik bulunan salon silinememelidir."
        // ==============================================================
        // Mekan seviyesinde de ayni kural gecerli: mekani silersek
        // altindaki salonlar da erisilemez hale gelir.
        //
        // "Aktif" derken TAMAMLANMAMIS ve IPTAL EDILMEMIS etkinlikleri
        // kastediyorum. Gecmis etkinliklerin varligi silmeyi engellememeli
        // -- yoksa hicbir mekan asla silinemezdi.
        var hasActiveEvents = await _context.Events
            .AsNoTracking()
            .AnyAsync(
                e => e.VenueId == request.Id
                  && e.Status != EventStatus.Cancelled
                  && e.Status != EventStatus.Completed,
                cancellationToken)
            .ConfigureAwait(false);

        if (hasActiveEvents)
        {
            return Result.Failure(VenueErrors.HasActiveEvents);
        }

        // SOFT DELETE.
        //
        // Fiziksel silme yapsaydik gecmis etkinliklerin VenueId'si
        // bosluga isaret ederdi ve "3 yil onceki konser neredeydi?"
        // sorusu cevapsiz kalirdi. Ayrica FK kisiti (Restrict) zaten
        // silmeye izin vermezdi.
        venue.IsDeleted = true;
        venue.DeletedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

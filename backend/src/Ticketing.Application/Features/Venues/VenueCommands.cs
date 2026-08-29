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
        "venue.not_found", "Mekan bulunamadı.");

    public static readonly Error CityNotFound = Error.NotFound(
        "venue.city_not_found", "Secilen şehir bulunamadı.");

    public static readonly Error HasActiveEvents = Error.Conflict(
        "venue.has_active_events",
        "Bu mekanda aktif etkinlikler var. Önce etkinlikleri iptal edin.");

    public static readonly Error DuplicateName = Error.Conflict(
        "venue.duplicate_name",
        "Bu sehirde aynı isimde bir mekan zaten var.");
}

// OLUSTURMA

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
            .NotEmpty().WithMessage("Mekan adı zorunludur.")
            .MaximumLength(200).WithMessage("Mekan adı en fazla 200 karakter olabilir.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Adres zorunludur.")
            .MaximumLength(500).WithMessage("Adres en fazla 500 karakter olabilir.");

        RuleFor(x => x.CityId)
            .NotEmpty().WithMessage("Şehir seçilmelidir.");

        // Koordinat kontrolunu HEM burada HEM entity'de yapıyorum.
        //
        // Tekrar gibi görünüyor ama amaclari farklı: buradaki kullanıcıya
        // "enlem -90 ile 90 arasında olmalı" diye anlasilir bir form
        // hatası verir; entity'deki ise koda hangi yoldan gelirse gelsin
        // geçersiz bir Venue olusmasini engeller (örneğin veri tasima
        // scripti FluentValidation'dan gecmez).
        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90, 90).WithMessage("Enlem -90 ile 90 arasında olmalıdır.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180, 180).WithMessage("Boylam -180 ile 180 arasında olmalıdır.")
            .When(x => x.Longitude.HasValue);
    }
}

internal sealed class CreateVenueCommandHandler : IRequestHandler<CreateVenueCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;

    public CreateVenueCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<Result<Guid>> Handle(CreateVenueCommand request, CancellationToken cancellationToken)
    {
        // Sehrin var olduğunu dogruluyorum.
        //
        // Yapmasaydik ne olurdu? Foreign key ihlali -> DbUpdateException
        // -> kullanıcı "Veri çakışması" gibi anlamsiz bir 409 alırdı.
        // Burada kontrol edip "Secilen şehir bulunamadı" demek çok daha
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

        // Aynı sehirde aynı isimde mekan olmasın.
        //
        // NOT: Bu kontrol yarisa aciktir -- iki kullanıcı aynı anda
        // olusturursa ikisi de "yok" gorup ikisi de ekleyebilir.
        // Kesin garanti için veritabaninda UNIQUE (CityId, Name)
        // index'i olmalı. Su an yok; Sprint 5'te ekleyecegim.
        //
        // Bu kontrolun değeri, YAYGIN durumda (tek kullanıcı, yazım
        // hatası) anlamlı bir mesaj vermek.
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

// GUNCELLEME

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

        // Şehir DEGISTIRILEMEZ -- komutda CityId alanı bilerek yok.
        //
        // Bir mekan fiziksel bir binadir; şehir degistirmez. Izin
        // verseydim, gecmis etkinliklerin şehir bilgisi de degismis
        // olurdu ve "İstanbul'da izledigim konser" birden Ankara'ya
        // tasinirdi. Raporlar bozulurdu.
        //
        // Yanlis sehirle olusturulmus bir mekan varsa: sil, yeniden oluştur.
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

// SILME

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

        // PDF is kuralı: "Aktif etkinlik bulunan salon silinememelidir."
        //
        // Mekan seviyesinde de aynı kural geçerli: mekani silersek
        // altindaki salonlar da erişilemez hale gelir.
        //
        // "Aktif" derken TAMAMLANMAMIS ve İPTAL EDILMEMIS etkinlikleri
        // kastediyorum. Gecmis etkinliklerin varligi silmeyi engellememeli
        // -- yoksa hiçbir mekan asla silinemezdi.
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
        // Fiziksel silme yapsaydim gecmis etkinliklerin VenueId'si
        // bosluga isaret ederdi ve "3 yil önceki konser neredeydi?"
        // sorusu cevapsiz kalırdı. Ayrıca FK kisiti (Restrict) zaten
        // silmeye izin vermezdi.
        venue.IsDeleted = true;
        venue.DeletedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

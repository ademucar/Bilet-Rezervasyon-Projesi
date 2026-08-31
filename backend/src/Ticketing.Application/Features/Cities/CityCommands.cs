using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Cities;

internal static class CityErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "city.not_found", "Şehir bulunamadı.");

    public static readonly Error NameTaken = Error.Conflict(
        "city.name_taken", "Bu adda bir şehir zaten var.");

    public static readonly Error PlateCodeTaken = Error.Conflict(
        "city.plate_code_taken", "Bu plaka kodu başka bir şehre ait.");

    // Silme engeli. PDF'te boyle bir madde yok; kendi kararim.
    //
    // Sehri soft delete etsem veritabani tutarli kalirdi (FK
    // kirilmaz), ama mekan listesinde sehir adi bos gorunmeye
    // baslardi ve kimse sebebini anlamazdi. Bagli kayit varken
    // silmeyi reddedip adine "once mekanlari tasi" demek daha
    // durust.
    public static readonly Error InUse = Error.Conflict(
        "city.in_use",
        "Bu şehirde kayıtlı mekanlar var. Önce onları taşıyın veya silin.");
}

// ---- Olusturma ----

public sealed record CreateCityCommand(string Name, int PlateCode) : IRequest<Result<Guid>>;

public sealed class CreateCityCommandValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Şehir adı zorunludur.")
            .MaximumLength(100);

        // Ayni kural City.Create icinde de var.
        //
        // Tekrar gibi gorunuyor ama amaclari farkli: validator
        // kullaniciya alan bazinda anlasilir hata veriyor, entity ise
        // koda hangi yoldan gelirse gelsin gecersiz bir City
        // olusmasini engelliyor (veri tasima scripti, test kodu...).
        // Etkinlik tarihlerinde de ayni ayrimi yapmistim.
        RuleFor(x => x.PlateCode)
            .InclusiveBetween(1, 81)
            .WithMessage("Plaka kodu 1 ile 81 arasında olmalıdır.");
    }
}

// ---- Guncelleme ----

/// <summary>
/// Sehri yeniden adlandirir.
/// </summary>
/// <remarks>
/// PLAKA KODU DEGISTIRILEMIYOR -- bilerek.
///
/// Plaka kodu sehrin kimligi gibi: 34 her zaman Istanbul. Yanlis
/// girilmisse dogru islem duzeltmek degil, kaydi silip yeniden
/// olusturmak. Degistirilebilir yapsaydim, iki sehrin plakasini
/// yanlislikla takas etmek tek tiklik bir hata olurdu ve bunu fark
/// etmek aylar surerdi.
/// </remarks>
public sealed record RenameCityCommand(Guid Id, string Name) : IRequest<Result>;

public sealed class RenameCityCommandValidator : AbstractValidator<RenameCityCommand>
{
    public RenameCityCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Şehir adı zorunludur.")
            .MaximumLength(100);
    }
}

// ---- Silme ----

public sealed record DeleteCityCommand(Guid Id) : IRequest<Result>;

internal sealed class CityCommandHandler
    : IRequestHandler<CreateCityCommand, Result<Guid>>,
      IRequestHandler<RenameCityCommand, Result>,
      IRequestHandler<DeleteCityCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public CityCommandHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    /// <summary>
    /// Sehir listesi onbellegini temizler.
    /// </summary>
    /// <remarks>
    /// PDF KURALI: "Veri guncellendiginde ilgili cache temizlenmelidir."
    ///
    /// Sehir listesi Redis'te 24 SAAT duruyor. Temizlemeseydim admin
    /// yeni sehri ekler, filtre acilir listesinde goremez ve
    /// "eklenmedi mi?" diye tekrar eklemeye calisirdi -- bu kez
    /// benzersizlik hatasi alirdi. Yani unutulan tek satir,
    /// kullaniciya "sistem bozuk" hissi verirdi.
    /// </remarks>
    private Task ClearCacheAsync(CancellationToken cancellationToken)
        => _cache.RemoveAsync(CacheKeys.Cities, cancellationToken);

    public async Task<Result<Guid>> Handle(CreateCityCommand request, CancellationToken cancellationToken)
    {
        var ad = request.Name.Trim();

        // Benzersizligi ONCEDEN kontrol ediyorum.
        //
        // Veritabaninda zaten unique index var ve son soz onun; ama
        // oraya birakirsam kullanici "23505 duplicate key" hatasini
        // gorur. Buradaki kontrol ayni durumu ANLASILIR bir mesaja
        // ceviriyor. Yarisa acik (iki istek ayni anda gelirse ikisi de
        // "yok" gorebilir) -- kabul edilebilir, cunku asil koruma
        // index'te duruyor.
        if (await _context.Cities.AnyAsync(c => c.Name == ad, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CityErrors.NameTaken);
        }

        if (await _context.Cities
                .AnyAsync(c => c.PlateCode == request.PlateCode, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CityErrors.PlateCodeTaken);
        }

        var city = City.Create(ad, request.PlateCode);

        _context.Cities.Add(city);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(city.Id);
    }

    public async Task<Result> Handle(RenameCityCommand request, CancellationToken cancellationToken)
    {
        var city = await _context.Cities
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (city is null)
        {
            return Result.Failure(CityErrors.NotFound);
        }

        var ad = request.Name.Trim();

        // "c.Id != request.Id" sart: sehrin kendi adini kendisiyle
        // catistirmasin. Bu olmadan, adini degistirmeden kaydeden
        // admin "bu ad zaten var" hatasi alirdi.
        if (await _context.Cities
                .AnyAsync(c => c.Name == ad && c.Id != request.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(CityErrors.NameTaken);
        }

        city.Rename(ad);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> Handle(DeleteCityCommand request, CancellationToken cancellationToken)
    {
        var city = await _context.Cities
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (city is null)
        {
            return Result.Failure(CityErrors.NotFound);
        }

        var mekanVar = await _context.Venues
            .AnyAsync(v => v.CityId == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (mekanVar)
        {
            return Result.Failure(CityErrors.InUse);
        }

        // Soft delete: IsDeleted alanini SaveChanges sirasinda
        // interceptor dolduruyor (Sprint 12). Remove cagirmam
        // yeterli, kayit fiziksel olarak silinmiyor.
        _context.Cities.Remove(city);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

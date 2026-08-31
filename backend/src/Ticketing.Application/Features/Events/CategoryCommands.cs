using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Events;

internal static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "category.not_found", "Kategori bulunamadı.");

    public static readonly Error SlugTaken = Error.Conflict(
        "category.slug_taken", "Bu slug başka bir kategoriye ait.");

    public static readonly Error InUse = Error.Conflict(
        "category.in_use",
        "Bu kategoride etkinlikler var. Önce onların kategorisini değiştirin.");
}

// ---- Olusturma ----

public sealed record CreateEventCategoryCommand(
    string Name,
    string Slug,
    string? IconName,
    int DisplayOrder) : IRequest<Result<Guid>>;

public sealed class CreateEventCategoryCommandValidator
    : AbstractValidator<CreateEventCategoryCommand>
{
    public CreateEventCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Kategori adı zorunludur.")
            .MaximumLength(100);

        RuleFor(x => x.Slug)
            .NotEmpty().WithMessage("Slug zorunludur.")
            .MaximumLength(100)
            .Must(GecerliSlug)
            .WithMessage("Slug yalnızca küçük harf, rakam ve tire içerebilir. Örnek: rock-konseri");

        RuleFor(x => x.DisplayOrder)
            .InclusiveBetween(0, 999)
            .WithMessage("Sıra 0 ile 999 arasında olmalıdır.");
    }

    /// <summary>
    /// Slug bicim kontrolu.
    /// </summary>
    /// <remarks>
    /// Bu kural entity'de YOK, yalnizca burada. Sebebi su: entity
    /// slug'i bos olmadigi surece kabul ediyor ve bu dogru -- veri
    /// tasima sirasinda eski sistemden gelen bicimsiz slug'lari
    /// reddetmek istemem. Ama ARAYUZDEN girilen slug adrese
    /// yaziliyor; bosluk veya Turkce karakter iceren bir slug
    /// (/etkinlikler?kategori=rock konseri) baglantiyi bozar.
    ///
    /// Yani "olusturmada kati, tasima sirasinda esnek" ayrimini
    /// bilerek koruyorum. Etkinlik tarihlerinde de ayni tercihi
    /// yapmistim: gecmise etkinlik olusturmayi validator engelliyor,
    /// entity engellemiyor.
    ///
    /// Regex yerine elle kontrol: kural bu kadar basitken regex
    /// okunakliligi dusuruyor ve kaynak uretecine bir desen daha
    /// eklemeye degmiyor.
    /// </remarks>
    private static bool GecerliSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        foreach (var karakter in slug)
        {
            var uygun = karakter is >= 'a' and <= 'z'
                     || karakter is >= '0' and <= '9'
                     || karakter == '-';

            if (!uygun)
            {
                return false;
            }
        }

        // Basta veya sonda tire, ya da art arda iki tire: adres
        // cirkinlesiyor ve iki farkli slug ayni gorunuyor
        // ("rock--konseri" ile "rock-konseri" gozle ayirt edilemez).
        return !slug.StartsWith('-')
            && !slug.EndsWith('-')
            && !slug.Contains("--", StringComparison.Ordinal);
    }
}

// ---- Guncelleme ----

public sealed record UpdateEventCategoryCommand(
    Guid Id,
    string Name,
    string Slug,
    string? IconName,
    int DisplayOrder) : IRequest<Result>;

public sealed class UpdateEventCategoryCommandValidator
    : AbstractValidator<UpdateEventCategoryCommand>
{
    public UpdateEventCategoryCommandValidator()
    {
        // Ayni kurallar iki komutta da gecerli olmali. Tekrarlamak
        // yerine olusturma dogrulayicisini yeniden kullaniyorum:
        // ileride bir kural degisirse iki yerde birden degismesin.
        RuleFor(x => new CreateEventCategoryCommand(x.Name, x.Slug, x.IconName, x.DisplayOrder))
            .SetValidator(new CreateEventCategoryCommandValidator());
    }
}

// ---- Silme ----

public sealed record DeleteEventCategoryCommand(Guid Id) : IRequest<Result>;

internal sealed class CategoryCommandHandler
    : IRequestHandler<CreateEventCategoryCommand, Result<Guid>>,
      IRequestHandler<UpdateEventCategoryCommand, Result>,
      IRequestHandler<DeleteEventCategoryCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public CategoryCommandHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    // Kategoriler Redis'te 24 SAAT duruyor (GetCategoriesQuery).
    // Temizlemezsem admin ekledigi kategoriyi bir gun boyunca filtre
    // listesinde goremez. Sehir tarafinda da ayni notu dustum.
    private Task ClearCacheAsync(CancellationToken cancellationToken)
        => _cache.RemoveAsync(CacheKeys.Categories, cancellationToken);

    public async Task<Result<Guid>> Handle(
        CreateEventCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await _context.EventCategories
                .AnyAsync(c => c.Slug == slug, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure<Guid>(CategoryErrors.SlugTaken);
        }

        var kategori = EventCategory.Create(
            request.Name,
            slug,
            request.IconName,
            request.DisplayOrder);

        _context.EventCategories.Add(kategori);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(kategori.Id);
    }

    public async Task<Result> Handle(
        UpdateEventCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var kategori = await _context.EventCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (kategori is null)
        {
            return Result.Failure(CategoryErrors.NotFound);
        }

        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await _context.EventCategories
                .AnyAsync(c => c.Slug == slug && c.Id != request.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(CategoryErrors.SlugTaken);
        }

        kategori.Update(request.Name, slug, request.IconName, request.DisplayOrder);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> Handle(
        DeleteEventCategoryCommand request,
        CancellationToken cancellationToken)
    {
        var kategori = await _context.EventCategories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (kategori is null)
        {
            return Result.Failure(CategoryErrors.NotFound);
        }

        // Etkinligin CategoryId'si zorunlu (nullable degil). Kategoriyi
        // silseydim etkinlik silinmis bir kayda isaret ederdi ve
        // liste ekraninda kategori adi bos gorunurdu -- kimse
        // sebebini anlamazdi. Sehirde de ayni karari verdim.
        var etkinlikVar = await _context.Events
            .AnyAsync(e => e.CategoryId == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (etkinlikVar)
        {
            return Result.Failure(CategoryErrors.InUse);
        }

        _context.EventCategories.Remove(kategori);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

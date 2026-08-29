using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Events;

public sealed record CategoryDto(Guid Id, string Name, string Slug, string? IconName);

/// <summary>
/// Etkinlik kategorileri. PDF Sprint 11: "Etkinlik kategorileri" cache edilebilir.
/// </summary>
/// <remarks>
/// Sayfalama YOK -- şehir listesindeki gerekcenin aynisi: kategori
/// sayısı bir avuc ve filtre açılır listesinde tamaminin görünmesi
/// gerekiyor. Sayfalama, frontend'i "sonraki sayfa" mantığı yazmaya
/// zorlardi.
/// </remarks>
public sealed record GetCategoriesQuery : IRequest<Result<IReadOnlyList<CategoryDto>>>;

internal sealed class GetCategoriesQueryHandler
    : IRequestHandler<GetCategoriesQuery, Result<IReadOnlyList<CategoryDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;

    public GetCategoriesQueryHandler(IApplicationDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<CategoryDto>>> Handle(
        GetCategoriesQuery request,
        CancellationToken cancellationToken)
    {
        // 24 saat: kategoriler admin tarafından çok nadir eklenir.
        // Sonuç kullanicidan bağımsız -- ortak onbellekte güvenli.
        var categories = await _cache.GetOrCreateAsync(
            CacheKeys.Categories,
            LoadAsync,
            CacheDurations.ReferenceData,
            cancellationToken).ConfigureAwait(false);

        return Result.Success(categories);
    }

    private async Task<IReadOnlyList<CategoryDto>> LoadAsync(CancellationToken cancellationToken)
        => await _context.EventCategories
            .AsNoTracking()
            // DisplayOrder önce: admin kategorileri istedigi sırada
            // göstermek isteyebilir ("Konser" en ustte olsun gibi).
            // Esitlik durumunda alfabetik -- yoksa sıra veritabaninin
            // keyfine kalır ve her sorguda degisebilir.
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug, c.IconName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Reviews;

public sealed record ReviewDto(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    int Rating,
    string Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    /// <summary>Bu yorum istegi yapan kullaniciya mi ait? Duzenle/Sil dugmeleri icin.</summary>
    bool IsMine);

/// <summary>
/// Etkinligin yorum ozeti: ortalama puan ve dagilim.
/// </summary>
/// <param name="RatingCounts">
/// 1-5 arasi her puandan kac tane var. Yildiz dagilim cubugu icin.
/// </param>
public sealed record ReviewSummary(
    double AverageRating,
    int TotalCount,
    IReadOnlyDictionary<int, int> RatingCounts);

public sealed record EventReviewsResult(
    ReviewSummary Summary,
    PagedResult<ReviewDto> Reviews);

// ===================================================================
// LISTELE -- PDF: GET /api/v1/events/{eventId}/reviews
// ===================================================================

public sealed record GetEventReviewsQuery : PaginationRequest, IRequest<Result<EventReviewsResult>>
{
    public Guid EventId { get; init; }
}

internal sealed class GetEventReviewsQueryHandler
    : IRequestHandler<GetEventReviewsQuery, Result<EventReviewsResult>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public GetEventReviewsQueryHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result<EventReviewsResult>> Handle(
        GetEventReviewsQuery request,
        CancellationToken cancellationToken)
    {
        // Giris yapmamis kullanici da yorumlari gorebilir; o zaman
        // hicbir yorum "benim" olmaz.
        var currentUserId = _currentUser.UserId;

        // ==============================================================
        // GIZLENMIS YORUMLAR LISTEDE YOK
        // ==============================================================
        // PDF: "Admin uygunsuz yorumu kaldirabilir."
        //
        // Gizleme ancak yorum GORUNMEZSE bir anlam tasir. Bu filtreyi
        // unutsaydik moderasyon ozelligi hicbir ise yaramazdi -- admin
        // gizler, yorum yine gorunurdu.
        //
        // (Soft delete edilenler zaten global query filter ile eleniyor.)
        // ==============================================================
        var query = _context.Reviews
            .AsNoTracking()
            .Where(r => r.EventId == request.EventId && !r.IsHidden);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // ==============================================================
        // OZET: TEK SORGUDA GRUPLAMA
        // ==============================================================
        // Ortalamayi ve dagilimi AYRI sorgularla da alabilirdim ama:
        //
        //   - Iki gidis donus olurdu
        //   - Daha onemlisi: iki sorgu ARASINDA yeni bir yorum gelirse
        //     ortalama ile dagilim BIRBIRIYLE TUTARSIZ olurdu
        //     (ortalama 12 yoruma, dagilim 13 yoruma gore)
        //
        // Tek GroupBy ile puan basina sayimi aliyorum; ortalamayi
        // bunlardan HESAPLIYORUM. Boylece ikisi tanim geregi tutarli.
        // ==============================================================
        var dagilim = await query
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 1-5 arasi TUM puanlari dolduruyorum, yalnizca var olanlari degil.
        //
        // Frontend "3 yildiz: %0" cubugunu cizebilmeli. Eksik anahtar
        // olsaydi arayuz her seferinde varsayilan deger kontrolu
        // yapmak zorunda kalirdi.
        var counts = Enumerable.Range(1, 5)
            .ToDictionary(
                puan => puan,
                puan => dagilim.FirstOrDefault(d => d.Rating == puan)?.Count ?? 0);

        var toplamPuan = counts.Sum(x => x.Key * x.Value);

        var summary = new ReviewSummary(
            // Sifira bolme korumasi: hic yorum yoksa ortalama 0.
            //
            // Kontrol olmasaydi NaN doner, JSON'a "NaN" yazilir ve
            // frontend'de sayi ayristirma hatasi olusurdu.
            AverageRating: totalCount == 0 ? 0 : Math.Round((double)toplamPuan / totalCount, 2),
            TotalCount: totalCount,
            RatingCounts: counts);

        // ==============================================================
        // AD KISALTMASI SORGUDA DEGIL, BELLEKTE
        // ==============================================================
        // Once "FirstName + LastName.Substring(0,1)" seklinde SORGUYA
        // yazmistim. Derleyici CA1845 ile uyardi (Substring yerine
        // AsSpan kullan) -- ama AsSpan bir IFADE AGACINDA calismaz,
        // EF onu SQL'e ceviremez.
        //
        // Yani kuralin onerdigi duzeltme burada UYGULANAMAZ. Iki
        // secenek kaldi: kurali bastirmak, ya da kisaltmayi sorgudan
        // cikarmak.
        //
        // Ikincisini sectim. Yalnizca uyariyi susturmakla kalmiyor,
        // daha da dogru: string birlestirme SQL'de degil C#'ta
        // yapiliyor ve veritabani yalnizca ham sutunlari donduruyor.
        // Sayfa basina en fazla 20 satir oldugu icin bellekte
        // islemenin maliyeti yok.
        // ==============================================================
        var ham = await query
            // En yeni yorum once. Kullanici etkinligin GUNCEL durumunu
            // merak eder; iki yil onceki yorumu degil.
            .OrderByDescending(r => r.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(r => new
            {
                r.Id,
                r.UserId,
                r.User.FirstName,
                r.User.LastName,
                r.Rating,
                r.Comment,
                r.CreatedAt,
                r.UpdatedAt
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = ham.ConvertAll(r => new ReviewDto(
            r.Id,
            r.UserId,

            // ==========================================================
            // TAM AD DEGIL, KISALTILMIS AD
            // ==========================================================
            // "Adem U." seklinde donuyorum.
            //
            // Yorumlar HERKESE ACIK ve arama motorlari tarafindan
            // indekslenebilir. Tam ad + katildigi etkinlik birlesince
            // kisinin nerede oldugunu gosteren bir iz olusur.
            //
            // Soyadinin ilk harfi, ayni ada sahip iki kullaniciyi
            // ayirt etmeye yetiyor ama kimligi acik etmiyor.
            // E-posta ASLA donmuyor.
            //
            // Bos soyad kontrolu: veritabaninda zorunlu alan ama
            // savunmayi burada da tutuyorum -- tek karakterlik bir
            // varsayim yuzunden tum yorum listesinin patlamasi
            // kabul edilemez.
            $"{r.FirstName} {(r.LastName.Length > 0 ? r.LastName[0] + "." : string.Empty)}".Trim(),
            r.Rating,
            r.Comment,
            r.CreatedAt,
            r.UpdatedAt,
            currentUserId != null && r.UserId == currentUserId));

        return Result.Success(new EventReviewsResult(
            summary,
            PagedResult<ReviewDto>.Create(items, request.PageNumber, request.PageSize, totalCount)));
    }
}

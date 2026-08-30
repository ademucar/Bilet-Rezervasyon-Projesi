using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Reviews;

/// <param name="IsMine">Bu yorum isteği yapan kullanıcıya mi ait? Düzenle/Sil dugmeleri için.</param>
public sealed record ReviewDto(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    int Rating,
    string Comment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool IsMine);

/// <summary>
/// Etkinligin yorum özeti: ortalama puan ve dagilim.
/// </summary>
/// <param name="RatingCounts">
/// 1-5 arasi her puandan kac tane var. Yildiz dagilim cubugu için.
/// </param>
public sealed record ReviewSummary(
    double AverageRating,
    int TotalCount,
    IReadOnlyDictionary<int, int> RatingCounts);

public sealed record EventReviewsResult(
    ReviewSummary Summary,
    PagedResult<ReviewDto> Reviews);

// LISTELE -- PDF: GET /api/v1/events/{eventId}/reviews

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
        // Giriş yapmamis kullanıcı da yorumlari görebilir; o zaman
        // hiçbir yorum "benim" olmaz.
        var currentUserId = _currentUser.UserId;

        // Gizlenmis yorumlar listede yok
        //
        // PDF: "Admin uygunsuz yorumu kaldirabilir."
        //
        // Gizleme ancak yorum GORUNMEZSE bir anlam tasir. Bu filtreyi
        // unutsaydim moderasyon ozelligi hiçbir ise yaramazdi -- admin
        // gizler, yorum yine görünürdü.
        //
        // (Soft delete edilenler zaten global query filter ile eleniyor.)
        var query = _context.Reviews
            .AsNoTracking()
            .Where(r => r.EventId == request.EventId && !r.IsHidden);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // Ozet: tek sorguda gruplama
        //
        // Ortalamayi ve dagilimi AYRI sorgularla da alabilirdim ama:
        //
        //   - Iki gidis donus olurdu
        //   - Daha onemlisi: iki sorgu ARASINDA yeni bir yorum gelirse
        //     ortalama ile dagilim birbiriyle tutarsiz olurdu
        //     (ortalama 12 yoruma, dagilim 13 yoruma göre)
        //
        // Tek GroupBy ile puan başına sayimi alıyorum; ortalamayi
        // bunlardan HESAPLIYORUM. Boylece ikisi tanim geregi tutarli.
        var dagilim = await query
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 1-5 arasi TÜM puanlari dolduruyorum, yalnızca var olanlari değil.
        //
        // Frontend "3 yıldız: %0" cubugunu cizebilmeli. Eksik anahtar
        // olsaydı arayüz her seferinde varsayılan deger kontrolü
        // yapmak zorunda kalırdı.
        var counts = Enumerable.Range(1, 5)
            .ToDictionary(
                puan => puan,
                puan => dagilim.FirstOrDefault(d => d.Rating == puan)?.Count ?? 0);

        var toplamPuan = counts.Sum(x => x.Key * x.Value);

        var summary = new ReviewSummary(
            // Sifira bolme korumasi: hiç yorum yoksa ortalama 0.
            //
            // Kontrol olmasaydı NaN döner, JSON'a "NaN" yazilir ve
            // frontend'de sayi ayristirma hatası olusurdu.
            AverageRating: totalCount == 0 ? 0 : Math.Round((double)toplamPuan / totalCount, 2),
            TotalCount: totalCount,
            RatingCounts: counts);

        // Ad kisaltmasi sorguda değil, bellekte
        //
        // Önce "FirstName + LastName.Substring(0,1)" seklinde SORGUYA
        // yazmistim. Derleyici CA1845 ile uyardi (Substring yerine
        // AsSpan kullan) -- ama AsSpan bir ifade agacinda calismaz,
        // EF önü SQL'e ceviremez.
        //
        // Yani kuralin onerdigi duzeltme burada UYGULANAMAZ. Iki
        // seçenek kaldı: kuralı bastirmak, ya da kisaltmayi sorgudan
        // cikarmak.
        //
        // Ikincisini sectim. Yalnızca uyariyi susturmakla kalmiyor,
        // daha da doğru: string birlestirme SQL'de değil C#'ta
        // yapiliyor ve veritabani yalnızca ham sutunlari döndürüyor.
        // Sayfa başına en fazla 20 satır olduğu için bellekte
        // islemenin maliyeti yok.
        var ham = await query
            // En yeni yorum önce. Kullanıcı etkinliğin GUNCEL durumunu
            // merak eder; iki yil önceki yorumu değil.
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
                r.UpdatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = ham.ConvertAll(r => new ReviewDto(
            r.Id,
            r.UserId,

            // Tam ad değil, kisaltilmis ad
            //
            // "Adem U." seklinde donuyorum.
            //
            // Yorumlar herkese acik ve arama motorlari tarafından
            // indekslenebilir. Tam ad + katildigi etkinlik birlesince
            // kisinin nerede olduğunu gosteren bir iz olusur.
            //
            // Soyadinin ilk harfi, aynı ada sahip iki kullanıcıyı
            // ayırt etmeye yetiyor ama kimliği açık etmiyor.
            // E-posta ASLA donmuyor.
            //
            // Boş soyad kontrolü: veritabaninda zorunlu alan ama
            // savunmayi burada da tutuyorum -- tek karakterlik bir
            // varsayim yuzunden tüm yorum listesinin patlamasi
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

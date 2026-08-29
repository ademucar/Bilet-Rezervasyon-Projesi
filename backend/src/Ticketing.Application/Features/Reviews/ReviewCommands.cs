using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Reviews;

internal static class ReviewErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "review.not_found", "Yorum bulunamadı.");

    public static readonly Error EventNotFound = Error.NotFound(
        "review.event_not_found", "Etkinlik bulunamadı.");

    /// <summary>PDF: "Etkinlik tamamlanmadan yorum yapılamaz."</summary>
    public static readonly Error EventNotCompleted = Error.Conflict(
        "review.event_not_completed",
        "Yorum yalnızca tamamlanmis etkinlikler için yapilabilir.");

    /// <summary>PDF: "Yalnızca etkinlige geçerli bilet almis kullanıcı yorum yapabilir."</summary>
    public static readonly Error NoValidTicket = Error.Forbidden(
        "review.no_valid_ticket",
        "Bu etkinlige geçerli biletiniz olmadığı için yorum yapamazsiniz.");

    /// <summary>PDF: "Kullanıcı etkinlik başına bir yorum olusturabilir."</summary>
    public static readonly Error AlreadyReviewed = Error.Conflict(
        "review.already_exists",
        "Bu etkinlik için zaten bir yorumunuz var. Mevcut yorumunuzu duzenleyebilirsiniz.");

    /// <summary>PDF: "Kullanıcı yalnızca kendi yorumunu düzenleyebilir."</summary>
    public static readonly Error NotOwner = Error.Forbidden(
        "review.not_owner", "Yalnızca kendi yorumunuzu duzenleyebilirsiniz.");
}

// ===================================================================
// ORTAK KURAL KONTROLU
// ===================================================================

/// <summary>
/// PDF'in yorum yapma on kosullarini kontrol eder.
/// </summary>
/// <remarks>
/// Ayrı bir sinifta çünkü AYNI kontroller hem yorum olusturmada hem de
/// (ileride) "yorum yapabilir miyim?" sorgusunda gerekiyor. Iki yerde
/// kopyalasaydik birini guncelleyip digerini unutmak kacinilmazdi --
/// ve sonuç bir GÜVENLİK acigi olurdu: uc kontrol etmeyi birakir,
/// bilet almamis biri yorum yazardi.
/// </remarks>
internal static class ReviewEligibility
{
    /// <summary>
    /// ==============================================================
    /// "GECERLI BİLET" NE DEMEK? -- PDF'in soylemedigi ayrinti
    /// ==============================================================
    /// PDF "geçerli bilet almis kullanıcı" diyor ama hangi bilet
    /// durumlarinin geçerli sayilacagini soylemiyor. Karar bana ait:
    ///
    ///   Active  -> GECERLI. Bileti var, etkinlik bitti ama turnikeden
    ///              gecmemis olabilir (geç kalmis, kapida okutulmamis).
    ///              Parasini odedi, deneyimi hakkında konusabilir.
    ///
    ///   Used    -> GECERLI. Girişte okutuldu, kesinlikle katildi.
    ///              Yorum yapmaya en çok hakki olan kişi.
    ///
    ///   Refunded-> GECERSIZ. Parasini geri aldi. Etkinlige gitmedi
    ///              ve maddi bir bagi kalmadi.
    ///
    ///   Cancelled/Expired -> GECERSIZ. Bilet hiç geçerli olmadi.
    ///
    /// Neden Refunded'i disliyorum? Çünkü aksi halde bilet alip hemen
    /// iade eden biri yorum hakki kazanirdi. Bu, sahte yorum uretmenin
    /// en ucuz yolu olurdu: al, iade et, kötü puan ver.
    /// ==============================================================
    /// </summary>
    public static readonly TicketStatus[] ValidTicketStatuses =
    [
        TicketStatus.Active,
        TicketStatus.Used
    ];

    public static Task<bool> HasValidTicketAsync(
        IApplicationDbContext context,
        Guid userId,
        Guid eventId,
        CancellationToken cancellationToken)
        => context.Tickets
            .AsNoTracking()
            .AnyAsync(
                t => t.UserId == userId
                  && t.EventSeat.EventSession.EventId == eventId
                  && ValidTicketStatuses.Contains(t.Status),
                cancellationToken);
}

// ===================================================================
// OLUSTUR -- PDF: POST /api/v1/events/{eventId}/reviews
// ===================================================================

public sealed record CreateReviewCommand(Guid EventId, int Rating, string Comment)
    : IRequest<Result<Guid>>;

public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator()
    {
        // PDF: "Puan 1 ile 5 arasında olmalıdır."
        //
        // Bu kural UC YERDE birden var ve bu tekrar KASITLI:
        //   1. Burada (FluentValidation) -> kullanıcıya 400 + açık mesaj
        //   2. Review.Create             -> entity kendini korur
        //   3. CHECK constraint          -> SQL ile giren veri de gecemez
        //
        // Her katman farklı bir saldiri/hata yuzeyini kapatiyor.
        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5)
            .WithMessage("Puan 1 ile 5 arasında olmalıdır.");

        RuleFor(x => x.Comment)
            .NotEmpty().WithMessage("Yorum metni boş olamaz.")
            .MaximumLength(2000);
    }
}

internal sealed class CreateReviewCommandHandler
    : IRequestHandler<CreateReviewCommand, Result<Guid>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheService _cache;

    public CreateReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        ICacheService cache)
    {
        _context = context;
        _currentUser = currentUser;
        _cache = cache;
    }

    public async Task<Result<Guid>> Handle(
        CreateReviewCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<Guid>(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // ==============================================================
        // KURAL 1: Etkinlik var mi ve TAMAMLANDI mi?
        // ==============================================================
        // PDF: "Etkinlik tamamlanmadan yorum yapılamaz."
        //
        // Neden bu kural var? Çünkü yorum bir DENEYIM anlatisidir.
        // Henüz gerceklesmemis bir konser hakkında "harikaydi" veya
        // "berbatti" demek anlamsiz -- ve manipulasyona açık.
        //
        // Yalnızca durumu okuyorum, entity'nin tamamini değil:
        // güncelleme yapmayacagim.
        // ==============================================================
        var eventInfo = await _context.Events
            .AsNoTracking()
            .Where(e => e.Id == request.EventId)
            .Select(e => new { e.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (eventInfo is null)
        {
            return Result.Failure<Guid>(ReviewErrors.EventNotFound);
        }

        if (eventInfo.Status != EventStatus.Completed)
        {
            return Result.Failure<Guid>(ReviewErrors.EventNotCompleted);
        }

        // ==============================================================
        // KURAL 2: Geçerli bilet var mi?
        // ==============================================================
        // PDF: "Yalnızca etkinlige geçerli bilet almis kullanıcı yorum
        // yapabilir."
        //
        // Bu kural yorumlarin GUVENILIRLIGINI koruyor. Olmasaydı
        // rakip bir organizatör sahte hesaplarla puan dusurebilirdi.
        // ==============================================================
        var hasTicket = await ReviewEligibility
            .HasValidTicketAsync(_context, userId, request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (!hasTicket)
        {
            return Result.Failure<Guid>(ReviewErrors.NoValidTicket);
        }

        // ==============================================================
        // KURAL 3: Etkinlik başına TEK yorum
        // ==============================================================
        // PDF: "Kullanıcı etkinlik başına bir yorum olusturabilir."
        //
        // Bu kontrol YARISA ACIK: iki istek aynı anda gelirse ikisi de
        // "yok" görebilir. Sorun değil -- veritabanindaki
        // ix_reviews_user_event UNIQUE index'i ikincisini reddedecek
        // ve aşağıda yakaliyorum.
        //
        // Buradaki kontrol YAYGIN durumu (kullanıcı ikinci kez yorum
        // yazmaya çalışıyor) ucuz ve ANLASILIR bir mesajla cozuyor.
        // ==============================================================
        var alreadyExists = await _context.Reviews
            .AsNoTracking()
            .AnyAsync(r => r.UserId == userId && r.EventId == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            return Result.Failure<Guid>(ReviewErrors.AlreadyReviewed);
        }

        var review = Review.Create(userId, request.EventId, request.Rating, request.Comment);

        _context.Reviews.Add(review);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // UNIQUE index ihlali: yaris durumunda ikinci istek buraya duser.
            //
            // Kullanıcıya "beklenmedik hata" demek yerine gerçek sebebi
            // soyluyoruz -- zaten bir yorumu var.
            return Result.Failure<Guid>(ReviewErrors.AlreadyReviewed);
        }

        // Etkinlik detayı ortalama puanı tasiyor; önbellek bayatladi.
        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(review.Id);
    }
}

// ===================================================================
// GUNCELLE -- PDF: PUT /api/v1/reviews/{id}
// ===================================================================

public sealed record UpdateReviewCommand(Guid Id, int Rating, string Comment) : IRequest<Result>;

public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5)
            .WithMessage("Puan 1 ile 5 arasında olmalıdır.");

        RuleFor(x => x.Comment).NotEmpty().MaximumLength(2000);
    }
}

internal sealed class UpdateReviewCommandHandler : IRequestHandler<UpdateReviewCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheService _cache;

    public UpdateReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        ICacheService cache)
    {
        _context = context;
        _currentUser = currentUser;
        _cache = cache;
    }

    public async Task<Result> Handle(UpdateReviewCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (review is null)
        {
            return Result.Failure(ReviewErrors.NotFound);
        }

        // ==============================================================
        // PDF: "Kullanıcı yalnızca kendi yorumunu düzenleyebilir."
        // ==============================================================
        // Burada 404 DEĞİL 403 donuyorum -- rezervasyon ve odemede
        // verdigim karardan FARKLI. Sebep:
        //
        // Yorumlar HERKESE ACIK. Kullanıcı zaten etkinlik sayfasinda
        // baskasinin yorumunu görüyor ve Id'sini biliyor. "Bu yorum
        // yok" demek sacma olurdu -- gozunun onunde duruyor.
        //
        // Rezervasyonda 404 dondurmustum çünkü orada kaydin VARLIGI
        // gizli bilgiydi. Burada değil. Kural ezbere değil, neyin
        // gizli olduğu dusunulerek uygulanmali.
        // ==============================================================
        if (review.UserId != userId)
        {
            return Result.Failure(ReviewErrors.NotOwner);
        }

        // Gizlenmis yorumun duzenlenmesini entity engelliyor
        // (DomainException -> 422). Admin gizlemisse kullanıcı
        // metni degistirip tekrar yayina sokamamali.
        review.Update(request.Rating, request.Comment);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// SIL -- PDF: DELETE /api/v1/reviews/{id}
// ===================================================================

/// <param name="HideReason">
/// Admin gizliyorsa sebep. Kullanıcı kendi yorumunu siliyorsa null.
/// </param>
public sealed record DeleteReviewCommand(Guid Id, string? HideReason) : IRequest<Result>;

internal sealed class DeleteReviewCommandHandler : IRequestHandler<DeleteReviewCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheService _cache;

    public DeleteReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        ICacheService cache)
    {
        _context = context;
        _currentUser = currentUser;
        _cache = cache;
    }

    public async Task<Result> Handle(DeleteReviewCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (review is null)
        {
            return Result.Failure(ReviewErrors.NotFound);
        }

        var isAdmin = _currentUser.Roles.Contains(Role.Names.Admin);

        // ==============================================================
        // AYNI UC, IKI FARKLI ISLEM -- BILINCLI
        // ==============================================================
        // PDF iki ayrı kural veriyor:
        //   "Kullanıcı yalnızca kendi yorumunu düzenleyebilir."
        //   "Admin uygunsuz yorumu kaldirabilir."
        //
        // Ikisi aynı uctan yonetiliyor ama SONUCLARI FARKLI:
        //
        //   KULLANICI  -> soft delete. Yorum kaybolur; kullanıcı
        //                 isterse yenisini yazabilir (unique index
        //                 IsDeleted=false filtreli olduğu için buna
        //                 izin veriyor).
        //
        //   ADMIN      -> GIZLEME (IsHidden). Kayıt durur, denetim
        //                 izi korunur, kullanıcı yerine yenisini
        //                 yazamaz.
        //
        // Neden admin de silmiyor? Çünkü silinen bir yorumun yerine
        // kullanıcı aynisini tekrar yazabilirdi ve moderasyon
        // sonsuz bir kovalamacaya donerdi. Gizlemek kalici.
        //
        // Ayrıca "neden yorumum kayboldu?" sorusuna cevap verebilmek
        // için HiddenReason saklaniyor.
        // ==============================================================
        if (isAdmin && review.UserId != userId)
        {
            review.Hide(request.HideReason ?? "Uygunsuz icerik.");
        }
        else if (review.UserId == userId)
        {
            // Soft delete: AuditableEntity uzerindeki IsDeleted.
            // Global query filter sayesinde artık hiçbir sorguda
            // gorunmeyecek.
            review.IsDeleted = true;
        }
        else
        {
            return Result.Failure(ReviewErrors.NotOwner);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }
}

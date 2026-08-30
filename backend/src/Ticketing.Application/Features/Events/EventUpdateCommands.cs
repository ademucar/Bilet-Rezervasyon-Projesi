using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions.Caching;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Enums;

namespace Ticketing.Application.Features.Events;

// Etkinlik guncelleme ve silme
//
// PDF Sprint 5 acikca su iki ucu istiyor:
//     PUT    /api/v1/events/{id}
//     DELETE /api/v1/events/{id}
//
// Sprint 19 denetiminde ikisinin de EKSIK olduğunu buldum. Domain
// tarafında UpdateDetails() ve UpdateDates() metotlari sprint 5'ten
// BERI vardi -- ama onlari cagiran hiçbir uc yoktu. Yani yazilmis
// ama erisilemeyen kod.
//
// Bu, projede tekrar eden desenin bir başkası: Sprint 12 (denetim
// alanlari), 15 (maskeleyici), 16 (correlation ID), 17 (idempotency),
// 18 (XML yorumlari), 19 (Docker imaji). Hepsi "var ama calismiyor".

/// <summary>
/// Etkinligin duzenlenebilir alanlarini günceller. PDF: PUT /api/v1/events/{id}
/// </summary>
/// <remarks>
/// İki farkli kural seti var ve domain bunu zaten ayiriyor
///
/// PDF is kuralı: "Yayina alinmis etkinliğin kritik alanlari
/// KONTROLSUZ degistirilemez."
///
/// "Kontrolsuz" kelimesi önemli: hiçbir sey degistirilemez demiyor.
/// Domain katmani ayrimi zaten yapiyor:
///
///   UpdateDetails -> başlık, açıklama, yaş sınırı
///                    Yayindayken de degisebilir. Yazim hatası
///                    duzeltmek yasak olmamali.
///                    Yalnızca iptal/tamamlanmis etkinlikte kapalı.
///
///   UpdateDates   -> etkinlik ve satış tarihleri
///                    satis basladiysa kapalı. Bilet almis
///                    kullanicilarin altindan tarihi cekmek olmaz.
///
/// Bu handler ikisini AYRI cagiriyor: kullanıcı yalnızca başlığı
/// degistirmek istiyorsa, tarih kurallari devreye girmiyor.
/// </remarks>
public sealed record UpdateEventCommand(
    Guid EventId,
    string Title,
    string Description,
    int? MinimumAge,
    DateTimeOffset? EventDate,
    DateTimeOffset? SalesStartDate,
    DateTimeOffset? SalesEndDate) : IRequest<Result>;

public sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    public UpdateEventCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Başlık gereklidir.")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Açıklama gereklidir.")
            .MaximumLength(4000);

        RuleFor(x => x.MinimumAge)
            .InclusiveBetween(0, 99)
            .When(x => x.MinimumAge.HasValue);

        // Tarihler ya hep ya hiç
        //
        // Ucunden yalnızca birini gonderirsek diger ikisi eski
        // degerinde kalır ve tutarsiz bir kombinasyon olusabilir
        // (örneğin satış bitisi yeni etkinlik tarihinden sonra).
        //
        // Domain zaten ValidateDates ile bunu yakalar ama hatayi
        // ISTEK seviyesinde vermek daha net: kullanıcı "eksik alan"
        // mesaji görüyor, "geçersiz tarih aralığı" değil.
        RuleFor(x => x)
            .Must(x =>
            {
                var dolu = new[] { x.EventDate, x.SalesStartDate, x.SalesEndDate }
                    .Count(d => d.HasValue);

                return dolu is 0 or 3;
            })
            .WithMessage(
                "Tarih alanlarinin ucu birden gonderilmeli veya hicbiri " +
                "gonderilmemelidir.");
    }
}

internal sealed partial class UpdateEventCommandHandler
    : IRequestHandler<UpdateEventCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<UpdateEventCommandHandler> _logger;

    public UpdateEventCommandHandler(
        IApplicationDbContext context,
        ICacheService cache,
        ILogger<UpdateEventCommandHandler> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result> Handle(
        UpdateEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var evt = await _context.Events
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        // Domain kurallari burada isliyor; ihlalde DomainException
        // firlatiyor ve GlobalExceptionHandler önü 422'ye ceviriyor.
        evt.UpdateDetails(request.Title, request.Description, request.MinimumAge);

        if (request.EventDate.HasValue
            && request.SalesStartDate.HasValue
            && request.SalesEndDate.HasValue)
        {
            evt.UpdateDates(
                request.EventDate.Value,
                request.SalesStartDate.Value,
                request.SalesEndDate.Value);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogEventUpdated(_logger, evt.Id, evt.Title);

        // Onbellek temizligi şart
        //
        // Etkinlik detayı ve popüler listesi onbellekte duruyor
        // (Sprint 11). Temizlemezsek kullanıcı başlığı değiştirir,
        // sayfayı yeniler ve ESKİ başlığı görür -- "kaydedilmedi mi?"
        // diye tekrar dener.
        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }

    [LoggerMessage(
        EventId = LogEvents.EtkinlikGuncellendi,
        Level = LogLevel.Information,
        Message = "Etkinlik güncellendi. Id: {EventId}, Baslik: {Title}")]
    private static partial void LogEventUpdated(ILogger logger, Guid eventId, string title);
}

/// <summary>
/// Etkinligi siler (soft delete). PDF: DELETE /api/v1/events/{id}
/// </summary>
/// <remarks>
/// Fiziksel silme yok -- soft DELETE
///
/// AuditableEntity uzerindeki IsDeleted alanı isaretleniyor ve global
/// sorgu filtresi kaydı gizliyor.
///
/// Neden fiziksel silmiyorum?
///   - Etkinlige bağlı bilet, ödeme ve denetim kayitlari var. Fiziksel
///     silme ya bunlari da silerdi (mali kayıt kaybi) ya da yabanci
///     anahtar hatası verirdi.
///   - Silme KARARININ kendisi bir denetim verisi: "kim, ne zaman
///     sildi" sorusu cevaplanabilmeli.
///
/// Hangi etkinlik silinebilir?
///
/// Yalnızca hiç bilet satilmamis olanlar.
///
/// Bileti olan bir etkinligi silmek, o bileti almis kullanicilarin
/// elindeki bileti geçersiz kilardi -- ve onlara hiçbir sey
/// soylenmemis olurdu. Boyle bir durumda doğru işlem SILMEK değil,
/// İPTAL etmek (POST /events/{id}/cancel): iptal, iade zincirini ve
/// bildirimleri baslatiyor.
///
/// Bu ayrimi kod içinde net tutuyorum ki ilerde biri "silme neden
/// calismiyor?" diye sordugunda cevap hazır olsun.
/// </remarks>
public sealed record DeleteEventCommand(Guid EventId) : IRequest<Result>;

internal sealed partial class DeleteEventCommandHandler
    : IRequestHandler<DeleteEventCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cache;
    private readonly ILogger<DeleteEventCommandHandler> _logger;

    public DeleteEventCommandHandler(
        IApplicationDbContext context,
        ICacheService cache,
        ILogger<DeleteEventCommandHandler> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result> Handle(
        DeleteEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var evt = await _context.Events
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            .ConfigureAwait(false);

        if (evt is null)
        {
            return Result.Failure(EventErrors.NotFound);
        }

        // Satilmis bilet var mi?
        //
        // Koltuk durumuna DEĞİL, bilet kaydina bakiyorum.
        //
        // Koltuk "Locked" olabilir (biri secmis ama odememis) --
        // o kilit 10 dakikada dusuyor ve silmeyi engellememeli.
        // Ama bir BİLET uretilmisse para alinmis demektir.
        var oturumIdleri = evt.Sessions.Select(s => s.Id).ToList();

        var biletVar = await _context.Tickets
            .AnyAsync(t => oturumIdleri.Contains(t.EventSessionId), cancellationToken)
            .ConfigureAwait(false);

        if (biletVar)
        {
            return Result.Failure(Error.Conflict(
                "event.has_tickets",
                "Bileti satilmis etkinlik silinemez. Bunun yerine etkinligi " +
                "iptal edin: iptal, iade surecini ve kullanici bildirimlerini " +
                "baslatir."));
        }

        // Aktif (süresi dolmamis) rezervasyon da engelliyor: kullanıcı
        // o an ödeme ekraninda olabilir.
        var aktifRezervasyon = await _context.Reservations
            .AnyAsync(
                r => oturumIdleri.Contains(r.EventSessionId)
                  && (r.Status == ReservationStatus.Locked
                   || r.Status == ReservationStatus.PaymentPending
                   || r.Status == ReservationStatus.Confirmed),
                cancellationToken)
            .ConfigureAwait(false);

        if (aktifRezervasyon)
        {
            return Result.Failure(Error.Conflict(
                "event.has_active_reservations",
                "Aktif rezervasyonu olan etkinlik silinemez."));
        }

        // AuditFieldsInterceptor silmeyi soft DELETE'E ceviriyor
        //
        // Remove() cagiriyoruz ama kayıt FIZIKSEL olarak silinmiyor:
        // Sprint 12'de yazdigim interceptor EntityState.Deleted'i
        // yakalayip IsDeleted = true yapiyor.
        //
        // Burada Remove() yazmak, "silme niyeti"ni normal EF diliyle
        // ifade etmemizi sagliyor; soft delete davranisi tek yerde
        // (interceptor) duruyor ve her silme için tekrar edilmiyor.
        _context.Events.Remove(evt);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogEventDeleted(_logger, evt.Id, evt.Title);

        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }

    /// <remarks>
    /// Warning seviyesi: silme geri alinmasi zor bir işlem ve
    /// denetimde gorulmesi gerekiyor (Sprint 16'daki iade/iptal
    /// kararlarinin aynisi).
    /// </remarks>
    [LoggerMessage(
        EventId = LogEvents.EtkinlikSilindi,
        Level = LogLevel.Warning,
        Message = "Etkinlik SILINDI (soft delete). Id: {EventId}, Baslik: {Title}")]
    private static partial void LogEventDeleted(ILogger logger, Guid eventId, string title);
}

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

// ===================================================================
// ETKINLIK GUNCELLEME VE SILME
// ===================================================================
// PDF Sprint 5 acikca su iki ucu istiyor:
//     PUT    /api/v1/events/{id}
//     DELETE /api/v1/events/{id}
//
// Sprint 19 denetiminde ikisinin de EKSIK oldugunu buldum. Domain
// tarafinda UpdateDetails() ve UpdateDates() metotlari SPRINT 5'TEN
// BERI vardi -- ama onlari cagiran hicbir uc yoktu. Yani yazilmis
// ama erisilemeyen kod.
//
// Bu, projede tekrar eden desenin bir baskasi: Sprint 12 (denetim
// alanlari), 15 (maskeleyici), 16 (correlation ID), 17 (idempotency),
// 18 (XML yorumlari), 19 (Docker imaji). Hepsi "var ama calismiyor".
// ===================================================================

/// <summary>
/// Etkinligin duzenlenebilir alanlarini gunceller. PDF: PUT /api/v1/events/{id}
/// </summary>
/// <remarks>
/// ==================================================================
/// IKI FARKLI KURAL SETI VAR VE DOMAIN BUNU ZATEN AYIRIYOR
/// ==================================================================
/// PDF is kurali: "Yayina alinmis etkinligin kritik alanlari
/// KONTROLSUZ degistirilemez."
///
/// "Kontrolsuz" kelimesi onemli: hicbir sey degistirilemez demiyor.
/// Domain katmani ayrimi zaten yapiyor:
///
///   UpdateDetails -> baslik, aciklama, yas siniri
///                    Yayindayken de degisebilir. Yazim hatasi
///                    duzeltmek yasak olmamali.
///                    Yalnizca iptal/tamamlanmis etkinlikte kapali.
///
///   UpdateDates   -> etkinlik ve satis tarihleri
///                    SATIS BASLADIYSA kapali. Bilet almis
///                    kullanicilarin altindan tarihi cekmek olmaz.
///
/// Bu handler ikisini AYRI cagiriyor: kullanici yalnizca basligi
/// degistirmek istiyorsa, tarih kurallari devreye girmiyor.
/// ==================================================================
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
            .NotEmpty().WithMessage("Baslik gereklidir.")
            .MaximumLength(200);

        RuleFor(x => x.Description)
            .NotEmpty().WithMessage("Aciklama gereklidir.")
            .MaximumLength(4000);

        RuleFor(x => x.MinimumAge)
            .InclusiveBetween(0, 99)
            .When(x => x.MinimumAge.HasValue);

        // ==========================================================
        // TARIHLER YA HEP YA HIC
        // ==========================================================
        // Ucunden yalnizca birini gonderirsek diger ikisi eski
        // degerinde kalir ve tutarsiz bir kombinasyon olusabilir
        // (ornegin satis bitisi yeni etkinlik tarihinden sonra).
        //
        // Domain zaten ValidateDates ile bunu yakalar ama hatayi
        // ISTEK seviyesinde vermek daha net: kullanici "eksik alan"
        // mesaji goruyor, "gecersiz tarih araligi" degil.
        // ==========================================================
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
        // firlatiyor ve GlobalExceptionHandler onu 422'ye ceviriyor.
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

        // ==============================================================
        // ONBELLEK TEMIZLIGI SART
        // ==============================================================
        // Etkinlik detayi ve populer listesi onbellekte duruyor
        // (Sprint 11). Temizlemezsek kullanici basligi degistirir,
        // sayfayi yeniler ve ESKI basligi gorur -- "kaydedilmedi mi?"
        // diye tekrar dener.
        // ==============================================================
        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }

    [LoggerMessage(
        EventId = LogEvents.EtkinlikGuncellendi,
        Level = LogLevel.Information,
        Message = "Etkinlik guncellendi. Id: {EventId}, Baslik: {Title}")]
    private static partial void LogEventUpdated(ILogger logger, Guid eventId, string title);
}

/// <summary>
/// Etkinligi siler (soft delete). PDF: DELETE /api/v1/events/{id}
/// </summary>
/// <remarks>
/// ==================================================================
/// FIZIKSEL SILME YOK -- SOFT DELETE
/// ==================================================================
/// AuditableEntity uzerindeki IsDeleted alani isaretleniyor ve global
/// sorgu filtresi kaydi gizliyor.
///
/// Neden fiziksel silmiyoruz?
///   - Etkinlige bagli bilet, odeme ve denetim kayitlari var. Fiziksel
///     silme ya bunlari da silerdi (mali kayit kaybi) ya da yabanci
///     anahtar hatasi verirdi.
///   - Silme KARARININ kendisi bir denetim verisi: "kim, ne zaman
///     sildi" sorusu cevaplanabilmeli.
///
/// ------------------------------------------------------------------
/// HANGI ETKINLIK SILINEBILIR?
/// ------------------------------------------------------------------
/// Yalnizca HIC BILET SATILMAMIS olanlar.
///
/// Bileti olan bir etkinligi silmek, o bileti almis kullanicilarin
/// elindeki bileti gecersiz kilardi -- ve onlara hicbir sey
/// soylenmemis olurdu. Boyle bir durumda dogru islem SILMEK degil,
/// IPTAL etmek (POST /events/{id}/cancel): iptal, iade zincirini ve
/// bildirimleri baslatiyor.
///
/// Bu ayrimi kod icinde net tutuyorum ki ilerde biri "silme neden
/// calismiyor?" diye sordugunda cevap hazir olsun.
/// ==================================================================
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

        // ==============================================================
        // SATILMIS BILET VAR MI?
        // ==============================================================
        // Koltuk durumuna DEGIL, bilet kaydina bakiyorum.
        //
        // Koltuk "Locked" olabilir (biri secmis ama odememis) --
        // o kilit 10 dakikada dusuyor ve silmeyi engellememeli.
        // Ama bir BILET uretilmisse para alinmis demektir.
        // ==============================================================
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

        // Aktif (suresi dolmamis) rezervasyon da engelliyor: kullanici
        // o an odeme ekraninda olabilir.
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

        // ==============================================================
        // AuditFieldsInterceptor SILMEYI SOFT DELETE'E CEVIRIYOR
        // ==============================================================
        // Remove() cagiriyoruz ama kayit FIZIKSEL olarak silinmiyor:
        // Sprint 12'de yazdigimiz interceptor EntityState.Deleted'i
        // yakalayip IsDeleted = true yapiyor.
        //
        // Burada Remove() yazmak, "silme niyeti"ni normal EF diliyle
        // ifade etmemizi sagliyor; soft delete davranisi tek yerde
        // (interceptor) duruyor ve her silme icin tekrar edilmiyor.
        // ==============================================================
        _context.Events.Remove(evt);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogEventDeleted(_logger, evt.Id, evt.Title);

        await _cache.RemoveByPrefixAsync(CacheKeys.EventPrefix, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success();
    }

    /// <remarks>
    /// Warning seviyesi: silme geri alinmasi zor bir islem ve
    /// denetimde gorulmesi gerekiyor (Sprint 16'daki iade/iptal
    /// kararlarinin aynisi).
    /// </remarks>
    [LoggerMessage(
        EventId = LogEvents.EtkinlikSilindi,
        Level = LogLevel.Warning,
        Message = "Etkinlik SILINDI (soft delete). Id: {EventId}, Baslik: {Title}")]
    private static partial void LogEventDeleted(ILogger logger, Guid eventId, string title);
}

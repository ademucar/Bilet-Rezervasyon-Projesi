using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Options;
using Ticketing.Application.Common.Results;

namespace Ticketing.Application.Features.Reservations;

// ===================================================================
// IPTAL -- PDF: POST /api/v1/reservations/{id}/cancel
// ===================================================================

public sealed record CancelReservationCommand(Guid Id, string? Reason) : IRequest<Result>;

internal sealed class CancelReservationCommandHandler
    : IRequestHandler<CancelReservationCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CancelReservationCommandHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CancelReservationCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        // Items ve icindeki EventSeat'leri yukluyorum: koltuklari
        // SERBEST BIRAKACAGIZ ve bunun icin takip edilmeleri gerekiyor.
        var reservation = await _context.Reservations
            .Include(r => r.Items)
                .ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null)
        {
            return Result.Failure(ReservationErrors.NotFound);
        }

        // Sahiplik kontrolu.
        //
        // 403 degil 404 donuyorum: baskasinin rezervasyonunun VAR
        // oldugunu dogrulamamak icin. 403 deseydim, saldirgan Id
        // tarayip hangi rezervasyonlarin var oldugunu ogrenebilirdi.
        if (reservation.UserId != userId)
        {
            return Result.Failure(ReservationErrors.NotFound);
        }

        // Durum gecisi entity'de dogrulaniyor: zaten iptal edilmis
        // veya onaylanmis bir rezervasyon iptal edilemez.
        reservation.Cancel(request.Reason);

        // ==============================================================
        // KOLTUKLARI SERBEST BIRAK
        // ==============================================================
        // Bu adim ATLANIRSA en kotu hata olusur: rezervasyon iptal
        // gorunur ama koltuklar 10 dakika daha kilitli kalir.
        // Kullanici "iptal ettim ama koltuk hala dolu" der ve
        // sebebini kimse anlayamaz.
        //
        // Release, satilmis koltugu reddediyor -- iptal edilen bir
        // rezervasyonda satilmis koltuk olamaz ama savunmayi
        // entity'de tutuyoruz.
        foreach (var item in reservation.Items)
        {
            item.EventSeat.Release();
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// ===================================================================
// SURE UZATMA -- PDF: POST /api/v1/reservations/{id}/extend
// ===================================================================

public sealed record ExtendReservationCommand(Guid Id) : IRequest<Result<ReservationDto>>;

internal sealed class ExtendReservationCommandHandler
    : IRequestHandler<ExtendReservationCommand, Result<ReservationDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ReservationOptions _options;

    public ExtendReservationCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IOptions<ReservationOptions> options)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<ReservationDto>> Handle(
        ExtendReservationCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<ReservationDto>(
                Error.Unauthorized("auth.required", "Giris yapmalisiniz."));
        }

        var now = _clock.UtcNow;

        var reservation = await _context.Reservations
            .Include(r => r.Items)
                .ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null || reservation.UserId != userId)
        {
            return Result.Failure<ReservationDto>(ReservationErrors.NotFound);
        }

        // Entity kontrol ediyor:
        //   - Yalnizca Locked durumunda uzatilabilir
        //   - Suresi DOLMUS rezervasyon uzatilamaz (diriltilemez)
        //   - Uzatma limiti asilamaz
        reservation.Extend(
            TimeSpan.FromMinutes(_options.MaxExtensionMinutes),
            _options.MaxExtensionCount,
            now);

        // ==============================================================
        // KOLTUKLARIN KILIT SURESINI DE UZAT
        // ==============================================================
        // Bu adim atlanirsa TUTARSIZLIK olusur: rezervasyon 15 dakika
        // gecerli gorunur ama koltuklar 10. dakikada "musait" olur ve
        // baskasi alabilir.
        //
        // Rezervasyon ile koltuk sureleri HER ZAMAN ayni olmali.
        // Ikisini ayri yerlerde tuttugumuz icin bu senkronizasyon
        // bizim sorumlulugumuzda.
        foreach (var item in reservation.Items)
        {
            item.EventSeat.ExtendLock(reservation.ExpiresAt);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var dto = await _context.Reservations
            .Where(r => r.Id == reservation.Id)
            .ToDto(_context)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return dto is null
            ? Result.Failure<ReservationDto>(ReservationErrors.NotFound)
            : Result.Success(ReservationQueries.WithRemainingSeconds(dto, now));
    }
}

// ===================================================================
// SURESI DOLANLARI TEMIZLE -- background job cagiracak (Sprint 9)
// ===================================================================

/// <summary>
/// Suresi dolmus rezervasyonlari iptal eder ve koltuklari serbest birakir.
///
/// PDF Sprint 7: "Suresi dolan rezervasyon otomatik olarak iptal
/// edilmelidir."
/// PDF Sprint 9: "Suresi dolan rezervasyonlari iptal etme" job'i.
///
/// Simdilik endpoint olarak da acik (admin tetikleyebilsin ve test
/// edebilelim). Sprint 9'da Hangfire ile dakikada bir calisacak.
/// </summary>
public sealed record ExpireReservationsCommand(int BatchSize = 100) : IRequest<Result<int>>;

internal sealed class ExpireReservationsCommandHandler
    : IRequestHandler<ExpireReservationsCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;

    public ExpireReservationsCommandHandler(IApplicationDbContext context, IDateTimeProvider clock)
    {
        _context = context;
        _clock = clock;
    }

    public async Task<Result<int>> Handle(
        ExpireReservationsCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // ==============================================================
        // TOPLU ISLEM SINIRI (BatchSize)
        // ==============================================================
        // Sinir olmasaydi, sistem bir sure durup 50.000 suresi dolmus
        // rezervasyon birikseydi, job hepsini tek transaction'da
        // islemeye calisir ve dakikalarca tablo kilitlerdi.
        //
        // Parca parca islemek, job'in her calismasinda kisa surmesini
        // ve digerlerinin bir sonraki calismada islenmesini saglar.
        //
        // ix_reservations_status_expires index'i bu sorguyu karsiliyor.
        var expired = await _context.Reservations
            .Include(r => r.Items)
                .ThenInclude(i => i.EventSeat)
            .Where(r => (r.Status == Domain.Enums.ReservationStatus.Locked
                      || r.Status == Domain.Enums.ReservationStatus.PaymentPending)
                     && r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .Take(request.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var reservation in expired)
        {
            reservation.Expire(now);

            foreach (var item in reservation.Items)
            {
                // Satilmis koltugu atla.
                //
                // Nasil olur? Odeme tam bu sirada tamamlanmis olabilir:
                // rezervasyon PaymentPending, sure doldu, ama odeme
                // basarili donup koltugu satti.
                //
                // Bu kontrol olmasaydi Release() DomainException
                // firlatir ve TUM parti (batch) basarisiz olurdu --
                // tek bir kenar durum yuzunden 99 rezervasyon
                // temizlenmeden kalirdi.
                if (item.EventSeat.Status != Domain.Enums.EventSeatStatus.Sold)
                {
                    item.EventSeat.Release();
                }
            }
        }

        if (expired.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(expired.Count);
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Options;
using Ticketing.Application.Common.Results;
using Ticketing.Application.Features.Outbox;
using Ticketing.Domain.Entities;

namespace Ticketing.Application.Features.Reservations;

// İPTAL -- PDF: POST /api/v1/reservations/{id}/cancel

public sealed record CancelReservationCommand(Guid Id, string? Reason) : IRequest<Result>;

internal sealed class CancelReservationCommandHandler
    : IRequestHandler<CancelReservationCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly ISeatNotifier _seatNotifier;

    public CancelReservationCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        ISeatNotifier seatNotifier)
    {
        _context = context;
        _currentUser = currentUser;
        _seatNotifier = seatNotifier;
    }

    public async Task<Result> Handle(CancelReservationCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure(Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // Items ve icindeki EventSeat'leri yukluyorum: koltukları
        // SERBEST BIRAKACAGIZ ve bunun için takip edilmeleri gerekiyor.
        var reservation = await _context.Reservations
            .Include(r => r.Items)
                .ThenInclude(i => i.EventSeat)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            .ConfigureAwait(false);

        if (reservation is null)
        {
            return Result.Failure(ReservationErrors.NotFound);
        }

        // Sahiplik kontrolü.
        //
        // 403 değil 404 donuyorum: baskasinin rezervasyonunun VAR
        // olduğunu dogrulamamak için. 403 deseydim, saldirgan Id
        // tarayip hangi rezervasyonlarin var olduğunu ogrenebilirdi.
        if (reservation.UserId != userId)
        {
            return Result.Failure(ReservationErrors.NotFound);
        }

        // Durum gecisi entity'de dogrulaniyor: zaten iptal edilmiş
        // veya onaylanmis bir rezervasyon iptal edilemez.
        reservation.Cancel(request.Reason);

        // Koltuklari serbest birak
        //
        // Bu adim ATLANIRSA en kötü hata olusur: rezervasyon iptal
        // görünür ama koltuklar 10 dakika daha kilitli kalır.
        // Kullanıcı "iptal ettim ama koltuk hâlâ dolu" der ve
        // sebebini kimse anlayamaz.
        //
        // Release, satılmış koltuğu reddediyor -- iptal edilen bir
        // rezervasyonda satılmış koltuk olamaz ama savunmayi
        // entity'de tutuyorum.
        foreach (var item in reservation.Items)
        {
            item.EventSeat.Release();
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // PDF Sprint 10: "SeatReleased".
        // Vazgecen kullanıcının koltukları, oturumu izleyen herkeste
        // anında yesile dönüyor.
        await _seatNotifier.SeatsReleasedAsync(
            reservation.EventSessionId,
            reservation.Items.Select(i => i.EventSeatId).ToList(),
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

// SURE UZATMA -- PDF: POST /api/v1/reservations/{id}/extend

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
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
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
        //   - Yalnızca Locked durumunda uzatilabilir
        //   - Süresi DOLMUS rezervasyon uzatilamaz (diriltilemez)
        //   - Uzatma limiti asilamaz
        reservation.Extend(
            TimeSpan.FromMinutes(_options.MaxExtensionMinutes),
            _options.MaxExtensionCount,
            now);

        // Koltuklarin kilit suresini de uzat
        //
        // Bu adim atlanirsa TUTARSIZLIK olusur: rezervasyon 15 dakika
        // geçerli görünür ama koltuklar 10. dakikada "musait" olur ve
        // başkası alabilir.
        //
        // Rezervasyon ile koltuk sureleri HER ZAMAN aynı olmalı.
        // Ikisini ayrı yerlerde tuttugum için bu senkronizasyon
        // benim sorumlulugumda.
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

// SURESI DOLANLARI TEMIZLE -- background job cagiracak (Sprint 9)

/// <summary>
/// Süresi dolmuş rezervasyonları iptal eder ve koltukları serbest birakir.
///
/// PDF Sprint 7: "Süresi dolan rezervasyon otomatik olarak iptal
/// edilmelidir."
/// PDF Sprint 9: "Süresi dolan rezervasyonları iptal etme" job'i.
///
/// Şimdilik endpoint olarak da açık (admin tetikleyebilsin ve test
/// edebileyim). Sprint 9'da Hangfire ile dakikada bir calisacak.
/// </summary>
public sealed record ExpireReservationsCommand(int BatchSize = 100) : IRequest<Result<int>>;

internal sealed class ExpireReservationsCommandHandler
    : IRequestHandler<ExpireReservationsCommand, Result<int>>
{
    private readonly IApplicationDbContext _context;
    private readonly IDateTimeProvider _clock;
    private readonly ISeatNotifier _seatNotifier;

    public ExpireReservationsCommandHandler(
        IApplicationDbContext context,
        IDateTimeProvider clock,
        ISeatNotifier seatNotifier)
    {
        _context = context;
        _clock = clock;
        _seatNotifier = seatNotifier;
    }

    public async Task<Result<int>> Handle(
        ExpireReservationsCommand request,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // TOPLU ISLEM SINIRI (BatchSize)
        //
        // Sinir olmasaydı, sistem bir süre durup 50.000 süresi dolmuş
        // rezervasyon birikseydi, job hepsini tek transaction'da
        // islemeye çalışır ve dakikalarca tablo kilitlerdi.
        //
        // Parca parca islemek, job'in her calismasinda kisa surmesini
        // ve digerlerinin bir sonraki calismada islenmesini saglar.
        //
        // ix_reservations_status_expires index'i bu sorguyu karsiliyor.
        var expired = await _context.Reservations
            .Include(r => r.Items)
                .ThenInclude(i => i.EventSeat)

            // Etkinlik başlığı, Outbox mesajinin içeriği için gerekli.
            // Include etmeseydim her rezervasyon için ayrı bir sorgu
            // atilirdi (N+1) -- 100 rezervasyonluk bir partide 100
            // fazladan gidis donus.
            .Include(r => r.EventSession)
                .ThenInclude(s => s.Event)
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
                // Satılmış koltuğu atla.
                //
                // Nasil olur? Ödeme tam bu sırada tamamlanmis olabilir:
                // rezervasyon PaymentPending, süre doldu, ama ödeme
                // başarılı donup koltuğu satti.
                //
                // Bu kontrol olmasaydı Release() DomainException
                // firlatir ve TÜM parti (batch) başarısız olurdu --
                // tek bir kenar durum yuzunden 99 rezervasyon
                // temizlenmeden kalırdı.
                if (item.EventSeat.Status != Domain.Enums.EventSeatStatus.Sold)
                {
                    item.EventSeat.Release();
                }
            }

            // Bildirimi burada gondermiyoruz -- outbox'a yaziyorum
            //
            // PDF Sprint 9: "Rezervasyon süresi doldu bildirimi"
            // Outbox senaryolari arasında.
            //
            // Neden doğrudan e-posta gondermiyorum? Çünkü bu bir
            // DONGU içinde: 100 rezervasyonluk bir partide 100
            // e-posta gonderimi demek. SMTP sunucusu yavassa (her
            // biri 2 saniye) job 3 dakika surer, bu sırada bir
            // sonraki calisma baslayamaz ve süresi dolan yeni
            // rezervasyonlar temizlenmeden bekler.
            //
            // Yani KOLTUKLAR BOSA BEKLER -- doğrudan gelir kaybi.
            //
            // Outbox'a yazmak ise sadece bir INSERT: mikrosaniyeler.
            // Gonderim isi ayrı bir job'a devrediliyor.
            // PDF: "Job islemleri kullanıcı istegini gereksiz yere
            // bekletmemelidir."
            _context.OutboxMessages.Add(OutboxMessage.Create(
                OutboxMessageTypes.ReservationExpired,
                JsonSerializer.Serialize(new ReservationExpiredPayload(
                    reservation.Id,
                    reservation.UserId,
                    reservation.ReservationCode,
                    reservation.EventSession.Event.Title,
                    reservation.Items.Count))));
        }

        if (expired.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // PDF is kuralı: "Rezervasyon süresi doldugunda koltuk
            // serbest gorunmelidir."
            //
            // Bu, Sprint 10'un en görünür faydasi. SignalR olmasaydı
            // kullanıcının bosalan koltuğu gormesi için sayfayı
            // yenilemesi gerekirdi -- ya da Sprint 7'de koydugum
            // 10 saniyelik yoklamayi beklemesi.
            //
            // IKI AYRI OLAY gonderiyorum:
            //   SeatReleased      -> oturumu izleyen HERKESE
            //   ReservationExpired -> "senin rezervasyonun bitti"
            //
            // Tek olayda birlestirseydim, rezervasyon sahibi kendi
            // rezervasyonunun mu yoksa baskasininkinin mi bittigini
            // ayırt edemezdi.
            foreach (var reservation in expired)
            {
                await _seatNotifier.SeatsReleasedAsync(
                    reservation.EventSessionId,
                    reservation.Items.Select(i => i.EventSeatId).ToList(),
                    cancellationToken).ConfigureAwait(false);

                await _seatNotifier.ReservationExpiredAsync(
                    reservation.EventSessionId,
                    reservation.Id,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(expired.Count);
    }
}

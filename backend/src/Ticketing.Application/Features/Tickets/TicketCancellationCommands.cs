using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions.Payments;
using Ticketing.Application.Abstractions.Persistence;
using Ticketing.Application.Abstractions.RealTime;
using Ticketing.Application.Abstractions.Security;
using Ticketing.Application.Abstractions.Time;
using Ticketing.Application.Common.Logging;
using Ticketing.Application.Common.Results;
using Ticketing.Domain.Entities;
using Ticketing.Domain.Enums;
using Ticketing.Domain.ValueObjects;

namespace Ticketing.Application.Features.Tickets;

internal static class TicketErrors
{
    // Bulunamadi ve "senin degil" AYNI hatayi donuyor.
    //
    // Baskasinin bilet kimligini deneyen biri, 403 alirsa o biletin
    // VAR oldugunu ogrenir; 404 alirsa hicbir sey ogrenmez. Aradaki
    // fark kucuk gorunuyor ama numaralandirma saldirisinin tamami bu
    // farka dayaniyor.
    public static readonly Error NotFound = Error.NotFound(
        "ticket.not_found", "Bilet bulunamadı.");

    public static readonly Error NotActive = Error.Conflict(
        "ticket.not_active", "Yalnızca geçerli biletler iptal edilebilir.");

    public static readonly Error EventStarted = Error.Conflict(
        "ticket.event_started", "Etkinlik başladıktan sonra bilet iptal edilemez.");

    public static readonly Error RefundRejected = Error.Conflict(
        "ticket.refund_rejected", "İade sağlayıcı tarafından reddedildi.");
}

/// <summary>
/// İptal edilirse ne kadar iade alacagini onceden soyler.
/// </summary>
/// <remarks>
/// Neden ayri bir sorgu?
///
/// Kullanici "İptal et" dedigi anda ne kaybedecegini bilmeli. Iade
/// yuzdesi etkinlige kalan sureye gore degisiyor (7 gunden fazla
/// %100, 48 saatten az %0) ve bu hesabi istemcide TEKRARLAMAK
/// istemedim: politika etkinlik basina saklaniyor ve organizator
/// degistirebiliyor. Istemcide kopyalasaydim, politika degistiginde
/// kullaniciya yanlis rakam gosterirdik.
///
/// Hesabi tek yerde -- CancellationPolicy'de -- tutup sonucu
/// soruyorum.
/// </remarks>
public sealed record GetTicketCancellationPreviewQuery(Guid TicketId)
    : IRequest<Result<TicketCancellationPreview>>;

public sealed record TicketCancellationPreview(
    Guid TicketId,
    string TicketNumber,
    string EventTitle,
    DateTimeOffset SessionStartDate,
    decimal Price,
    string Currency,
    int RefundPercentage,
    decimal RefundAmount,

    // Iptal edilebilir mi? Edilemiyorsa sebebi Reason'da.
    //
    // Sebebi ISTEMCIYE metin olarak gonderiyorum, kod olarak degil.
    // Burada dogru tercih bu: istemcinin yapacagi tek sey metni
    // gostermek. Kod gonderseydim, istemcide bu kodlari Turkce
    // metinlere ceviren ikinci bir tablo tutmam gerekirdi ve iki
    // taraf zamanla ayrisirdi.
    bool CanCancel,
    string? Reason);

internal sealed class GetTicketCancellationPreviewQueryHandler
    : IRequestHandler<GetTicketCancellationPreviewQuery, Result<TicketCancellationPreview>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetTicketCancellationPreviewQueryHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<TicketCancellationPreview>> Handle(
        GetTicketCancellationPreviewQuery request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<TicketCancellationPreview>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // Politikayi ve seans tarihini tek sorguda cekiyorum.
        // AsNoTracking: bu bir okuma, hicbir sey degismeyecek.
        var veri = await _context.Tickets
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && t.UserId == userId)
            .Select(t => new
            {
                t.Id,
                t.TicketNumber,
                t.Status,
                Fiyat = t.Price.Amount,
                ParaBirimi = t.Price.Currency,
                Baslik = t.EventSeat.EventSession.Event.Title,
                Baslangic = t.EventSeat.EventSession.StartDate,
                Politika = t.EventSeat.EventSession.Event.CancellationPolicy,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (veri is null)
        {
            return Result.Failure<TicketCancellationPreview>(TicketErrors.NotFound);
        }

        var simdi = _clock.UtcNow;
        var yuzde = veri.Politika.CalculateRefundPercentage(veri.Baslangic, simdi);
        var tutar = IadeTutari(veri.Fiyat, yuzde);

        var (edilebilir, sebep) = veri.Status switch
        {
            TicketStatus.Active when veri.Baslangic <= simdi => (false, "Etkinlik başladı."),
            TicketStatus.Active => (true, (string?)null),
            TicketStatus.Used => (false, "Bilet kullanıldı."),
            TicketStatus.Expired => (false, "Etkinlik geçti."),
            _ => (false, "Bilet zaten iptal edilmiş."),
        };

        return Result.Success(new TicketCancellationPreview(
            veri.Id,
            veri.TicketNumber,
            veri.Baslik,
            veri.Baslangic,
            veri.Fiyat,
            veri.ParaBirimi,
            yuzde,
            tutar,
            edilebilir,
            sebep));
    }

    /// <summary>
    /// Iade tutarini kurusa yuvarlar.
    /// </summary>
    /// <remarks>
    /// MidpointRounding.ToEven (bankaci yuvarlamasi) DEGIL: para
    /// hesabinda musterinin lehine yuvarlamak, sonra "neden 1 kurus
    /// eksik geldi" sorusunu cevaplamaktan ucuz. AwayFromZero da
    /// gunluk hayatta beklenen davranis.
    /// </remarks>
    internal static decimal IadeTutari(decimal fiyat, int yuzde)
        => Math.Round(fiyat * yuzde / 100m, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Kullanici kendi biletini iptal eder.
/// PDF sayfa 4: "Kullanici: Biletini iptal edebilir."
/// </summary>
/// <remarks>
/// Bu ucu yazana kadar kullanicinin TEK biletini iptal etmesinin bir
/// yolu yoktu. Var olan iade ucu (POST /payments/{id}/refund) bir
/// ODEMEYI iade ediyor ve tam iade halinde o rezervasyondaki BUTUN
/// biletleri iptal ediyor. Dort kisilik bir rezervasyonda tek kisi
/// gelemeyecekse, kullanicinin yapabilecegi hicbir sey yoktu.
///
/// Ustelik o uc iade tutarini CAGIRANDAN aliyor. Yani iade
/// politikasi -- PDF Sprint 1 soru 10'un karsiligi olan
/// CancellationPolicy -- hicbir yerden cagrilmiyordu:
/// CalculateRefundPercentage yazilmis, testi bile yazilmamisti.
/// Bu komut onu ilk kez gercekten kullaniyor.
/// </remarks>
public sealed record CancelMyTicketCommand(Guid TicketId) : IRequest<Result<TicketCancellationResult>>;

public sealed record TicketCancellationResult(
    Guid TicketId,
    TicketStatus Status,
    int RefundPercentage,
    decimal RefundAmount,
    string Currency);

internal sealed partial class CancelMyTicketCommandHandler
    : IRequestHandler<CancelMyTicketCommand, Result<TicketCancellationResult>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IPaymentService _paymentService;
    private readonly ISeatNotifier _seatNotifier;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<CancelMyTicketCommandHandler> _logger;

    [LoggerMessage(
        EventId = LogEvents.BiletIptalEdildi,
        Level = LogLevel.Warning,
        Message = "Bilet iptal edildi. Bilet: {BiletNo}, Iade: %{Yuzde} = {Tutar} {ParaBirimi}")]
    private static partial void LogTicketCancelled(
        ILogger logger, string biletNo, int yuzde, decimal tutar, string paraBirimi);

    public CancelMyTicketCommandHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IPaymentService paymentService,
        ISeatNotifier seatNotifier,
        IDateTimeProvider clock,
        ILogger<CancelMyTicketCommandHandler> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _paymentService = paymentService;
        _seatNotifier = seatNotifier;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<TicketCancellationResult>> Handle(
        CancelMyTicketCommand request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            return Result.Failure<TicketCancellationResult>(
                Error.Unauthorized("auth.required", "Giriş yapmalisiniz."));
        }

        // Include listesi uzun ama hepsi gerekli; her birinin sebebi:
        //   QrCode         -> iptal edilen biletin karekodu iptal edilmeli
        //   EventSeat      -> koltuk tekrar satisa acilacak
        //   EventSession   -> iade yuzdesi seans baslangicina gore
        //   Event          -> CancellationPolicy orada
        //   ReservationItem-> odemeyi bulmak icin ReservationId lazim
        //
        // Sondaki ReservationItem'i ILK YAZISTA UNUTTUM ve tam da
        // ustune not dustugum tuzaga dustum: asagida
        // bilet.ReservationItem.ReservationId'yi bir LINQ ifadesi
        // icinde kullaniyorum, nesne null geliyor ve EF sorguyu
        // derlerken NullReferenceException firlatiyor. Tarayicida
        // "Beklenmeyen bir hata" olarak gorundu, 500 dondu.
        //
        // Sprint 17'de idempotency kontrolunun basina gelenle ayni
        // desen (bkz. PaymentQueries.cs) -- ama bu kez daha sansliyim:
        // orada eksik Include kodu SESSIZCE yanlis calistiriyordu,
        // burada gurultuyle patladi ve hemen fark ettim.
        var bilet = await _context.Tickets
            .Include(t => t.QrCode)
            .Include(t => t.ReservationItem)
            .Include(t => t.EventSeat)
                .ThenInclude(s => s.EventSession)
                    .ThenInclude(s => s.Event)
            .FirstOrDefaultAsync(
                t => t.Id == request.TicketId && t.UserId == userId,
                cancellationToken)
            .ConfigureAwait(false);

        if (bilet is null)
        {
            return Result.Failure<TicketCancellationResult>(TicketErrors.NotFound);
        }

        if (bilet.Status != TicketStatus.Active)
        {
            return Result.Failure<TicketCancellationResult>(TicketErrors.NotActive);
        }

        var seans = bilet.EventSeat.EventSession;
        var simdi = _clock.UtcNow;

        // Etkinlik basladiysa iptali kabul etmiyorum.
        //
        // Domain buna izin verirdi (Ticket.Cancel yalnizca kullanilmis
        // bileti reddediyor) ve iade zaten %0 olurdu. Yine de
        // engelliyorum: kullaniciya "iptal edildi" deyip hicbir sey
        // iade etmemek, "iptal edilemez" demekten daha kotu bir
        // deneyim. Ustelik koltugu bosaltmak da anlamsiz -- etkinlik
        // suruyor, o koltuga kimse oturmayacak.
        if (seans.StartDate <= simdi)
        {
            return Result.Failure<TicketCancellationResult>(TicketErrors.EventStarted);
        }

        var yuzde = seans.Event.CancellationPolicy.CalculateRefundPercentage(seans.StartDate, simdi);
        var tutar = GetTicketCancellationPreviewQueryHandler.IadeTutari(bilet.Price.Amount, yuzde);

        // ---- Para iadesi (varsa) ----
        //
        // Once saglayici, sonra veritabani. RefundPaymentCommand'da
        // ayni sirayi kullandim ve gerekcesi orada yazili: once
        // veritabanina yazip saglayici reddederse, kullanici parasini
        // almadan sistemde "iade edildi" gorunur.
        Payment? odeme = null;

        if (tutar > 0)
        {
            odeme = await _context.Payments
                .Include(p => p.Reservation)
                .Where(p => p.ReservationId == bilet.ReservationItem.ReservationId
                         && (p.Status == PaymentStatus.Successful || p.Status == PaymentStatus.Refunded))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (odeme is not null)
            {
                // Kismi iade odemenin kalan iade edilebilir tutarini
                // asamaz. Asiyorsa kalani iade ediyorum: bu, ayni
                // rezervasyondaki baska biletlerin daha once iade
                // edilmis olmasi halinde olusabilir.
                var kalan = odeme.GetRefundableAmount().Amount;
                tutar = Math.Min(tutar, kalan);
            }

            if (odeme is not null && tutar > 0)
            {
                var saglayici = await _paymentService
                    .RefundPaymentAsync(odeme.ProviderReference ?? string.Empty, tutar, cancellationToken)
                    .ConfigureAwait(false);

                if (!saglayici.IsSuccess)
                {
                    return Result.Failure<TicketCancellationResult>(TicketErrors.RefundRejected);
                }

                odeme.Refund(new Money(tutar, odeme.Amount.Currency), saglayici.ProviderReference);
            }
            else
            {
                // Odeme kaydi yoksa (veya iade edilecek bakiye
                // kalmadiysa) bileti yine de iptal ediyorum, ama
                // "iade edildi" demiyorum: tutari sifirliyorum.
                tutar = 0;
            }
        }

        // ---- Bilet, karekod, koltuk ----

        bilet.Cancel(withRefund: tutar > 0, simdi);

        // Karekodu iptal ediyorum.
        //
        // Bu satir olmasaydi iptal edilen biletin QR'i kapida hala
        // okunurdu: MarkAsUsed durumu Active olmadigi icin reddeder,
        // ama kullanici kapida rezil olurdu ve gorevlinin elinde
        // "neden gecmedi" sorusuna cevap olmazdi. Revoke, reddi
        // KAREKOD seviyesinde ve acik sebeple veriyor.
        bilet.QrCode?.Revoke();

        // Koltugu tekrar satisa aciyorum.
        //
        // Atlanirsa koltuk kalici olarak "satilmis" kalir ve bir daha
        // kimseye satilamaz -- dogrudan gelir kaybi. Iade akisinda da
        // ayni notu dusmustum.
        if (bilet.EventSeat.Status == EventSeatStatus.Sold)
        {
            bilet.EventSeat.Refund();
        }

        _context.Notifications.Add(Notification.Create(
            userId,
            NotificationType.TicketCancelled,
            "Biletiniz iptal edildi",
            tutar > 0
                ? $"{bilet.TicketNumber} numarali biletiniz iptal edildi. " +
                  $"%{yuzde} iade: {tutar} {bilet.Price.Currency}."
                : $"{bilet.TicketNumber} numarali biletiniz iptal edildi. " +
                  "Etkinlige kalan sure nedeniyle iade yapilamadi.",
            bilet.Id));

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Koltuk haritasina bakanlara haber ver: koltuk bosaldi.
        //
        // Kaydetmeden ONCE gonderseydim ve kayit basarisiz olsaydi,
        // ekranindaki koltugu bos gorup secmeye calisan kullanici
        // sunucudan "dolu" cevabi alirdi ve sebebini anlamazdi.
        await _seatNotifier
            .SeatsReleasedAsync(seans.Id, [bilet.EventSeatId], cancellationToken)
            .ConfigureAwait(false);

        LogTicketCancelled(_logger, bilet.TicketNumber, yuzde, tutar, bilet.Price.Currency);

        return Result.Success(new TicketCancellationResult(
            bilet.Id,
            bilet.Status,
            yuzde,
            tutar,
            bilet.Price.Currency));
    }
}

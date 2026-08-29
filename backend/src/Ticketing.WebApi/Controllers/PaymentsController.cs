using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Payments;
using Ticketing.Domain.Enums;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Ödeme islemleri. PDF Sprint 8.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
// PDF Sprint 15: "Ödeme endpointi" hiz sınırı.
// Sinif duzeyinde -- ödeme ile ilgili her uc korunuyor.
[EnableRateLimiting(RateLimitingSetup.Policies.Transaction)]
public sealed class PaymentsController : ApiControllerBase
{
    /// <summary>
    /// Rezervasyon için ödeme baslatir.
    /// </summary>
    /// <remarks>
    /// TUTAR GONDERILMEZ -- rezervasyondan okunur.
    /// PDF Sprint 6: "Frontend tarafından gonderilen toplam tutara
    /// güvenilmemelidir."
    ///
    /// Idempotency-Key header'i ile cift ödeme engellenir.
    /// </remarks>
    /// <response code="201">Ödeme baslatildi.</response>
    /// <response code="422">Rezervasyon odenebilir durumda değil veya zaten odenmis.</response>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = new CreatePaymentCommand(request.ReservationId, idempotencyKey);
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(
            result,
            $"/api/v1/payments/{(result.IsSuccess ? result.Value.Id : Guid.Empty)}");
    }

    /// <summary>Ödeme detayı. Sahibi veya admin görebilir.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetPaymentQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Ödemeyi tamamlar: rezervasyonu onaylar, biletleri ve QR kodlarini
    /// üretir, koltukları satıldı olarak isaretler.
    /// </summary>
    /// <remarks>
    /// BU ENDPOINT BIR ÖDEME CALLBACK'IDIR
    ///
    /// Gerçek entegrasyonda ödeme sağlayıcısı burayi cagirir.
    ///
    /// GÜVENLİK: Callback'e KORU KORUNE GUVENILMEZ. Handler, islemi
    /// saglayiciya SORARAK dogruluyor (VerifyPaymentAsync). Doğrulama
    /// olmasaydı saldirgan bu adrese istek gonderip bedava bilet
    /// alabilirdi.
    ///
    /// IDEMPOTENT: Saglayicilar callback'i birden fazla kez gönderir.
    /// Ikinci cagride yeni bilet URETILMEZ, mevcut ödeme döner.
    ///
    /// NOT (Sprint 15): Gerçek saglayicilar callback'i imzalar ve biz
    /// imzayi dogrularız. Simulasyonda imza yok; bu yüzden endpoint
    /// şimdilik [Authorize] ile korunuyor. Gerçek entegrasyonda
    /// [AllowAnonymous] + imza doğrulama olacak.
    /// </remarks>
    [HttpPost("{id:guid}/complete")]
    [Authorize]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody] CompletePaymentRequest? request,
        CancellationToken cancellationToken)
        // PDF Sprint 15 idempotency listesi: "Ödeme callback"
        //
        // Bu uc için AYRI bir Idempotency-Key GEREKMIYOR ve bilinçli
        // olarak eklemedim.
        //
        // Sebep: idempotency zaten SAGLANIYOR ama farklı bir yoldan.
        // Payment.Complete(), ödeme zaten Successful ise false dönüyor
        // ve handler bilet URETMIYOR. Yani aynı callback yuz kez gelse
        // de sonuç aynı: iki bilet, iki QR.
        //
        // Anahtar bazlı idempotency burada YANLIS olurdu: anahtari
        // SAGLAYICI uretecekti ve saglayicilar her denemede aynı
        // anahtari gonderecegini GARANTI ETMIYOR. Anahtar degisirse
        // "yeni istek" sanip ikinci kez bilet uretirdik.
        //
        // Odemenin KENDİ DURUMU en guvenilir idempotency anahtaridir.
        // Sprint 8'de bunu ucten uca dogrulamistim: callback 3 kez
        // cagrildi, bilet sayısı 2'de kaldı.
        => HandleResult(await Sender
            .Send(new CompletePaymentCommand(id, request?.ProviderReference), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Ödemeyi başarısız olarak isaretler.
    /// Rezervasyon iptal edilir ve koltuklar serbest bırakılır.
    /// </summary>
    [HttpPost("{id:guid}/fail")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Fail(
        Guid id,
        [FromBody] FailPaymentRequest? request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new FailPaymentCommand(id, request?.Reason), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// İade yapar. Tutar belirtilmezse kalan tüm tutar iade edilir.
    /// Tam iadede biletler iptal edilir ve koltuklar tekrar satışa çıkar.
    /// </summary>
    /// <remarks>
    /// YALNIZCA ADMIN. Kullanıcının kendi kendine iade baslatmasi,
    /// iade politikasini (CancellationPolicy) atlatmasi anlamina
    /// gelirdi. Kullanıcı tarafli iade akışı Sprint 12'de bilet
    /// iptali üzerinden gelecek ve politikayi uygulayacak.
    /// </remarks>
    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Refund(
        Guid id,
        [FromBody] RefundPaymentRequest? request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
        // PDF Sprint 15 idempotency listesi: "İade baslatma"
        //
        // İade, cift calistirilmasi EN TEHLIKELI işlem: aynı parayi iki
        // kez geri gondermek doğrudan mali kayip.
        //
        // Domain katmani zaten koruyor: Payment.Refund(), toplam iadenin
        // odenen tutarı asmasini reddediyor (payment.refund_exceeds_amount).
        // Yani ikinci tam iade denemesi HATA veriyor.
        //
        // Ama bu, ag kopmasi yuzunden TEKRARLANAN bir isteği de hata
        // yapiyor -- oysa admin tek bir iade yapmak istemisti ve
        // istegin ulasip ulasmadigini bilmiyor.
        //
        // Idempotency anahtari bu ikisini AYIRIYOR:
        //   aynı anahtar  -> "bu isteği zaten isledim", basari döner
        //   farklı anahtar -> gerçekten ikinci iade, kurallar isler
        => HandleResult(await Sender
            .Send(
                new RefundPaymentCommand(id, request?.Amount, request?.Reason, idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Kullanıcının biletleri. PDF sayfa 4.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users/me")]
public sealed class MyTicketsController : ApiControllerBase
{
    /// <summary>
    /// Kullanıcının biletlerini döndürür.
    /// QR değeri YALNIZCA aktif biletlerde döner.
    /// </summary>
    [HttpGet("tickets")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<TicketDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] TicketStatus? status,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetMyTicketsQuery(status), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record CreatePaymentRequest(Guid ReservationId);

public sealed record CompletePaymentRequest(string? ProviderReference);

public sealed record FailPaymentRequest(string? Reason);

public sealed record RefundPaymentRequest(decimal? Amount, string? Reason);

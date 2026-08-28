using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Payments;
using Ticketing.Domain.Enums;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Odeme islemleri. PDF Sprint 8.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments")]
// PDF Sprint 15: "Odeme endpointi" hiz siniri.
// Sinif duzeyinde -- odeme ile ilgili her uc korunuyor.
[EnableRateLimiting(RateLimitingSetup.Policies.Transaction)]
public sealed class PaymentsController : ApiControllerBase
{
    /// <summary>
    /// Rezervasyon icin odeme baslatir.
    /// </summary>
    /// <remarks>
    /// TUTAR GONDERILMEZ -- rezervasyondan okunur.
    /// PDF Sprint 6: "Frontend tarafindan gonderilen toplam tutara
    /// guvenilmemelidir."
    ///
    /// Idempotency-Key header'i ile cift odeme engellenir.
    /// </remarks>
    /// <response code="201">Odeme baslatildi.</response>
    /// <response code="422">Rezervasyon odenebilir durumda degil veya zaten odenmis.</response>
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

    /// <summary>Odeme detayi. Sahibi veya admin gorebilir.</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetPaymentQuery(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Odemeyi tamamlar: rezervasyonu onaylar, biletleri ve QR kodlarini
    /// uretir, koltuklari satildi olarak isaretler.
    /// </summary>
    /// <remarks>
    /// ==================================================================
    /// BU ENDPOINT BIR ODEME CALLBACK'IDIR
    /// ==================================================================
    /// Gercek entegrasyonda odeme saglayicisi burayi cagirir.
    ///
    /// GUVENLIK: Callback'e KORU KORUNE GUVENILMEZ. Handler, islemi
    /// saglayiciya SORARAK dogruluyor (VerifyPaymentAsync). Dogrulama
    /// olmasaydi saldirgan bu adrese istek gonderip bedava bilet
    /// alabilirdi.
    ///
    /// IDEMPOTENT: Saglayicilar callback'i birden fazla kez gonderir.
    /// Ikinci cagride yeni bilet URETILMEZ, mevcut odeme doner.
    ///
    /// NOT (Sprint 15): Gercek saglayicilar callback'i imzalar ve biz
    /// imzayi dogrularız. Simulasyonda imza yok; bu yuzden endpoint
    /// simdilik [Authorize] ile korunuyor. Gercek entegrasyonda
    /// [AllowAnonymous] + imza dogrulama olacak.
    /// </remarks>
    [HttpPost("{id:guid}/complete")]
    [Authorize]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Complete(
        Guid id,
        [FromBody] CompletePaymentRequest? request,
        CancellationToken cancellationToken)
        // ==============================================================
        // PDF Sprint 15 idempotency listesi: "Odeme callback"
        // ==============================================================
        // Bu uc icin AYRI bir Idempotency-Key GEREKMIYOR ve bilincli
        // olarak eklemedim.
        //
        // Sebep: idempotency zaten SAGLANIYOR ama farkli bir yoldan.
        // Payment.Complete(), odeme zaten Successful ise false donuyor
        // ve handler bilet URETMIYOR. Yani ayni callback yuz kez gelse
        // de sonuc ayni: iki bilet, iki QR.
        //
        // Anahtar bazli idempotency burada YANLIS olurdu: anahtari
        // SAGLAYICI uretecekti ve saglayicilar her denemede ayni
        // anahtari gonderecegini GARANTI ETMIYOR. Anahtar degisirse
        // "yeni istek" sanip ikinci kez bilet uretirdik.
        //
        // Odemenin KENDI DURUMU en guvenilir idempotency anahtaridir.
        // Sprint 8'de bunu ucten uca dogrulamistim: callback 3 kez
        // cagrildi, bilet sayisi 2'de kaldi.
        // ==============================================================
        => HandleResult(await Sender
            .Send(new CompletePaymentCommand(id, request?.ProviderReference), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Odemeyi basarisiz olarak isaretler.
    /// Rezervasyon iptal edilir ve koltuklar serbest birakilir.
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
    /// Iade yapar. Tutar belirtilmezse kalan tum tutar iade edilir.
    /// Tam iadede biletler iptal edilir ve koltuklar tekrar satisa cikar.
    /// </summary>
    /// <remarks>
    /// YALNIZCA ADMIN. Kullanicinin kendi kendine iade baslatmasi,
    /// iade politikasini (CancellationPolicy) atlatmasi anlamina
    /// gelirdi. Kullanici tarafli iade akisi Sprint 12'de bilet
    /// iptali uzerinden gelecek ve politikayi uygulayacak.
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
        // ==============================================================
        // PDF Sprint 15 idempotency listesi: "Iade baslatma"
        // ==============================================================
        // Iade, cift calistirilmasi EN TEHLIKELI islem: ayni parayi iki
        // kez geri gondermek dogrudan mali kayip.
        //
        // Domain katmani zaten koruyor: Payment.Refund(), toplam iadenin
        // odenen tutari asmasini reddediyor (payment.refund_exceeds_amount).
        // Yani ikinci tam iade denemesi HATA veriyor.
        //
        // Ama bu, ag kopmasi yuzunden TEKRARLANAN bir istegi de hata
        // yapiyor -- oysa admin tek bir iade yapmak istemisti ve
        // istegin ulasip ulasmadigini bilmiyor.
        //
        // Idempotency anahtari bu ikisini AYIRIYOR:
        //   ayni anahtar  -> "bu istegi zaten isledim", basari doner
        //   farkli anahtar -> gercekten ikinci iade, kurallar isler
        // ==============================================================
        => HandleResult(await Sender
            .Send(
                new RefundPaymentCommand(id, request?.Amount, request?.Reason, idempotencyKey),
                cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>Kullanicinin biletleri. PDF sayfa 4.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users/me")]
public sealed class MyTicketsController : ApiControllerBase
{
    /// <summary>
    /// Kullanicinin biletlerini dondurur.
    /// QR degeri YALNIZCA aktif biletlerde doner.
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

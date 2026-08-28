using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Abstractions.Reporting;
using Ticketing.Application.Features.Reports;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Raporlama uclari. PDF Sprint 13.
/// </summary>
/// <remarks>
/// ==================================================================
/// YETKI: [Authorize] YETERLI, ROL KONTROLU HANDLER'DA
/// ==================================================================
/// Ilk aklima gelen [Authorize(Policy = OrganizerOnly)] koymakti.
/// Yapmadim, cunku raporlari IKI FARKLI ROL kullaniyor:
///
///   ADMIN       -> tum sistemin verisi
///   ORGANIZATOR -> yalnizca kendi etkinlikleri
///
/// Policy ile ikisini birden ifade etmek ("admin VEYA organizator")
/// mumkun ama asil is kapsamin BELIRLENMESI ve o zaten handler'da
/// yapiliyor (ReportScopeResolver).
///
/// Iki yerde rol kontrolu yapsaydik, birini guncelleyip digerini
/// unutmak riski dogardi. Tek yerde tutuyorum: uc "giris yapmis
/// olmali" der, handler "ne gorebilirsin" der.
///
/// Sonuc: normal bir kullanici bu uclara 403 aliyor
/// (report.forbidden).
/// ==================================================================
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Authorize]
public sealed class ReportsController : ApiControllerBase
{
    /// <summary>PDF: GET /api/v1/reports/sales-summary</summary>
    [HttpGet("sales-summary")]
    [ProducesResponseType<SalesSummaryReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SalesSummary(
        [FromQuery] GetSalesSummaryReportQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF: GET /api/v1/reports/event-occupancy</summary>
    [HttpGet("event-occupancy")]
    [ProducesResponseType<IReadOnlyList<EventOccupancyRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EventOccupancy(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetEventOccupancyReportQuery(), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>PDF: GET /api/v1/reports/revenue-by-event</summary>
    [HttpGet("revenue-by-event")]
    [ProducesResponseType<IReadOnlyList<EventRevenue>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevenueByEvent(
        [FromQuery] GetRevenueByEventReportQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF: GET /api/v1/reports/ticket-type-sales</summary>
    [HttpGet("ticket-type-sales")]
    [ProducesResponseType<IReadOnlyList<TicketTypeSalesRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TicketTypeSales(
        [FromQuery] GetTicketTypeSalesReportQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>PDF: GET /api/v1/reports/payment-statuses</summary>
    [HttpGet("payment-statuses")]
    [ProducesResponseType<IReadOnlyList<PaymentStatusRow>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PaymentStatuses(
        [FromQuery] GetPaymentStatusReportQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Rapor disa aktarimi TALEP EDER. PDF: POST /api/v1/reports/export
    /// </summary>
    /// <remarks>
    /// PDF: "Rapor uretimi background job olarak calistirilmali ve
    /// tamamlandiginda kullaniciya bildirim gonderilmelidir."
    ///
    /// Bu uc dosyayi DONDURMEZ -- talebi kuyruga alir ve 202 doner.
    /// Rapor hazir olunca kullaniciya bildirim gidiyor ve dosya
    /// GET /reports/exports/{id} adresinden indiriliyor.
    ///
    /// 202 Accepted, "kabul ettim ama henuz tamamlamadim" demenin
    /// standart yolu. 200 donseydik istemci isin bittigini sanardi.
    /// </remarks>
    [HttpPost("export")]
    [ProducesResponseType<Guid>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromBody] ExportReportCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        return Accepted(
            $"/api/v1/reports/exports/{result.Value}",
            new { exportId = result.Value });
    }

    /// <summary>Uretilmis rapor dosyasini indirir.</summary>
    /// <remarks>
    /// Rapor hazir degilse 404 doner. Kullanici bildirimi ALDIKTAN
    /// sonra buraya geliyor, yani normal akista 404 gorulmez.
    /// </remarks>
    [HttpGet("exports/{exportId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        Guid exportId,
        [FromServices] IReportFileStore store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        // ==============================================================
        // SAHIPLIK KONTROLU: BILDIRIM UZERINDEN
        // ==============================================================
        // exportId bir Guid v7 -- tahmin edilmesi pratikte imkansiz.
        // Ama "tahmin edilemez kimlik" tek basina yetki DEGILDIR
        // (guvenlik literaturunde "security through obscurity").
        //
        // Kimligi bir yerden ogrenen biri (log, tarayici gecmisi,
        // paylasilan ekran goruntusu) baskasinin gelir raporunu
        // indirebilirdi.
        //
        // Bu yuzden bildirim tablosuna bakiyoruz: rapor hazir
        // oldugunda SAHIBINE bir bildirim yaziliyor ve o bildirimin
        // RelatedEntityId'si exportId. Yani "bu raporun bildirimi bu
        // kullaniciya mi yazilmis?" sorusu, sahiplik sorusunun ta
        // kendisi.
        // ==============================================================
        var sahiplikResult = await Sender
            .Send(new VerifyReportOwnershipQuery(exportId), cancellationToken)
            .ConfigureAwait(false);

        if (!sahiplikResult.IsSuccess || !sahiplikResult.Value)
        {
            // 403 degil 404: raporun VARLIGINI dogrulamiyoruz.
            return NotFound();
        }

        var file = await store.GetAsync(exportId, cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return NotFound();
        }

        return File(file.Content, file.ContentType, file.FileName);
    }
}

/// <summary>Organizator ve admin panelleri. PDF Sprint 13.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>Organizator paneli: PDF'in saydigi 10 metrik.</summary>
    [HttpGet("organizer")]
    [ProducesResponseType<OrganizerDashboard>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Organizer(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
        => HandleResult(await Sender
            .Send(new GetOrganizerDashboardQuery(days), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Admin paneli: PDF'in saydigi 10 metrik.
    /// </summary>
    /// <remarks>
    /// Burada policy KULLANIYORUM (raporlardan farkli olarak).
    ///
    /// Sebep: bu panelin kapsami yok -- ya TUM sistemi gorursun ya da
    /// hicbir seyi. "Kismi admin" diye bir sey olmadigi icin kontrolu
    /// en dista yapmak dogru ve handler'i sadelestiriyor.
    /// </remarks>
    [HttpGet("admin")]
    [Authorize(Policy = Security.AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<AdminDashboard>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Admin(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetAdminDashboardQuery(), cancellationToken)
            .ConfigureAwait(false));
}

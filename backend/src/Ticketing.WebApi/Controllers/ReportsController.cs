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
/// YETKI: [Authorize] YETERLI, ROL KONTROLU HANDLER'DA
///
/// İlk aklima gelen [Authorize(Policy = OrganizerOnly)] koymakti.
/// Yapmadim, çünkü raporlari IKI FARKLI ROL kullaniyor:
///
///   ADMIN       -> tüm sistemin verisi
///   ORGANİZATÖR -> yalnızca kendi etkinlikleri
///
/// Policy ile ikisini birden ifade etmek ("admin VEYA organizatör")
/// mumkun ama asil is kapsamin BELIRLENMESI ve o zaten handler'da
/// yapiliyor (ReportScopeResolver).
///
/// Iki yerde rol kontrolü yapsaydim, birini guncelleyip digerini
/// unutmak riski dogardi. Tek yerde tutuyorum: uc "giriş yapmış
/// olmalı" der, handler "ne gorebilirsin" der.
///
/// Sonuç: normal bir kullanıcı bu uclara 403 aliyor
/// (report.forbidden).
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
    /// PDF: "Rapor üretimi background job olarak calistirilmali ve
    /// tamamlandiginda kullanıcıya bildirim gonderilmelidir."
    ///
    /// Bu uc dosyayı DONDURMEZ -- talebi kuyruga alır ve 202 döner.
    /// Rapor hazır olunca kullanıcıya bildirim gidiyor ve dosya
    /// GET /reports/exports/{id} adresinden indiriliyor.
    ///
    /// 202 Accepted, "kabul ettim ama henüz tamamlamadim" demenin
    /// standart yolu. 200 donseydim istemci isin bittigini sanardi.
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
    /// Rapor hazır degilse 404 döner. Kullanıcı bildirimi ALDIKTAN
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

        // Sahiplik kontrolu: bildirim uzerinden
        //
        // exportId bir Guid v7 -- tahmin edilmesi pratikte imkansiz.
        // Ama "tahmin edilemez kimlik" tek başına yetki DEĞİLDİR
        // (güvenlik literaturunde "security through obscurity").
        //
        // Kimligi bir yerden ogrenen biri (log, tarayıcı gecmisi,
        // paylasilan ekran goruntusu) baskasinin gelir raporunu
        // indirebilirdi.
        //
        // Bu yüzden bildirim tablosuna bakiyorum: rapor hazır
        // olduğunda SAHIBINE bir bildirim yaziliyor ve o bildirimin
        // RelatedEntityId'si exportId. Yani "bu raporun bildirimi bu
        // kullanıcıya mi yazilmis?" sorusu, sahiplik sorusunun ta
        // kendisi.
        var sahiplikResult = await Sender
            .Send(new VerifyReportOwnershipQuery(exportId), cancellationToken)
            .ConfigureAwait(false);

        if (!sahiplikResult.IsSuccess || !sahiplikResult.Value)
        {
            // 403 değil 404: raporun VARLIGINI dogrulamiyorum.
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

/// <summary>Organizatör ve admin panelleri. PDF Sprint 13.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
public sealed class DashboardController : ApiControllerBase
{
    /// <summary>Organizatör paneli: PDF'in saydığı 10 metrik.</summary>
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
    /// Admin paneli: PDF'in saydığı 10 metrik.
    /// </summary>
    /// <remarks>
    /// Burada policy KULLANIYORUM (raporlardan farklı olarak).
    ///
    /// Sebep: bu panelin kapsami yok -- ya TÜM sistemi gorursun ya da
    /// hiçbir seyi. "Kismi admin" diye bir sey olmadığı için kontrolü
    /// en dista yapmak doğru ve handler'i sadelestiriyor.
    /// </remarks>
    [HttpGet("admin")]
    [Authorize(Policy = Security.AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<AdminDashboard>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Admin(CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetAdminDashboardQuery(), cancellationToken)
            .ConfigureAwait(false));
}

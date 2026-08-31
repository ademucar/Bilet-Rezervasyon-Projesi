using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Common.Pagination;
using Ticketing.Application.Features.Audit;
using Ticketing.Application.Features.Users;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Kullanici yonetimi -- PDF sayfa 5:
/// "Admin: Tum kullanicilari yonetebilir."
/// </summary>
/// <remarks>
/// Neden AuthController'a eklemedim?
///
/// AuthController kullanicinin KENDI hesabiyla ilgili: giris, kayit,
/// sifre sifirlama. Buradakiler ise BASKA kullanicilar uzerinde
/// yonetim islemleri. Ikisini ayni yerde toplasaydim, "hangi uc
/// herkese acik, hangisi yalnizca admine?" sorusu dosyayi bastan
/// sona okumadan cevaplanamazdi.
///
/// Sinif duzeyinde AdminOnly: bu controller'a ileride eklenecek her
/// uc otomatik korunuyor. Uc basina yazsaydim, birinde unutmak
/// yetki acigi olurdu.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/users")]
[Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
public sealed class AdminUsersController : ApiControllerBase
{
    /// <summary>Kullanicilari sayfali listeler. Ada, e-postaya, role ve duruma gore suzulur.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] GetUsersQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Hesabi aktif veya pasif yapar.
    /// </summary>
    /// <remarks>
    /// Silme DEGIL pasiflestirme: kullanicinin gecmis rezervasyonlari,
    /// biletleri ve odemeleri duruyor. Hesabi silseydim bu kayitlar
    /// sahipsiz kalirdi ve mali gecmis bozulurdu.
    ///
    /// Admin kendi hesabini pasife alamaz -- tek admin kendini
    /// kilitlerse sisteme girecek kimse kalmaz.
    /// </remarks>
    [HttpPut("{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetActive(
        Guid id,
        [FromBody] SetUserActiveRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new SetUserActiveCommand(id, request.IsActive), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Kullaniciya rol atar veya rolunu kaldirir.</summary>
    [HttpPut("{id:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetRole(
        Guid id,
        [FromBody] SetUserRoleRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new SetUserRoleCommand(id, request.RoleName, request.Assign), cancellationToken)
            .ConfigureAwait(false));
}

/// <summary>
/// Denetim kayitlari -- PDF sayfa 5:
/// "Admin: Audit log kayitlarini inceleyebilir."
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/audit-logs")]
[Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
public sealed class AdminAuditLogsController : ApiControllerBase
{
    /// <summary>
    /// Denetim kayitlarini sayfali listeler.
    /// </summary>
    /// <remarks>
    /// YALNIZCA OKUMA ucu var; ekleme, guncelleme, silme yok.
    ///
    /// Bilerek: AuditLog append-only bir tablo. Degistirilebilir bir
    /// denetim kaydi, denetim fikrinin kendisini gecersiz kilar --
    /// izini silebilen biri icin kayit tutmanin anlami kalmaz.
    /// Kayitlar yalnizca is islemlerinin yan etkisi olarak olusuyor.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogListItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] GetAuditLogsQuery query,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(query, cancellationToken).ConfigureAwait(false));
}

public sealed record SetUserActiveRequest(bool IsActive);

public sealed record SetUserRoleRequest(string RoleName, bool Assign);

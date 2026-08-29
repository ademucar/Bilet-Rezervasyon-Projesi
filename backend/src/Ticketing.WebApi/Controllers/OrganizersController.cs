using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Organizers;
using Ticketing.Domain.Enums;
using Ticketing.WebApi.Security;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Organizatör basvurulari. PDF sayfa 5:
/// "Admin organizatör basvurularini onaylayabilir."
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/organizer-applications")]
public sealed class OrganizersController : ApiControllerBase
{
    /// <summary>Giriş yapmış kullanıcı organizatör olmak için basvurur.</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Apply(
        [FromBody] ApplyForOrganizerCommand command,
        CancellationToken cancellationToken)
    {
        var result = await Sender.Send(command, cancellationToken).ConfigureAwait(false);

        return HandleCreated(result, "/api/v1/organizer-applications");
    }

    /// <summary>Basvurulari listeler. Duruma göre filtrelenebilir.</summary>
    [HttpGet]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<IReadOnlyList<OrganizerApplicationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApplications(
        [FromQuery] OrganizerApplicationStatus? status,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new GetOrganizerApplicationsQuery(status), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>
    /// Basvuruyu onaylar: organizatör profili oluşturur ve rolü atar.
    /// Uc işlem de tek transaction içinde.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new ApproveOrganizerApplicationCommand(id), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Basvuruyu reddeder. Gerekce ZORUNLUDUR.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Reject(
        Guid id,
        [FromBody] RejectApplicationRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new RejectOrganizerApplicationCommand(id, request.Reason), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record RejectApplicationRequest(string Reason);

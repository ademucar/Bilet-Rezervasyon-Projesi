using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Cities;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Sehir listesi. Etkinlik filtreleme ve mekan olusturma ekranlarinda kullanilir.
/// Sprint 11'de Redis'te 24 saat cache'lenecek.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cities")]
public sealed class CitiesController : ApiControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCitiesQuery(), cancellationToken).ConfigureAwait(false));
}

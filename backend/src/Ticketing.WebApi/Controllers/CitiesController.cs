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
    /// <summary>
    /// Tum sehirleri doner. Filtre ve mekan formlarindaki acilir liste icin.
    /// </summary>
    /// <remarks>
    /// Sayfalama YOK: 81 il var ve filtre listesinde tamaminin
    /// gorunmesi gerekiyor. Sayfalasaydik frontend'i "sonraki sayfa"
    /// mantigi yazmaya zorlardik -- hicbir kullanici sehir listesinde
    /// sayfa gezmek istemez.
    ///
    /// Sonuc kullanicidan bagimsiz oldugu icin 24 saat onbellekte
    /// tutuluyor (Sprint 11).
    /// </remarks>
    /// <response code="200">Sehir listesi.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCitiesQuery(), cancellationToken).ConfigureAwait(false));
}

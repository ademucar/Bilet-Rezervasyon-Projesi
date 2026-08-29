using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Cities;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Şehir listesi. Etkinlik filtreleme ve mekan oluşturma ekranlarinda kullanilir.
/// Sprint 11'de Redis'te 24 saat cache'lenecek.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cities")]
public sealed class CitiesController : ApiControllerBase
{
    /// <summary>
    /// Tüm sehirleri döner. Filtre ve mekan formlarindaki açılır liste için.
    /// </summary>
    /// <remarks>
    /// Sayfalama YOK: 81 il var ve filtre listesinde tamaminin
    /// görünmesi gerekiyor. Sayfalasaydik frontend'i "sonraki sayfa"
    /// mantığı yazmaya zorlardik -- hiçbir kullanıcı şehir listesinde
    /// sayfa gezmek istemez.
    ///
    /// Sonuç kullanicidan bağımsız olduğu için 24 saat onbellekte
    /// tutuluyor (Sprint 11).
    /// </remarks>
    /// <response code="200">Şehir listesi.</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<CityDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCities(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCitiesQuery(), cancellationToken).ConfigureAwait(false));
}

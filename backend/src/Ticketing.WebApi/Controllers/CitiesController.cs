using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Cities;
using Ticketing.WebApi.Security;

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
    /// görünmesi gerekiyor. Sayfalasaydim frontend'i "sonraki sayfa"
    /// mantığı yazmaya zorlardim -- hiçbir kullanıcı şehir listesinde
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

    /// <summary>
    /// Yeni şehir ekler. PDF sayfa 5: "Kategori, şehir ve salon yönetimi."
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCityRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Sender
            .Send(new CreateCityCommand(request.Name, request.PlateCode), cancellationToken)
            .ConfigureAwait(false);

        return HandleCreated(
            result,
            $"/api/v1/cities/{(result.IsSuccess ? result.Value : Guid.Empty)}");
    }

    /// <summary>
    /// Şehri yeniden adlandirir.
    /// </summary>
    /// <remarks>
    /// PUT degil PATCH gibi davraniyor ama PUT biraktim: degistirilen
    /// tek alan zaten ad. Plaka kodu bilerek degistirilemiyor --
    /// gerekcesi RenameCityCommand'da yazili.
    /// </remarks>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rename(
        Guid id,
        [FromBody] RenameCityRequest request,
        CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new RenameCityCommand(id, request.Name), cancellationToken)
            .ConfigureAwait(false));

    /// <summary>Şehri siler (soft delete). Mekanı olan şehir silinemez.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthenticationSetup.Policies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => HandleResult(await Sender
            .Send(new DeleteCityCommand(id), cancellationToken)
            .ConfigureAwait(false));
}

public sealed record CreateCityRequest(string Name, int PlateCode);

public sealed record RenameCityRequest(string Name);

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Features.Auth;
using Ticketing.Application.Features.Auth.Login;
using Ticketing.Application.Features.Auth.Logout;
using Ticketing.Application.Features.Auth.Password;
using Ticketing.Application.Features.Auth.Profile;
using Ticketing.Application.Features.Auth.RefreshToken;
using Ticketing.Application.Features.Auth.Register;

namespace Ticketing.WebApi.Controllers;

/// <summary>
/// Kimlik dogrulama endpointleri. PDF Sprint 3.
///
/// ==================================================================
/// BU CONTROLLER'DA IS MANTIGI YOK -- BILINCLI
/// ==================================================================
/// PDF zorunlu kurallari:
///   - "Controller icinde is kurali yazilmamalidir."
///   - "Controller dogrudan DbContext kullanmamalidir."
///
/// Her metot uc sey yapiyor:
///   1. Istegi komuta cevir
///   2. MediatR'a gonder
///   3. Sonucu HTTP yanitina cevir
///
/// Tek bir "if" veya veritabani sorgusu yok. Architecture testimiz
/// DbContext kullanimini zaten engelliyor.
/// ==================================================================
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Yeni kullanici kaydi olusturur.</summary>
    /// <response code="200">Kayit basarili, token dondu.</response>
    /// <response code="400">Girdi dogrulama hatasi.</response>
    /// <response code="422">E-posta zaten kullaniliyor.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>Kullanici girisi yapar.</summary>
    /// <response code="200">Giris basarili.</response>
    /// <response code="401">E-posta veya sifre hatali.</response>
    /// <response code="403">Hesap kilitli veya pasif.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Access token'i yeniler. Rotation uygulanir: eski refresh token
    /// iptal edilir ve yenisi uretilir.
    /// </summary>
    /// <response code="200">Yenileme basarili.</response>
    /// <response code="401">Token gecersiz, suresi dolmus veya tekrar kullanilmis.</response>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Oturumu kapatir. Refresh token gonderilirse yalnizca o oturum,
    /// gonderilmezse tum oturumlar kapatilir.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Belirli bir refresh token'i iptal eder.
    ///
    /// Logout'tan farki: bu endpoint bir GUVENLIK islemidir --
    /// kullanici "su cihazdaki oturumu kapat" demek icin kullanir.
    /// Logout ise kendi oturumunu kapatir.
    /// </summary>
    [HttpPost("revoke-token")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeToken(
        [FromBody] LogoutCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Giris yapmis kullanicinin GUNCEL bilgilerini dondurur.
    /// Token'daki bayat veriyi degil, veritabanindaki guncel veriyi verir.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCurrentUserQuery(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Giris yapmis kullanicinin sifresini degistirir.
    /// Mevcut sifre DOGRULANIR -- yalnizca token'a sahip olmak yetmez.
    /// Basarili olursa TUM oturumlar kapatilir.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Sifre sifirlama e-postasi gonderir.
    ///
    /// GUVENLIK: E-posta kayitli olsun olmasin HER ZAMAN 204 doner.
    /// Aksi halde bu endpoint, kayitli e-postalari tespit etmek icin
    /// kullanilabilecek acik bir tarama araci olurdu.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Sifirlama anahtariyla yeni sifre belirler.
    /// Anahtar TEK KULLANIMLIKTIR ve 1 saat gecerlidir.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));
}

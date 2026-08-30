using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Ticketing.WebApi.Security;
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
/// Kimlik doğrulama endpointleri. PDF Sprint 3.
///
/// Bu controller'da is mantigi yok -- bilincli
///
/// PDF zorunlu kurallari:
///   - "Controller içinde is kuralı yazilmamalidir."
///   - "Controller doğrudan DbContext kullanmamalidir."
///
/// Her metot uc sey yapiyor:
///   1. Istegi komuta cevir
///   2. MediatR'a gönder
///   3. Sonucu HTTP yanitina cevir
///
/// Tek bir "if" veya veritabani sorgusu yok. Architecture testim
/// DbContext kullanimini zaten engelliyor.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
// HIZ SINIRI -- PDF Sprint 15: "Login, Register, Şifre sıfırlama"
//
// Politika SINIF duzeyinde: bu controller'daki TÜM uclar korunuyor.
//
// Uc uc tek tek isaretleseydik, ilerde eklenen bir uc (örneğin
// "e-posta doğrulama kodu tekrar gönder") korumasiz kalırdı -- ve
// bu tam olarak brute force'a açık bir uc olurdu.
//
// Sinif duzeyi "varsayılan olarak güvenli" davraniyor.
[EnableRateLimiting(RateLimitingSetup.Policies.Authentication)]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>Yeni kullanıcı kaydı oluşturur.</summary>
    /// <response code="200">Kayıt başarılı, token dondu.</response>
    /// <response code="400">Girdi doğrulama hatası.</response>
    /// <response code="422">E-posta zaten kullanılıyor.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>Kullanıcı girişi yapar.</summary>
    /// <response code="200">Giriş başarılı.</response>
    /// <response code="401">E-posta veya şifre hatalı.</response>
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
    /// iptal edilir ve yenisi üretilir.
    /// </summary>
    /// <response code="200">Yenileme başarılı.</response>
    /// <response code="401">Token geçersiz, süresi dolmuş veya tekrar kullanılmış.</response>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Oturumu kapatır. Refresh token gonderilirse yalnızca o oturum,
    /// gonderilmezse tüm oturumlar kapatilir.
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
    /// Logout'tan farki: bu endpoint bir GÜVENLİK islemidir --
    /// kullanıcı "su cihazdaki oturumu kapat" demek için kullanir.
    /// Logout ise kendi oturumunu kapatır.
    /// </summary>
    [HttpPost("revoke-token")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeToken(
        [FromBody] LogoutCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Giriş yapmış kullanıcının GUNCEL bilgilerini döndürür.
    /// Token'daki bayat veriyi değil, veritabanindaki güncel veriyi verir.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(new GetCurrentUserQuery(), cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Giriş yapmış kullanıcının sifresini değiştirir.
    /// Mevcut şifre DOGRULANIR -- yalnızca token'a sahip olmak yetmez.
    /// Başarılı olursa TÜM oturumlar kapatilir.
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
    /// Şifre sıfırlama e-postası gönderir.
    ///
    /// GÜVENLİK: E-posta kayıtlı olsun olmasın HER ZAMAN 204 döner.
    /// Aksi halde bu endpoint, kayıtlı e-postalari tespit etmek için
    /// kullanilabilecek açık bir tarama araci olurdu.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordCommand command,
        CancellationToken cancellationToken)
        => HandleResult(await Sender.Send(command, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// Sıfırlama anahtariyla yeni şifre belirler.
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

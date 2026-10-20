using System.Security.Claims;
using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Identity;
using FixTrack.Web.Models.Account;
using FixTrack.Web.Security;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FixTrack.Web.Controllers;

/// <summary>
/// Inicio y cierre de sesión y cambio de la contraseña propia. No existe registro público: las cuentas las crea un
/// administrador. Un intento fallido recibe siempre el mismo mensaje y tarda lo mismo, exista o no la cuenta, esté
/// bloqueada o deshabilitada (ver <see cref="FailedLoginEqualizer"/>); el motivo real solo queda en el registro interno.
/// </summary>
[Route("cuenta")]
public sealed class AccountController(
    SignInManager<FixTrackUser> signIn,
    IUserAccountService accounts,
    FailedLoginEqualizer failedLogins,
    ApplicationInfo application,
    ILogger<AccountController> logger) : Controller
{
    public const string LoginFailed =
        "No se pudo iniciar sesión con esos datos. Revisa el correo y la contraseña. Tras varios intentos fallidos, el acceso se bloquea unos minutos por seguridad.";

    [AllowAnonymous]
    [HttpGet("iniciar-sesion")]
    public IActionResult Login([FromQuery(Name = "volver")] string? returnUrl, [FromQuery(Name = "salida")] bool signedOut = false)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        return View(LoginModel(null, returnUrl, null, signedOut ? "Cerraste sesión correctamente." : null));
    }

    [AllowAnonymous]
    [HttpPost("iniciar-sesion")]
    [EnableRateLimiting(SecurityServiceCollectionExtensions.LoginRateLimit)]
    public async Task<IActionResult> Login(LoginFormModel form, [FromQuery(Name = "volver")] string? returnUrl, CancellationToken cancellationToken)
    {
        var email = form.Email?.Trim();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(form.Password))
        {
            return View(LoginModel(email, returnUrl, "Escribe tu correo electrónico y tu contraseña.", null));
        }

        var startedAt = failedLogins.Start();
        var user = await signIn.UserManager.FindByEmailAsync(email);
        if (user is null)
        {
            failedLogins.SimulatePasswordCheck(form.Password);
            logger.LogInformation("Inicio de sesión rechazado: el correo no tiene cuenta.");
            return await LoginFailedAsync(email, returnUrl, startedAt, cancellationToken);
        }

        // lockoutOnFailure: tras 5 intentos fallidos la cuenta se bloquea 15 minutos.
        var result = await signIn.PasswordSignInAsync(user, form.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await accounts.RecordSignInAsync(user.Id, cancellationToken);
            logger.LogInformation("Inicio de sesión de la cuenta {UserId}.", user.Id);
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        if (result.IsLockedOut || result.IsNotAllowed)
        {
            // Identity rechaza sin calcular el hash: se calcula uno equivalente para no responder más rápido.
            failedLogins.SimulatePasswordCheck(form.Password);
            logger.LogWarning(
                "Inicio de sesión rechazado: la cuenta {UserId} está {Reason}.",
                user.Id,
                user.LockoutEnd == DateTimeOffset.MaxValue ? "deshabilitada" : "bloqueada temporalmente");
        }
        else
        {
            logger.LogInformation("Inicio de sesión rechazado: contraseña incorrecta para la cuenta {UserId}.", user.Id);
        }

        return await LoginFailedAsync(email, returnUrl, startedAt, cancellationToken);
    }

    [HttpPost("cerrar-sesion")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login), new { salida = true });
    }

    /// <summary>Destino de la redirección por falta de permisos, si se accede directamente.</summary>
    [HttpGet("acceso-denegado")]
    public IActionResult AccessDenied() => StatusCode(StatusCodes.Status403Forbidden);

    [HttpGet("contrasena")]
    public IActionResult ChangePassword() => View(new ChangePasswordFormModel());

    [HttpPost("contrasena")]
    public async Task<IActionResult> ChangePassword(ChangePasswordFormModel form, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Challenge();
        }

        var result = await accounts.ChangeOwnPasswordAsync(userId, form.CurrentPassword, form.Password, form.ConfirmPassword, cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return Challenge();
        }

        if (!result.Succeeded)
        {
            foreach (var (field, message) in result.Errors)
            {
                ModelState.AddModelError(field, message);
            }

            // Las contraseñas no se devuelven al formulario.
            return View(new ChangePasswordFormModel());
        }

        // El cambio renueva el sello de seguridad: se actualiza la sesión actual para no cerrarla.
        if (await signIn.UserManager.FindByIdAsync(userId) is { } user)
        {
            await signIn.RefreshSignInAsync(user);
        }

        StatusMessage.Success(TempData, "Tu contraseña se cambió correctamente.");
        return LocalRedirect("/");
    }

    /// <summary>La misma respuesta para cualquier fallo, después del tiempo mínimo.</summary>
    private async Task<IActionResult> LoginFailedAsync(string email, string? returnUrl, long startedAt, CancellationToken cancellationToken)
    {
        await failedLogins.CompleteFailureAsync(startedAt, cancellationToken);
        return View(LoginModel(email, returnUrl, LoginFailed, null));
    }

    private LoginViewModel LoginModel(string? email, string? returnUrl, string? error, string? notice) =>
        new(email, Url.IsLocalUrl(returnUrl) ? returnUrl : null, error, notice,
            new ApplicationDetails(application.Version, application.EnvironmentName, application.IsDevelopment));

    /// <summary>Solo se vuelve a direcciones del propio sitio: evita redirecciones abiertas a otros dominios.</summary>
    private string SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
}

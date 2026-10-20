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
/// administrador. Los mensajes de error no revelan si un correo tiene cuenta.
/// </summary>
[Route("cuenta")]
public sealed class AccountController(
    SignInManager<FixTrackUser> signIn,
    IUserAccountService accounts,
    ApplicationInfo application,
    ILogger<AccountController> logger) : Controller
{
    private const string InvalidCredentials = "El correo o la contraseña no son correctos.";

    private const string LockedOut =
        "Por seguridad, la cuenta está bloqueada temporalmente o deshabilitada. Intenta de nuevo en 15 minutos o comunícate con el administrador.";

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

        var user = await signIn.UserManager.FindByEmailAsync(email);
        if (user is null)
        {
            logger.LogInformation("Intento de inicio de sesión con un correo sin cuenta.");
            return View(LoginModel(email, returnUrl, InvalidCredentials, null));
        }

        // lockoutOnFailure: tras 5 intentos fallidos la cuenta se bloquea 15 minutos.
        var result = await signIn.PasswordSignInAsync(user, form.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await accounts.RecordSignInAsync(user.Id, cancellationToken);
            logger.LogInformation("Inicio de sesión de la cuenta {UserId}.", user.Id);
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        if (result.IsLockedOut)
        {
            logger.LogWarning("Inicio de sesión rechazado: la cuenta {UserId} está bloqueada o deshabilitada.", user.Id);
            return View(LoginModel(email, returnUrl, LockedOut, null));
        }

        logger.LogInformation("Contraseña incorrecta para la cuenta {UserId}.", user.Id);
        return View(LoginModel(email, returnUrl, InvalidCredentials, null));
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

    private LoginViewModel LoginModel(string? email, string? returnUrl, string? error, string? notice) =>
        new(email, Url.IsLocalUrl(returnUrl) ? returnUrl : null, error, notice,
            new ApplicationDetails(application.Version, application.EnvironmentName, application.IsDevelopment));

    /// <summary>Solo se vuelve a direcciones del propio sitio: evita redirecciones abiertas a otros dominios.</summary>
    private string SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
}

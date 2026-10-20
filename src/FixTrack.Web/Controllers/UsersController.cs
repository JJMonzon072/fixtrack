using System.Security.Claims;
using FixTrack.Application.Common;
using FixTrack.Application.Security;
using FixTrack.Web.Models.Account;
using FixTrack.Web.Security;
using FixTrack.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FixTrack.Web.Controllers;

/// <summary>Cuentas de acceso y roles. Solo para administradores; las contraseñas nunca se devuelven a las vistas.</summary>
[Route("usuarios")]
[Authorize(Policy = AuthorizationPolicies.ManageAccounts)]
public sealed class UsersController(IUserAccountService accounts) : Controller
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new UsersIndexViewModel(await accounts.ListAsync(cancellationToken), CurrentUserId));

    [HttpGet("nueva")]
    public IActionResult Create() => View("Editor", new UserEditorViewModel(null, new UserFormModel { Role = Roles.Operator }, false));

    [HttpPost("nueva")]
    public async Task<IActionResult> Create(UserFormModel form, CancellationToken cancellationToken)
    {
        var result = await accounts.CreateAsync(
            new NewUserAccountInput(form.FullName, form.Email, form.Role, form.Password, form.ConfirmPassword),
            cancellationToken);
        if (!result.Succeeded)
        {
            AddErrors(result);
            return View("Editor", new UserEditorViewModel(null, WithoutPasswords(form), false));
        }

        StatusMessage.Success(TempData, "Cuenta creada. Comparte la contraseña inicial con la persona por un medio seguro.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}/editar")]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(id, cancellationToken);
        return account is null
            ? NotFound()
            : View("Editor", new UserEditorViewModel(account, new UserFormModel { FullName = account.FullName, Role = account.Role }, id == CurrentUserId));
    }

    [HttpPost("{id:guid}/editar")]
    public async Task<IActionResult> Edit(string id, UserFormModel form, CancellationToken cancellationToken)
    {
        var result = await accounts.UpdateAsync(id, new UserAccountInput(form.FullName, form.Role), CurrentUserId, cancellationToken);
        switch (result.Status)
        {
            case CommandStatus.NotFound:
                return NotFound();
            case CommandStatus.Succeeded:
                StatusMessage.Success(TempData, "Los cambios se guardaron correctamente.");
                return RedirectToAction(nameof(Index));
            case CommandStatus.Conflict:
                StatusMessage.Warning(TempData, result.Message ?? "No se pudo completar la operación.");
                return RedirectToAction(nameof(Edit), new { id });
        }

        AddErrors(result);
        var account = await accounts.GetAsync(id, cancellationToken);
        return account is null ? NotFound() : View("Editor", new UserEditorViewModel(account, WithoutPasswords(form), id == CurrentUserId));
    }

    [HttpPost("{id:guid}/deshabilitar")]
    public Task<IActionResult> Disable(string id, CancellationToken cancellationToken) =>
        SetEnabledAsync(id, enabled: false, "Cuenta deshabilitada. Sus sesiones abiertas se cerrarán en menos de un minuto.", cancellationToken);

    [HttpPost("{id:guid}/habilitar")]
    public Task<IActionResult> Enable(string id, CancellationToken cancellationToken) =>
        SetEnabledAsync(id, enabled: true, "Cuenta habilitada.", cancellationToken);

    [HttpGet("{id:guid}/contrasena")]
    public async Task<IActionResult> ResetPassword(string id, CancellationToken cancellationToken)
    {
        var account = await accounts.GetAsync(id, cancellationToken);
        return account is null ? NotFound() : View("Password", new ResetPasswordViewModel(account));
    }

    [HttpPost("{id:guid}/contrasena")]
    public async Task<IActionResult> ResetPassword(string id, ResetPasswordFormModel form, CancellationToken cancellationToken)
    {
        var result = await accounts.ResetPasswordAsync(id, form.Password, form.ConfirmPassword, cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrors(result);
            var account = await accounts.GetAsync(id, cancellationToken);
            return account is null ? NotFound() : View("Password", new ResetPasswordViewModel(account));
        }

        StatusMessage.Success(TempData, "Contraseña restablecida. Las sesiones abiertas de la cuenta se cerrarán en menos de un minuto.");
        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<IActionResult> SetEnabledAsync(string id, bool enabled, string successMessage, CancellationToken cancellationToken)
    {
        var result = await accounts.SetEnabledAsync(id, enabled, CurrentUserId, cancellationToken);
        if (result.Status == CommandStatus.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            StatusMessage.Success(TempData, successMessage);
        }
        else
        {
            StatusMessage.Warning(TempData, result.Message ?? "No se pudo completar la operación.");
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    private static UserFormModel WithoutPasswords(UserFormModel form) =>
        new() { FullName = form.FullName, Email = form.Email, Role = form.Role };

    private void AddErrors(CommandResult result)
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field, message);
        }
    }
}

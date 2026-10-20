using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using FixTrack.Application.Security;
using FixTrack.Infrastructure.Persistence;
using FixTrack.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Web;

/// <summary>Un Operador no puede ejecutar operaciones de administración, ni siquiera enviando el formulario a mano.</summary>
public sealed partial class SecurityTests
{
    /// <summary>
    /// Recorre todos los POST de la aplicación protegidos por las políticas de administración (los actuales y los que se
    /// agreguen) y los envía con la sesión y el token antifalsificación válidos de un Operador. Los Id de la ruta son los
    /// de su propia cuenta (el caso peligroso: ascenderse a Administrador) y los de un técnico existente.
    /// </summary>
    [Fact]
    public async Task Operators_get_403_on_every_administration_post_and_nothing_changes()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var technicianId = FormPost.CreatedId(await FormPost.SendAsync(admin, "/tecnicos/nuevo", new()
        {
            ["FullName"] = "Tecnico Blindado",
            ["Specialty"] = "Redes",
        }));
        using var client = await factory.CreateAuthenticatedClientAsync(TestAccounts.Operator);
        var before = await AccountsAsync();
        var operatorId = before.Single(account => account.Email == TestAccounts.Operator.Email).Id;

        var urls = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true)
            .Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(data => data.Policy is AuthorizationPolicies.ManageAccounts or AuthorizationPolicies.ManageTechnicians))
            .Select(endpoint => "/" + RouteId().Replace(
                endpoint.RoutePattern.RawText!,
                match => match.Groups["type"].Value == "guid" ? operatorId : technicianId.ToString(CultureInfo.InvariantCulture)))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        var statuses = new List<(string Url, HttpStatusCode Status)>();
        foreach (var url in urls)
        {
            using var response = await FormPost.SendAsync(client, url, new()
            {
                ["FullName"] = "Operador Ascendido",
                ["Email"] = $"ascenso-{Guid.NewGuid():N}@fixtrack.test",
                ["Role"] = Roles.Administrator,
                ["Specialty"] = "Intrusión",
                ["Password"] = "Ascenso1234567",
                ["ConfirmPassword"] = "Ascenso1234567",
            });
            statuses.Add((url, response.StatusCode));
        }

        var after = await AccountsAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var technician = await scope.ServiceProvider.GetRequiredService<FixTrackDbContext>().Technicians.SingleAsync(t => t.Id == technicianId, Token);

        // Cuentas: nueva, editar, deshabilitar, habilitar, desbloquear y contraseña. Técnicos: nuevo, editar, desactivar y reactivar.
        Assert.True(urls.Count >= 10, string.Join(", ", urls));
        Assert.Contains($"/usuarios/{operatorId}/editar", urls);
        Assert.All(statuses, item => Assert.True(item.Status == HttpStatusCode.Forbidden, $"{item.Url} respondió {(int)item.Status}"));
        Assert.Equal(
            before.Select(account => (account.Id, account.Role, account.IsEnabled, account.FullName)),
            after.Select(account => (account.Id, account.Role, account.IsEnabled, account.FullName)));
        Assert.Equal(("Tecnico Blindado", "Redes", true), (technician.FullName, technician.Specialty, technician.IsActive));
    }

    private async Task<List<UserAccount>> AccountsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return [.. (await scope.ServiceProvider.GetRequiredService<IUserAccountService>().ListAsync(Token)).OrderBy(account => account.Id, StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"\{id:(?<type>guid|int)\}")]
    private static partial Regex RouteId();
}

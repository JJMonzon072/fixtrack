using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace FixTrack.Tests.Web;

/// <summary>
/// Clientes HTTP con sesión iniciada por el formulario real de inicio de sesión. La cookie de cada cuenta se reutiliza
/// dentro de una misma aplicación de prueba para no repetir el inicio de sesión en cada prueba.
/// </summary>
public static partial class AuthenticatedClients
{
    public const string SessionCookie = "FixTrack.Sesion";

    private static readonly Uri BaseAddress = new("http://localhost");
    private static readonly ConditionalWeakTable<WebApplicationFactory<Program>, ConcurrentDictionary<string, Cookie>> Sessions = [];

    /// <summary>Cliente con la sesión de la cuenta indicada (el administrador si no se indica otra).</summary>
    public static async Task<HttpClient> CreateAuthenticatedClientAsync(this WebApplicationFactory<Program> factory, TestAccount? account = null)
    {
        account ??= TestAccounts.Administrator;
        var cookies = new CookieContainerHandler();
        var client = factory.CreateDefaultClient(BaseAddress, new RedirectHandler(), cookies);

        var sessions = Sessions.GetOrCreateValue(factory);
        if (sessions.TryGetValue(account.Email, out var session))
        {
            cookies.Container.Add(BaseAddress, session);
            return client;
        }

        using var response = await LogInAsync(client, account.Email, account.Password);
        var cookie = cookies.Container.GetCookies(BaseAddress)[SessionCookie]
            ?? throw new InvalidOperationException($"No se pudo iniciar sesión con {account.Email}: {response.StatusCode}.");
        sessions[account.Email] = cookie;
        return client;
    }

    /// <summary>Envía el formulario de inicio de sesión con su token antifalsificación.</summary>
    public static async Task<HttpResponseMessage> LogInAsync(HttpClient client, string email, string password)
    {
        var token = TestContext.Current.CancellationToken;
        var page = await client.GetStringAsync("/cuenta/iniciar-sesion", token);
        var form = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = AntiforgeryToken().Match(page).Groups["token"].Value,
        };
        return await client.PostAsync("/cuenta/iniciar-sesion", new FormUrlEncodedContent(form), token);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}

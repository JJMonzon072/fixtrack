using System.Globalization;
using System.Text.RegularExpressions;

namespace FixTrack.Tests.Web;

/// <summary>Envía formularios como lo haría el navegador, incluido el token antifalsificación.</summary>
internal static partial class FormPost
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Obtiene un token válido (desde el formulario de clientes) y envía el POST con él.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        var form = await client.GetStringAsync("/clientes/nuevo", Token);
        fields["__RequestVerificationToken"] = AntiforgeryToken().Match(form).Groups["token"].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields), Token);
    }

    /// <summary>Id del registro creado, tomado de la dirección a la que redirige el guardado (por ejemplo, /equipos/7).</summary>
    public static int CreatedId(HttpResponseMessage response) =>
        int.Parse(TrailingId().Match(response.RequestMessage!.RequestUri!.AbsolutePath).Groups["id"].Value, CultureInfo.InvariantCulture);

    public static async Task<int> CreateCustomerAsync(HttpClient client, string name, string phone, string? email = null) =>
        CreatedId(await SendAsync(client, "/clientes/nuevo", new()
        {
            ["FullName"] = name,
            ["Phone"] = phone,
            ["Email"] = email ?? string.Empty,
        }));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryToken();

    [GeneratedRegex(@"/(?<id>\d+)$")]
    private static partial Regex TrailingId();
}

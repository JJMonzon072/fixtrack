
using Microsoft.AspNetCore.HttpOverrides;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Ejecución detrás de un proxy HTTPS (por ejemplo, el balanceador de Render, que termina TLS y reenvía la solicitud
/// por HTTP). Los encabezados X-Forwarded-For y X-Forwarded-Proto solo se aceptan cuando la conexión viene de una red
/// configurada en <c>ReverseProxy:KnownNetworks</c>: una solicitud directa desde cualquier otra dirección no puede
/// hacerse pasar por HTTPS ni cambiar su IP. Sin redes configuradas, los encabezados se ignoran.
/// </summary>
public static class ReverseProxy
{
    public const string KnownNetworksSection = "ReverseProxy:KnownNetworks";

    public static IReadOnlyList<string> KnownNetworks(IConfiguration configuration) =>
        configuration.GetSection(KnownNetworksSection).Get<string[]>()?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];

    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = KnownNetworks(configuration).Select(System.Net.IPNetwork.Parse).ToList();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Los valores se procesan de derecha a izquierda mientras quien los agregó sea un proxy confiable: la IP del
            // cliente es la primera que no pertenece a esas redes y lo que el cliente haya escrito más a la izquierda se ignora.
            options.ForwardLimit = null;
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });

        return services;
    }

    public static IApplicationBuilder UseReverseProxySupport(this IApplicationBuilder app, IConfiguration configuration) =>
        KnownNetworks(configuration).Count > 0 ? app.UseForwardedHeaders() : app;
}

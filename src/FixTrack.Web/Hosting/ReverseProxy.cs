using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FixTrack.Web.Hosting;

/// <summary>
/// Ejecución detrás de un proxy HTTPS (por ejemplo, el balanceador de Render, que termina TLS y reenvía la solicitud
/// por HTTP). Los encabezados reenviados (la IP del cliente y X-Forwarded-Proto) solo se aceptan cuando la conexión
/// llega desde un proxy configurado explícitamente; no se confía en ningún rango por omisión, ni siquiera en loopback.
/// <list type="bullet">
/// <item><c>ReverseProxy:KnownProxies:N</c>: direcciones IP exactas de los proxies confiables.</item>
/// <item><c>ReverseProxy:KnownNetworks:N</c>: redes (CIDR) de los proxies confiables.</item>
/// <item><c>ReverseProxy:ClientIpHeader</c>: encabezado con la IP del cliente. Por defecto, X-Forwarded-For, que se
/// procesa de derecha a izquierda hasta la primera dirección no confiable. Con un encabezado de un solo valor que el
/// borde reescribe (por ejemplo, True-Client-IP) solo se toma el último salto.</item>
/// <item><c>ReverseProxy:IgnoreForwardedHeaders</c>: declara que no hay proxy confiable; los encabezados se ignoran.</item>
/// </list>
/// En producción es obligatorio elegir: proxies confiables o <c>IgnoreForwardedHeaders</c>. Si llegan encabezados
/// reenviados desde una dirección no confiable, se registra una advertencia con esa dirección, para poder averiguar la
/// del proxy real después del primer despliegue sin confiar en ella mientras tanto.
/// </summary>
public static class ReverseProxy
{
    public const string Section = "ReverseProxy";
    public const string DefaultClientIpHeader = "X-Forwarded-For";

    /// <summary>Configuración leída y validada de la sección <c>ReverseProxy</c>.</summary>
    public sealed record Settings(
        IReadOnlyList<IPAddress> KnownProxies,
        IReadOnlyList<System.Net.IPNetwork> KnownNetworks,
        string ClientIpHeader,
        bool IgnoreForwardedHeaders)
    {
        public bool HasTrustedProxies => KnownProxies.Count > 0 || KnownNetworks.Count > 0;

        /// <summary>¿La conexión viene de un proxy confiable? Las IPv4 mapeadas en IPv6 se comparan como IPv4.</summary>
        public bool Trusts(IPAddress? address)
        {
            if (address is null)
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return KnownProxies.Contains(address) || KnownNetworks.Any(network => network.Contains(address));
        }
    }

    /// <exception cref="InvalidOperationException">Si una dirección o red no es válida, o la configuración es contradictoria.</exception>
    public static Settings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var proxies = Values(section.GetSection("KnownProxies"))
            .Select(value => IPAddress.TryParse(value, out var address) ? Normalize(address) : throw Invalid("KnownProxies", value))
            .ToList();
        var networks = Values(section.GetSection("KnownNetworks"))
            .Select(value => System.Net.IPNetwork.TryParse(value, out var network) ? network : throw Invalid("KnownNetworks", value))
            .ToList();
        var header = section["ClientIpHeader"] is { Length: > 0 } configured ? configured.Trim() : DefaultClientIpHeader;
        var ignore = section.GetValue("IgnoreForwardedHeaders", false);

        if (ignore && (proxies.Count > 0 || networks.Count > 0))
        {
            throw new InvalidOperationException(
                "ReverseProxy:IgnoreForwardedHeaders no puede combinarse con KnownProxies o KnownNetworks: elige una de las dos opciones.");
        }

        return new Settings(proxies, networks, header, ignore);
    }

    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var settings = Read(configuration);
        if (environment.IsProduction() && !settings.HasTrustedProxies && !settings.IgnoreForwardedHeaders)
        {
            throw new InvalidOperationException(
                "En producción hay que indicar qué proxy es confiable: define ReverseProxy__KnownProxies__0 o " +
                "ReverseProxy__KnownNetworks__0 con las direcciones reales del proxy de la plataforma, o declara " +
                "ReverseProxy__IgnoreForwardedHeaders=true para ignorar los encabezados reenviados. Consulta el README (Despliegue).");
        }

        services.AddSingleton(settings);
        services.AddSingleton<IgnoredForwardedHeadersLog>();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardedForHeaderName = settings.ClientIpHeader;

            // Solo los proxies configurados: se quitan los valores por defecto (loopback).
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in settings.KnownProxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in settings.KnownNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }

            // X-Forwarded-For se procesa de derecha a izquierda mientras quien agregó cada valor sea un proxy confiable:
            // la IP del cliente es la primera que no pertenece a esos proxies y lo que el cliente escribió más a la
            // izquierda se ignora. Un encabezado de un solo valor (True-Client-IP) se toma solo del último salto.
            options.ForwardLimit = settings.ClientIpHeader.Equals(DefaultClientIpHeader, StringComparison.OrdinalIgnoreCase) ? null : 1;
        });
        return services;
    }

    public static IApplicationBuilder UseReverseProxySupport(this IApplicationBuilder app)
    {
        var settings = app.ApplicationServices.GetRequiredService<Settings>();
        var ignored = app.ApplicationServices.GetRequiredService<IgnoredForwardedHeadersLog>();
        app.Use((context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (!settings.Trusts(remote) && HasForwardedHeaders(context.Request.Headers, settings.ClientIpHeader))
            {
                ignored.Report(remote);
            }

            return next(context);
        });

        return settings.HasTrustedProxies ? app.UseForwardedHeaders() : app;
    }

    /// <summary>
    /// Una IPv4 escrita en notación IPv6 (<c>::ffff:10.1.2.3</c>, como la muestran algunas plataformas) se trata como la
    /// IPv4: así coincide con la dirección de la conexión, que también se compara en IPv4.
    /// </summary>
    public static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static bool HasForwardedHeaders(IHeaderDictionary headers, string clientIpHeader) =>
        headers.ContainsKey(ForwardedHeadersDefaults.XForwardedProtoHeaderName)
        || headers.ContainsKey(ForwardedHeadersDefaults.XForwardedForHeaderName)
        || headers.ContainsKey(clientIpHeader);

    private static IEnumerable<string> Values(IConfigurationSection section) =>
        section.GetChildren().Select(child => child.Value).OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim());

    private static InvalidOperationException Invalid(string name, string value) =>
        new($"ReverseProxy:{name} contiene un valor no válido: «{value}».");
}

/// <summary>
/// Registra, como máximo una vez cada 15 minutos, que llegaron encabezados reenviados desde una dirección no confiable.
/// Solo se anota la dirección del extremo que abrió la conexión, no el contenido de los encabezados.
/// </summary>
public sealed class IgnoredForwardedHeadersLog(ILogger<IgnoredForwardedHeadersLog> logger, TimeProvider clock)
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private long _nextReport = long.MinValue;

    public void Report(IPAddress? remote)
    {
        var now = clock.GetUtcNow().UtcTicks;
        var next = Interlocked.Read(ref _nextReport);
        if (now < next || Interlocked.CompareExchange(ref _nextReport, now + Interval.Ticks, next) != next)
        {
            return;
        }

        logger.LogWarning(
            "Se ignoraron encabezados reenviados (X-Forwarded-*) recibidos desde {RemoteAddress}: esa dirección no está en " +
            "ReverseProxy:KnownProxies ni en ReverseProxy:KnownNetworks. Si es el proxy de la plataforma, agrégala a la configuración.",
            remote is null ? "desconocida" : ReverseProxy.Normalize(remote).ToString());
    }
}

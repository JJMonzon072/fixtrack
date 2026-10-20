using FixTrack.Application;
using FixTrack.Infrastructure;
using FixTrack.Web;
using FixTrack.Web.Hosting;
using FixTrack.Web.Security;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // En plataformas como Render, el puerto HTTP llega en la variable PORT.
    if (builder.Configuration["PORT"] is { Length: > 0 } port)
    {
        builder.WebHost.UseSetting("http_ports", port);
    }

    // Raíz de composición: cada capa registra sus propios servicios.
    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddPresentation()
        .AddSecurity(builder.Configuration, builder.Environment)
        .AddReverseProxySupport(builder.Configuration, builder.Environment)
        .AddHealthEndpoints();

    var app = builder.Build();

    // Comandos de administración (crear el primer administrador, aplicar migraciones): se ejecutan y terminan.
    if (ConsoleCommands.IsCommand(args))
    {
        return await ConsoleCommands.RunAsync(app, args);
    }

    // Primero: el esquema (https) y la IP real del cliente, si la solicitud llega desde un proxy confiable.
    app.UseReverseProxySupport();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/error");
        app.UseHsts();
    }

    app.UseStatusCodePagesWithReExecute("/error/{0}");

    // Redirección a HTTPS cuando se conoce el puerto HTTPS (perfil "https", HTTPS_PORT, endpoints de Kestrel).
    // Sin ese dato el middleware no puede redirigir y solo registraría una advertencia: es el caso del contenedor
    // detrás de Render, cuyo balanceador termina TLS y ya redirige HTTP a HTTPS. Fuera de Desarrollo, HSTS se envía
    // en las respuestas HTTPS (las que llegan por HTTPS directamente o indicadas por el proxy confiable).
    // Las comprobaciones de salud responden también por HTTP: las usa la plataforma desde su red interna.
    if (HttpsEndpoint.IsConfigured(app.Configuration))
    {
        app.UseWhen(context => !HealthEndpoints.IsHealthRequest(context), branch => branch.UseHttpsRedirection());
    }

    app.UseSecurityHeaders();
    app.UseRequestLocalization();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Los archivos estáticos (estilos, íconos, fuentes) son públicos: los necesita la página de inicio de sesión.
    app.MapStaticAssets().AllowAnonymous();
    app.MapHealthEndpoints();
    app.MapControllers()
        .WithStaticAssets();

    await app.RunAsync();
    return 0;
}
catch (Exception error) when (Environment.ProcessId == 1)
{
    // Como PID 1 de un contenedor, una excepción no controlada deja el proceso girando en lugar de terminarlo (el kernel
    // ignora la señal de aborto para PID 1). Se informa el error y se sale con código 1, para que la plataforma lo detecte.
    await Console.Error.WriteLineAsync($"FixTrack no pudo iniciarse o se detuvo por un error: {error}");
    return 1;
}

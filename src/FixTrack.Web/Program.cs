using FixTrack.Application;
using FixTrack.Infrastructure;
using FixTrack.Web;
using FixTrack.Web.Hosting;
using FixTrack.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// Raíz de composición: cada capa registra sus propios servicios.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation()
    .AddSecurity(builder.Configuration, builder.Environment);

var app = builder.Build();

// Comandos de administración (crear el primer administrador, aplicar migraciones): se ejecutan y terminan.
if (ConsoleCommands.IsCommand(args))
{
    return await ConsoleCommands.RunAsync(app, args);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

// Fuera de Desarrollo, HTTPS es obligatorio: HSTS y redirección. En Desarrollo solo se redirige
// cuando el perfil expone un endpoint HTTPS (perfil "https"). El perfil "http" no tiene puerto
// HTTPS de destino, y el middleware solo registraría una advertencia en cada arranque.
if (!app.Environment.IsDevelopment() || HttpsEndpoint.IsConfigured(app.Configuration))
{
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();
app.UseRequestLocalization();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Los archivos estáticos (estilos, íconos, fuentes) son públicos: los necesita la página de inicio de sesión.
app.MapStaticAssets().AllowAnonymous();
app.MapControllers()
    .WithStaticAssets();

await app.RunAsync();
return 0;

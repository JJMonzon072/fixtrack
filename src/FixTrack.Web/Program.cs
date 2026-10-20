using FixTrack.Application;
using FixTrack.Infrastructure;
using FixTrack.Web;
using FixTrack.Web.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Raíz de composición: cada capa registra sus propios servicios.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation();

var app = builder.Build();

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

app.UseRequestLocalization();
app.UseRouting();

app.MapStaticAssets();
app.MapControllers()
    .WithStaticAssets();

app.Run();

using FixTrack.Application;
using FixTrack.Infrastructure;
using FixTrack.Web;

var builder = WebApplication.CreateBuilder(args);

// Raíz de composición: cada capa registra sus propios servicios.
builder.Services
    .AddApplication()
    .AddInfrastructure()
    .AddPresentation();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();

app.MapStaticAssets();
app.MapControllers()
    .WithStaticAssets();

app.Run();

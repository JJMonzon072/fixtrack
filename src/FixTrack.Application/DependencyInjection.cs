using FixTrack.Application.Customers;
using FixTrack.Application.Dashboard;
using FixTrack.Application.Devices;
using FixTrack.Application.Modules;
using FixTrack.Application.Reports;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Application;

public static class DependencyInjection
{
    /// <summary>Registra los servicios de la capa de aplicación.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IModuleCatalog, ModuleCatalog>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IDeviceService, DeviceService>();
        services.AddScoped<ITechnicianService, TechnicianService>();
        services.AddScoped<IServiceOrderService, ServiceOrderService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}

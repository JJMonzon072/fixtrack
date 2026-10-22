using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.Security;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Demo.Scenario;
using FixTrack.Domain.Common;
using FixTrack.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Demo;

/// <summary>Contraseñas de las cuentas ficticias: llegan de la configuración y nunca se escriben en la consola.</summary>
internal sealed record DemoPasswords(string Administrator, string Operator, string ReadOnly);

internal enum DemoLoadOutcome
{
    Loaded,

    /// <summary>La base ya tenía la demostración completa (por ejemplo, al volver a iniciar el entorno).</summary>
    AlreadyLoaded,
}

/// <summary>
/// Carga el escenario con los servicios de aplicación de FixTrack, operación por operación y con la fecha y el autor de
/// cada una: cada orden pasa por las mismas validaciones, transiciones, historial y auditoría que en la aplicación web.
/// No escribe filas directamente en la base.
/// </summary>
internal sealed class DemoLoader(IServiceProvider services)
{
    private readonly Dictionary<string, int> _technicians = [];
    private readonly Dictionary<string, int> _customers = [];
    private readonly Dictionary<string, int> _devices = [];
    private readonly Dictionary<string, int> _orders = [];
    private readonly Dictionary<DemoActor, Actor> _actors = new() { [DemoActor.System] = Actor.System };

    private DemoClock Clock => services.GetRequiredService<DemoClock>();

    private DemoSession Session => services.GetRequiredService<DemoSession>();

    public async Task<DemoLoadOutcome> LoadAsync(DemoScenario scenario, DemoPasswords passwords, CancellationToken cancellationToken)
    {
        if (await IsCompleteAsync(scenario, cancellationToken))
        {
            return DemoLoadOutcome.AlreadyLoaded;
        }

        await EnsureEmptyAsync(cancellationToken);

        await CreateStaffAccountsAsync(scenario, passwords, cancellationToken);
        foreach (var item in scenario.Timeline())
        {
            Clock.Set(item.AtUtc);
            Session.Actor = _actors[item.By];
            await RunAsync(scenario, item, cancellationToken);
        }

        // Al final: si la carga se interrumpe antes, la base queda marcada como incompleta (ver IsCompleteAsync).
        Clock.Set(scenario.AnchorUtc);
        Session.Actor = Actor.System;
        var readOnly = await InScopeAsync<IUserAccountService, CommandResult<string>>(accounts => accounts.CreateReadOnlyAccountAsync(
            scenario.ReadOnly.Email, scenario.ReadOnly.FullName, passwords.ReadOnly, passwords.ReadOnly, cancellationToken));
        Ensure(readOnly, $"la cuenta {scenario.ReadOnly.Email}");
        return DemoLoadOutcome.Loaded;
    }

    /// <summary>La cuenta de consulta se crea al final de la carga: si existe, la demostración está completa.</summary>
    private Task<bool> IsCompleteAsync(DemoScenario scenario, CancellationToken cancellationToken) =>
        InScopeAsync<FixTrackDbContext, bool>(db => db.Users.AnyAsync(user => user.Email == scenario.ReadOnly.Email, cancellationToken));

    private async Task EnsureEmptyAsync(CancellationToken cancellationToken)
    {
        var used = await InScopeAsync<FixTrackDbContext, bool>(async db =>
            await db.Users.AnyAsync(cancellationToken)
            || await db.Technicians.AnyAsync(cancellationToken)
            || await db.Customers.AnyAsync(cancellationToken)
            || await db.ServiceOrders.AnyAsync(cancellationToken));
        if (used)
        {
            throw new DemoException(
                "La base no está vacía y no contiene una demostración completa: no se cargó nada. " +
                "Usa una base nueva (en Docker Compose: ./demo/demo.sh reiniciar).");
        }
    }

    private async Task CreateStaffAccountsAsync(DemoScenario scenario, DemoPasswords passwords, CancellationToken cancellationToken)
    {
        // El primer administrador, como lo crea el comando crear-administrador de FixTrack.
        Clock.Set(scenario.AccountsCreatedAtUtc);
        Session.Actor = Actor.System;
        var admin = await InScopeAsync<IUserAccountService, InitialAdministratorResult>(accounts => accounts.CreateInitialAdministratorAsync(
            scenario.Administrator.Email, scenario.Administrator.FullName, passwords.Administrator, cancellationToken));
        if (admin.Outcome != InitialAdministratorOutcome.Created)
        {
            throw new DemoException($"No se creó {scenario.Administrator.Email}: {string.Join(" ", admin.Errors)}");
        }

        var adminId = await InScopeAsync<FixTrackDbContext, string>(db =>
            db.Users.Where(user => user.Email == scenario.Administrator.Email).Select(user => user.Id).SingleAsync(cancellationToken));
        _actors[DemoActor.Administrator] = new Actor(adminId, scenario.Administrator.FullName);

        // El operador lo crea el administrador desde la administración de usuarios.
        Clock.Set(scenario.AccountsCreatedAtUtc.AddMinutes(20));
        Session.Actor = _actors[DemoActor.Administrator];
        var created = await InScopeAsync<IUserAccountService, CommandResult<string>>(accounts => accounts.CreateAsync(
            new NewUserAccountInput(scenario.Operator.FullName, scenario.Operator.Email, scenario.Operator.Role, passwords.Operator, passwords.Operator),
            adminId,
            cancellationToken));
        _actors[DemoActor.Operator] = new Actor(Ensure(created, $"la cuenta {scenario.Operator.Email}"), scenario.Operator.FullName);
    }

    private async Task RunAsync(DemoScenario scenario, DemoEvent item, CancellationToken cancellationToken)
    {
        switch (item)
        {
            case CreateTechnicianEvent { Technician: var technician }:
                _technicians[technician.Key] = Ensure(
                    await InScopeAsync<ITechnicianService, CommandResult<int>>(service => service.CreateAsync(
                        new TechnicianInput(technician.FullName, technician.Specialty, technician.Phone, technician.Email), cancellationToken)),
                    technician.FullName);
                break;

            case CreateCustomerEvent { Customer: var customer }:
                _customers[customer.Key] = Ensure(
                    await InScopeAsync<ICustomerService, CommandResult<int>>(service => service.CreateAsync(
                        new CustomerInput(customer.FullName, customer.Phone, customer.Email, customer.Notes), cancellationToken)),
                    customer.FullName);
                break;

            case RegisterDeviceEvent { Device: var device }:
                _devices[device.Key] = Ensure(
                    await InScopeAsync<IDeviceService, CommandResult<int>>(service => service.RegisterAsync(
                        new RegisterDeviceInput(
                            _customers[device.CustomerKey],
                            new DeviceInput(device.Type, device.Brand, device.Model, device.SerialNumber, device.Notes)),
                        cancellationToken)),
                    device.SerialNumber);
                break;

            case ReceiveOrderEvent { Order: var order }:
                _orders[order.Key] = Ensure(
                    await InScopeAsync<IServiceOrderService, CommandResult<int>>(service => service.CreateAsync(
                        new ServiceOrderInput(
                            _customers[scenario.DeviceOf(order).CustomerKey],
                            _devices[order.DeviceKey],
                            order.ReportedIssue,
                            order.Priority,
                            order.TechnicianKey is { } technicianKey ? _technicians[technicianKey] : null,
                            order.PromisedAtUtc,
                            order.EstimatedCost,
                            order.IntakeNotes),
                        cancellationToken)),
                    order.Key);
                break;

            case OrderStepEvent { Order: var order, Step: var step }:
                await ApplyStepAsync(order, step, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Operación desconocida: {item.GetType().Name}.");
        }
    }

    private async Task ApplyStepAsync(DemoOrder order, DemoOrderStep step, CancellationToken cancellationToken)
    {
        var id = _orders[order.Key];
        var result = await InScopeAsync<IServiceOrderService, CommandResult>(async service =>
        {
            // La versión que "ve" quien hace el cambio, como en el formulario de la aplicación.
            var version = (await service.GetAsync(id, cancellationToken))!.Version;
            return step switch
            {
                AssignStep assign => await service.AssignTechnicianAsync(id, _technicians[assign.TechnicianKey], assign.Note, version, cancellationToken),
                DiagnosisStep diagnosis => await service.RecordDiagnosisAsync(id, diagnosis.Text, version, cancellationToken),
                PlanStep plan => await service.UpdatePlanAsync(
                    id, new ServiceOrderPlanInput(plan.Priority, plan.PromisedAtUtc, plan.EstimatedCost), version, cancellationToken),
                StatusStep status => await service.ChangeStatusAsync(
                    id, new ServiceOrderStatusInput(status.Status, status.Note, status.FinalCost), version, cancellationToken),
                _ => throw new InvalidOperationException($"Paso desconocido: {step.GetType().Name}."),
            };
        });
        Ensure(result, $"{order.Key} ({step.GetType().Name} {step.AtUtc:yyyy-MM-dd HH:mm} UTC)");
    }

    /// <summary>Cada operación en su propio alcance, como una solicitud distinta de la aplicación web.</summary>
    private async Task<TResult> InScopeAsync<TService, TResult>(Func<TService, Task<TResult>> operation)
        where TService : notnull
    {
        await using var scope = services.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static T Ensure<T>(CommandResult<T> result, string what) => (Ensure((CommandResult)result, what) ? result.Value : default)!;

    private static bool Ensure(CommandResult result, string what)
    {
        if (result.Succeeded)
        {
            return true;
        }

        var details = result.Message ?? string.Join(" ", result.Errors.Select(error => $"{error.Key}: {error.Value}"));
        throw new DemoException($"FixTrack rechazó {what} ({result.Status}): {details}");
    }
}

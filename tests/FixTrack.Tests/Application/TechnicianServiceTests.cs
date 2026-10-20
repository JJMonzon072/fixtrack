using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Technicians;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Application;

/// <summary>Casos de uso de técnicos con un repositorio en memoria: sin base de datos.</summary>
public sealed class TechnicianServiceTests
{
    private readonly FakeAuditLog _audit = new();

    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly InMemoryStore _store = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly FakeTechnicianRepository _technicians;
    private readonly ITechnicianService _service;

    public TechnicianServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(_store);
        _technicians = new FakeTechnicianRepository(_store);
        _service = new ServiceCollection()
            .AddApplication()
            .AddSingleton<ITechnicianRepository>(_technicians)
            .AddSingleton<IUnitOfWork>(_unitOfWork)
            .AddSingleton<IAuditLog>(_audit)
            .AddSingleton<TimeProvider>(new FixedClock(Now))
            .BuildServiceProvider()
            .GetRequiredService<ITechnicianService>();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<int> CreateAsync(string name = "Luis Pérez", string? email = null)
    {
        var result = await _service.CreateAsync(new TechnicianInput(name, "Laptops y equipos de escritorio", null, email), Token);
        Assert.True(result.Succeeded);
        return result.Value;
    }

    [Fact]
    public async Task Registers_an_active_technician()
    {
        var result = await _service.CreateAsync(new TechnicianInput(" Luis  Pérez ", "Impresoras", "5555-0101", "LUIS@example.com"), Token);

        Assert.True(result.Succeeded);
        var technician = Assert.Single(_store.Technicians);
        Assert.True(technician.IsActive);
        Assert.Equal("Luis Pérez", technician.FullName);
        Assert.Equal("luis@example.com", technician.Email);
        Assert.Equal(Now.UtcDateTime, technician.CreatedAtUtc);
    }

    [Fact]
    public async Task Returns_every_validation_error_at_once_and_saves_nothing()
    {
        var result = await _service.CreateAsync(new TechnicianInput("", "", "12", "x@"), Token);

        Assert.Equal(
            [nameof(Technician.Email), nameof(Technician.FullName), nameof(Technician.Phone), nameof(Technician.Specialty)],
            result.Errors.Keys.Order());
        Assert.Empty(_store.Technicians);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Emails_are_unique_among_technicians()
    {
        await CreateAsync("Luis Pérez", "luis@example.com");

        var duplicate = await _service.CreateAsync(new TechnicianInput("Otro Luis", "Redes", null, "Luis@Example.com"), Token);

        Assert.Equal(TechnicianService.DuplicateEmailMessage, duplicate.Errors[nameof(Technician.Email)]);
    }

    [Fact]
    public async Task A_stale_edit_is_rejected_without_changes()
    {
        var id = await CreateAsync();
        Assert.True((await _service.UpdateAsync(id, new TechnicianInput("Luis Alberto Pérez", "Redes", null, null), version: 0, Token)).Succeeded);

        var stale = await _service.UpdateAsync(id, new TechnicianInput("Luis Desactualizado", "Impresoras", null, null), version: 0, Token);

        Assert.Equal(CommandStatus.Conflict, stale.Status);
        Assert.Equal(TechnicianService.StaleMessage, stale.Message);
        Assert.Equal(("Luis Alberto Pérez", 1), (_store.Technicians[0].FullName, _store.Technicians[0].Version));
    }

    [Fact]
    public async Task Edits_and_persists_changes()
    {
        var id = await CreateAsync(email: "luis@example.com");

        var result = await _service.UpdateAsync(id, new TechnicianInput("Luis Alberto Pérez", "Redes", "5555-0101", "luis@example.com"), version: 0, Token);

        Assert.True(result.Succeeded);
        Assert.Equal("Redes", _store.Technicians[0].Specialty);
        Assert.Equal(Now.UtcDateTime, _store.Technicians[0].UpdatedAtUtc);
    }

    [Fact]
    public async Task Deactivates_and_reactivates_without_deleting()
    {
        var id = await CreateAsync();

        Assert.True((await _service.DeactivateAsync(id, Token)).Succeeded);
        Assert.False(_store.Technicians[0].IsActive);
        Assert.Equal(CommandStatus.Conflict, (await _service.DeactivateAsync(id, Token)).Status);

        Assert.True((await _service.ReactivateAsync(id, Token)).Succeeded);
        Assert.True(_store.Technicians[0].IsActive);
        Assert.Equal(CommandStatus.Conflict, (await _service.ReactivateAsync(id, Token)).Status);
        Assert.Single(_store.Technicians);

        // Solo los cambios realizados quedan en la auditoría, no los intentos rechazados.
        Assert.Equal(
            [("tecnico.desactivado", id.ToString()), ("tecnico.reactivado", id.ToString())],
            _audit.Events.Select(entry => (entry.Action, entry.EntityId)));
        Assert.All(_audit.Events, entry => Assert.Equal("tecnico", entry.EntityType));
    }

    [Fact]
    public async Task A_technician_with_open_orders_cannot_be_deactivated()
    {
        var id = await CreateAsync();
        _store.OpenOrdersByTechnician[id] = 2;

        var result = await _service.DeactivateAsync(id, Token);

        Assert.Equal(CommandStatus.Conflict, result.Status);
        Assert.Contains("2 órdenes abiertas", result.Message);
        Assert.True(_store.Technicians[0].IsActive);
    }

    [Fact]
    public async Task Deactivation_counts_open_orders_with_the_technician_locked_in_a_transaction()
    {
        var id = await CreateAsync();
        _store.OpenOrdersByTechnician[id] = 1;

        var refused = await _service.DeactivateAsync(id, Token);
        _store.OpenOrdersByTechnician[id] = 0;
        var accepted = await _service.DeactivateAsync(id, Token);

        Assert.Contains("1 orden abierta", refused.Message);
        Assert.True(accepted.Succeeded);
        Assert.Equal([id, id], _store.LockedTechnicians);
        Assert.Equal(1, _unitOfWork.RolledBackTransactions);
        Assert.Equal(1, _unitOfWork.CommittedTransactions);
    }

    [Fact]
    public async Task Lists_every_technician_with_its_workload()
    {
        var luis = await CreateAsync("Luis Pérez");
        var ana = await CreateAsync("Ana Castillo");
        _store.OpenOrdersByTechnician[luis] = 3;

        var workload = await _service.ListWorkloadAsync(Token);

        Assert.Equal(["Ana Castillo", "Luis Pérez"], workload.Select(t => t.FullName));
        Assert.Equal([0, 3], workload.Select(t => t.OpenOrderCount));
        Assert.All(workload, t => Assert.True(t.IsActive));
        Assert.Equal(ana, workload[0].Id);
    }

    [Fact]
    public async Task Unknown_technicians_are_reported_as_not_found()
    {
        Assert.Equal(CommandStatus.NotFound, (await _service.UpdateAsync(9, new TechnicianInput("Luis Pérez", "Redes", null, null), version: 0, Token)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _service.DeactivateAsync(9, Token)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _service.ReactivateAsync(-1, Token)).Status);
    }

    [Fact]
    public async Task Search_criteria_are_normalized_before_querying()
    {
        await _service.SearchAsync(new TechnicianSearch(" luis ", (StatusFilter)9, Page: 0), Token);

        var search = _technicians.LastSearch!;
        Assert.Equal("luis", search.Text);
        Assert.Equal(StatusFilter.Active, search.Status);
        Assert.Equal(1, search.Page);
    }
}

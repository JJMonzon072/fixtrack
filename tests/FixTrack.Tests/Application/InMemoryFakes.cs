using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.Technicians;

namespace FixTrack.Tests.Application;

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Almacén en memoria compartido por los repositorios falsos. Permite probar los casos de uso sin base de datos.
/// </summary>
public sealed class InMemoryStore
{
    public List<Customer> Customers { get; } = [];

    public List<Device> Devices { get; } = [];

    public List<Technician> Technicians { get; } = [];

    public List<Entity> Pending { get; } = [];

    /// <summary>Órdenes abiertas por técnico (el módulo Órdenes todavía no existe).</summary>
    public Dictionary<int, int> OpenOrdersByTechnician { get; } = [];

    /// <summary>Indica si hay una transacción en curso (la abre <see cref="FakeUnitOfWork"/>).</summary>
    public bool InTransaction { get; set; }

    /// <summary>Técnicos bloqueados con FindForAssignmentAsync, en orden.</summary>
    public List<int> LockedTechnicians { get; } = [];

    /// <summary>Asigna el Id como lo haría la columna IDENTITY de SQL Server.</summary>
    public static void AssignId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
}

/// <summary>Simula SaveChanges: asigna Ids a los registros nuevos y puede simular un duplicado detectado por la base.</summary>
public sealed class FakeUnitOfWork(InMemoryStore store) : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public int CommittedTransactions { get; private set; }

    public int RolledBackTransactions { get; private set; }

    public bool FailNextSaveWithDuplicate { get; set; }

    public bool FailNextSaveWithConcurrencyConflict { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailNextSaveWithDuplicate)
        {
            FailNextSaveWithDuplicate = false;
            store.Pending.Clear();
            throw new DuplicateValueException("Duplicado", new InvalidOperationException());
        }

        if (FailNextSaveWithConcurrencyConflict)
        {
            FailNextSaveWithConcurrencyConflict = false;
            store.Pending.Clear();
            throw new ConcurrencyConflictException("Conflicto", new InvalidOperationException());
        }

        foreach (var entity in store.Pending)
        {
            switch (entity)
            {
                case Customer customer:
                    InMemoryStore.AssignId(customer, store.Customers.Count + 1);
                    store.Customers.Add(customer);
                    break;
                case Device device:
                    InMemoryStore.AssignId(device, store.Devices.Count + 1);
                    store.Devices.Add(device);
                    break;
                case Technician technician:
                    InMemoryStore.AssignId(technician, store.Technicians.Count + 1);
                    store.Technicians.Add(technician);
                    break;
            }
        }

        store.Pending.Clear();
        SaveCount++;
        return Task.CompletedTask;
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        where TResult : CommandResult
    {
        store.InTransaction = true;
        try
        {
            var result = await operation(cancellationToken);
            if (result.Succeeded)
            {
                CommittedTransactions++;
            }
            else
            {
                RolledBackTransactions++;
            }

            return result;
        }
        finally
        {
            store.InTransaction = false;
        }
    }
}

public sealed class FakeCustomerRepository(InMemoryStore store) : ICustomerRepository
{
    public CustomerSearch? LastSearch { get; private set; }

    public Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
    {
        LastSearch = search;
        return Task.FromResult(new CustomerSearchResult(new PagedResult<CustomerListItem>([], 1, CustomerSearch.PageSize, 0), 0, 0));
    }

    public Task<CustomerDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Customers.Where(c => c.Id == id)
            .Select(c => new CustomerDetails(c.Id, c.FullName, c.Phone, c.Email, c.Notes, c.IsActive, 0, c.CreatedAtUtc, c.UpdatedAtUtc, c.DeactivatedAtUtc))
            .SingleOrDefault());

    public Task<Customer?> FindAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Customers.SingleOrDefault(c => c.Id == id));

    public Task<bool> EmailExistsAsync(string email, int? excludingCustomerId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Customers.Any(c => c.Email == email && c.Id != excludingCustomerId));

    public Task<IReadOnlyList<CustomerOption>> SuggestActiveAsync(string? text, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerOption>>([.. store.Customers
            .Where(c => c.IsActive && (text is null || c.FullName.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => c.FullName)
            .Take(limit)
            .Select(c => new CustomerOption(c.Id, c.FullName, c.Phone, c.Email, c.IsActive))]);

    public Task<CustomerOption?> GetOptionAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Customers.Where(c => c.Id == id)
            .Select(c => new CustomerOption(c.Id, c.FullName, c.Phone, c.Email, c.IsActive))
            .SingleOrDefault());

    public void Add(Customer customer) => store.Pending.Add(customer);
}

public sealed class FakeDeviceRepository(InMemoryStore store) : IDeviceRepository
{
    public DeviceSearch? LastSearch { get; private set; }

    public Task<PagedResult<DeviceListItem>> SearchAsync(DeviceSearch search, CancellationToken cancellationToken)
    {
        LastSearch = search;
        return Task.FromResult(new PagedResult<DeviceListItem>([], 1, DeviceSearch.PageSize, 0));
    }

    public Task<DeviceDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) => Task.FromResult<DeviceDetails?>(null);

    public Task<IReadOnlyList<CustomerDeviceItem>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerDeviceItem>>([]);

    public Task<Device?> FindAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Devices.SingleOrDefault(d => d.Id == id));

    // Igual que SQL Server con la intercalación CI: sin distinguir mayúsculas.
    public Task<bool> SerialNumberExistsAsync(string brand, string serialNumber, int? excludingDeviceId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Devices.Any(d =>
            string.Equals(d.Brand, brand, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)
            && d.Id != excludingDeviceId));

    public void Add(Device device) => store.Pending.Add(device);
}

public sealed class FakeTechnicianRepository(InMemoryStore store) : ITechnicianRepository
{
    public TechnicianSearch? LastSearch { get; private set; }

    public Task<TechnicianSearchResult> SearchAsync(TechnicianSearch search, CancellationToken cancellationToken)
    {
        LastSearch = search;
        return Task.FromResult(new TechnicianSearchResult(new PagedResult<TechnicianListItem>([], 1, TechnicianSearch.PageSize, 0), 0, 0));
    }

    public Task<TechnicianDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) => Task.FromResult<TechnicianDetails?>(null);

    public Task<Technician?> FindAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Technicians.SingleOrDefault(t => t.Id == id));

    public Task<bool> EmailExistsAsync(string email, int? excludingTechnicianId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Technicians.Any(t => t.Email == email && t.Id != excludingTechnicianId));

    public Task<Technician?> FindForAssignmentAsync(int id, CancellationToken cancellationToken)
    {
        if (!store.InTransaction)
        {
            throw new InvalidOperationException("El bloqueo del técnico requiere una transacción activa.");
        }

        store.LockedTechnicians.Add(id);
        return FindAsync(id, cancellationToken);
    }

    public Task<int> CountOpenOrdersAsync(int technicianId, CancellationToken cancellationToken) =>
        Task.FromResult(store.OpenOrdersByTechnician.GetValueOrDefault(technicianId));

    public void Add(Technician technician) => store.Pending.Add(technician);
}

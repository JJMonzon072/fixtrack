using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Application.Devices;
using FixTrack.Application.ServiceOrders;
using FixTrack.Application.Technicians;
using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;
using FixTrack.Domain.Devices;
using FixTrack.Domain.ServiceOrders;
using FixTrack.Domain.Technicians;

namespace FixTrack.Tests.Application;

/// <summary>Registro de auditoría en memoria: guarda los eventos para comprobarlos.</summary>
public sealed class FakeAuditLog : IAuditLog
{
    public List<(string Action, string EntityType, string EntityId, string Summary)> Events { get; } = [];

    public void Record(string action, string entityType, string entityId, string summary) =>
        Events.Add((action, entityType, entityId, summary));
}

/// <summary>Usuario fijo que hace las operaciones en las pruebas de casos de uso.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public static FakeCurrentUser Instance { get; } = new();

    public Actor Actor { get; } = new("cuenta-de-prueba", "Ana Recepción");
}

/// <summary>Reloj fijo para pruebas deterministas; solo avanza cuando la prueba lo indica.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan time) => _now += time;
}

/// <summary>
/// Almacén en memoria compartido por los repositorios falsos. Permite probar los casos de uso sin base de datos.
/// </summary>
public sealed class InMemoryStore
{
    public List<Customer> Customers { get; } = [];

    public List<Device> Devices { get; } = [];

    public List<Technician> Technicians { get; } = [];

    public List<ServiceOrder> ServiceOrders { get; } = [];

    public List<Entity> Pending { get; } = [];

    /// <summary>Órdenes abiertas adicionales por técnico, para pruebas que no necesitan crear órdenes reales.</summary>
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
                case ServiceOrder order:
                    InMemoryStore.AssignId(order, store.ServiceOrders.Count + 1);
                    store.ServiceOrders.Add(order);
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
            .Select(c => new CustomerDetails(c.Id, c.FullName, c.Phone, c.Email, c.Notes, c.IsActive, 0, c.CreatedAtUtc, c.UpdatedAtUtc, c.DeactivatedAtUtc, c.Version))
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

    public Task<Device?> FindWithOwnerAsync(int id, CancellationToken cancellationToken) => FindAsync(id, cancellationToken);

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

    public Task<IReadOnlyList<TechnicianWorkload>> ListWorkloadAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TechnicianWorkload>>([.. store.Technicians
            .OrderBy(t => t.FullName)
            .Select(t => new TechnicianWorkload(t.Id, t.FullName, t.Specialty, t.IsActive, OpenOrders(t.Id)))]);

    public Task<int> CountOpenOrdersAsync(int technicianId, CancellationToken cancellationToken) =>
        Task.FromResult(OpenOrders(technicianId));

    private int OpenOrders(int technicianId) =>
        store.OpenOrdersByTechnician.GetValueOrDefault(technicianId)
        + store.ServiceOrders.Count(order => order.TechnicianId == technicianId && !order.IsClosed);

    public void Add(Technician technician) => store.Pending.Add(technician);
}

public sealed class FakeServiceOrderRepository(InMemoryStore store) : IServiceOrderRepository
{
    public ServiceOrderSearch? LastSearch { get; private set; }

    public DateTime? LastSearchNow { get; private set; }

    public Task<ServiceOrderSearchResult> SearchAsync(ServiceOrderSearch search, DateTime nowUtc, CancellationToken cancellationToken)
    {
        LastSearch = search;
        LastSearchNow = nowUtc;
        return Task.FromResult(new ServiceOrderSearchResult(
            new PagedResult<ServiceOrderListItem>([], 1, ServiceOrderSearch.PageSize, 0),
            new ServiceOrderViewCounts(0, 0, 0)));
    }

    public Task<ServiceOrderDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) => Task.FromResult<ServiceOrderDetails?>(null);

    public Task<CustomerServiceOrders> ListByCustomerAsync(int customerId, int take, CancellationToken cancellationToken) =>
        Task.FromResult(new CustomerServiceOrders(0, 0, []));

    /// <summary>Mismo orden que la consulta real, calculado en memoria con <see cref="WorkPrioritizer"/>.</summary>
    public Task<IReadOnlyList<OpenOrderSnapshot>> ListMostUrgentAsync(
        IReadOnlyCollection<ServiceOrderStatus> statuses,
        DateTime nowUtc,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OpenOrderSnapshot>>([.. WorkPrioritizer
            .Rank(
                store.ServiceOrders.Where(order => !order.IsClosed && statuses.Contains(order.Status)),
                order => WorkPrioritizer.Score(order.Priority, order.Status, order.ReceivedAtUtc, order.PromisedAtUtc, order.TechnicianId is not null, nowUtc).Total,
                order => order.PromisedAtUtc,
                order => order.ReceivedAtUtc,
                order => order.Id)
            .Take(take)
            .Select(order => new OpenOrderSnapshot(
                order.Id, order.Status, order.Priority, order.ReceivedAtUtc, order.PromisedAtUtc, order.TechnicianId,
                order.Technician?.FullName, order.Device!.Customer!.FullName, order.Device.Type, order.Device.Brand, order.Device.Model))]);

    public Task<IReadOnlyDictionary<ServiceOrderStatus, int>> CountOpenByStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<ServiceOrderStatus, int>>(store.ServiceOrders
            .Where(order => !order.IsClosed)
            .GroupBy(order => order.Status)
            .ToDictionary(group => group.Key, group => group.Count()));

    public Task<int?> FindIdByCreationRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(store.ServiceOrders.FirstOrDefault(order => order.CreationRequestId == requestId)?.Id);

    public Task<ServiceOrder?> FindAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(store.ServiceOrders.SingleOrDefault(order => order.Id == id));

    public void Add(ServiceOrder order) => store.Pending.Add(order);
}

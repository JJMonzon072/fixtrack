using FixTrack.Application;
using FixTrack.Application.Common;
using FixTrack.Application.Customers;
using FixTrack.Domain.Customers;
using Microsoft.Extensions.DependencyInjection;

namespace FixTrack.Tests.Application;

/// <summary>Casos de uso de clientes con un repositorio en memoria: sin base de datos.</summary>
public sealed class CustomerServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private readonly FakeCustomerRepository _repository = new();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly ICustomerService _service;

    public CustomerServiceTests()
    {
        _unitOfWork = new FakeUnitOfWork(_repository);
        _service = new ServiceCollection()
            .AddApplication()
            .AddSingleton<ICustomerRepository>(_repository)
            .AddSingleton<IUnitOfWork>(_unitOfWork)
            .AddSingleton<TimeProvider>(new FixedClock(Now))
            .BuildServiceProvider()
            .GetRequiredService<ICustomerService>();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_a_customer_with_normalized_data()
    {
        var result = await _service.CreateAsync(new("  Ana  López ", "5555-1234", " ANA@EXAMPLE.COM ", null), Token);

        Assert.True(result.Succeeded);
        var customer = Assert.Single(_repository.Items);
        Assert.Equal(result.Value, customer.Id);
        Assert.Equal("Ana López", customer.FullName);
        Assert.Equal("ana@example.com", customer.Email);
        Assert.Equal(Now.UtcDateTime, customer.CreatedAtUtc);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Returns_every_validation_error_at_once_and_saves_nothing()
    {
        var result = await _service.CreateAsync(new("", "12", "no-es-correo", new string('x', 501)), Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Equal(
            [nameof(Customer.Email), nameof(Customer.FullName), nameof(Customer.Notes), nameof(Customer.Phone)],
            result.Errors.Keys.Order());
        Assert.Empty(_repository.Items);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Rejects_an_email_that_belongs_to_another_customer()
    {
        await _service.CreateAsync(new("Ana López", "5555-1234", "ana@example.com", null), Token);

        var result = await _service.CreateAsync(new("Otra Ana", "5555-9999", "Ana@Example.com", null), Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.Contains("correo", result.Errors[nameof(Customer.Email)]);
    }

    [Fact]
    public async Task A_customer_can_keep_its_own_email_when_edited()
    {
        var created = await _service.CreateAsync(new("Ana López", "5555-1234", "ana@example.com", null), Token);

        var result = await _service.UpdateAsync(created.Value, new("Ana María López", "5555-1234", "ana@example.com", "Cliente frecuente"), Token);

        Assert.True(result.Succeeded);
        Assert.Equal("Ana María López", _repository.Items[0].FullName);
        Assert.Equal(Now.UtcDateTime, _repository.Items[0].UpdatedAtUtc);
    }

    [Fact]
    public async Task A_duplicate_detected_by_the_database_becomes_a_field_error()
    {
        _unitOfWork.FailNextSaveWithDuplicate = true;

        var result = await _service.CreateAsync(new("Ana López", "5555-1234", "ana@example.com", null), Token);

        Assert.Equal(CommandStatus.Invalid, result.Status);
        Assert.True(result.Errors.ContainsKey(nameof(Customer.Email)));
    }

    [Fact]
    public async Task Deactivates_and_reactivates_without_deleting()
    {
        var created = await _service.CreateAsync(new("Ana López", "5555-1234", null, null), Token);

        Assert.True((await _service.DeactivateAsync(created.Value, Token)).Succeeded);
        Assert.False(_repository.Items[0].IsActive);
        Assert.Equal(CommandStatus.Conflict, (await _service.DeactivateAsync(created.Value, Token)).Status);

        Assert.True((await _service.ReactivateAsync(created.Value, Token)).Succeeded);
        Assert.True(_repository.Items[0].IsActive);
        Assert.Single(_repository.Items);
    }

    [Fact]
    public async Task Unknown_customers_are_reported_as_not_found()
    {
        Assert.Equal(CommandStatus.NotFound, (await _service.UpdateAsync(99, new("Ana López", "5555-1234", null, null), Token)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _service.DeactivateAsync(99, Token)).Status);
        Assert.Equal(CommandStatus.NotFound, (await _service.ReactivateAsync(0, Token)).Status);
        Assert.Null(await _service.GetAsync(-1, Token));
    }

    [Fact]
    public async Task Search_criteria_are_normalized_before_querying()
    {
        await _service.SearchAsync(new CustomerSearch("   " + new string('a', 150), (CustomerStatusFilter)42, Page: -3), Token);

        var search = _repository.LastSearch!;
        Assert.Equal(CustomerSearch.TextMaxLength, search.Text!.Length);
        Assert.Equal(CustomerStatusFilter.Active, search.Status);
        Assert.Equal(1, search.Page);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeCustomerRepository : ICustomerRepository
    {
        public List<Customer> Items { get; } = [];

        public List<Customer> Pending { get; } = [];

        public CustomerSearch? LastSearch { get; private set; }

        public Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
        {
            LastSearch = search;
            return Task.FromResult(new CustomerSearchResult(new PagedResult<CustomerListItem>([], 1, CustomerSearch.PageSize, 0), 0, 0));
        }

        public Task<CustomerDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(c => c.Id == id)
                .Select(c => new CustomerDetails(c.Id, c.FullName, c.Phone, c.Email, c.Notes, c.IsActive, 0, c.CreatedAtUtc, c.UpdatedAtUtc, c.DeactivatedAtUtc))
                .SingleOrDefault());

        public Task<Customer?> FindAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(c => c.Id == id));

        public Task<bool> EmailExistsAsync(string email, int? excludingCustomerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(c => c.Email == email && c.Id != excludingCustomerId));

        public void Add(Customer customer) => Pending.Add(customer);
    }

    /// <summary>Simula el guardado: asigna Ids como lo haría la columna IDENTITY.</summary>
    private sealed class FakeUnitOfWork(FakeCustomerRepository repository) : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public bool FailNextSaveWithDuplicate { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            if (FailNextSaveWithDuplicate)
            {
                FailNextSaveWithDuplicate = false;
                repository.Pending.Clear();
                throw new DuplicateValueException("Duplicado", new InvalidOperationException());
            }

            foreach (var customer in repository.Pending)
            {
                typeof(Customer).BaseType!.GetProperty("Id")!.SetValue(customer, repository.Items.Count + 1);
                repository.Items.Add(customer);
            }

            repository.Pending.Clear();
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}

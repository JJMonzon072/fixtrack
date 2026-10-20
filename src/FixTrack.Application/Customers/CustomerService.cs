using FixTrack.Application.Common;
using FixTrack.Domain.Common;
using FixTrack.Domain.Customers;

namespace FixTrack.Application.Customers;

internal sealed class CustomerService(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICustomerService
{
    internal const string DuplicateEmailMessage = "Ya existe un cliente registrado con este correo electrónico.";
    internal const int SuggestionLimit = 8;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<CustomerSearchResult> SearchAsync(CustomerSearch search, CancellationToken cancellationToken)
    {
        var text = Text.CleanOrNull(search.Text);
        var normalized = search with
        {
            Text = text is { Length: > CustomerSearch.TextMaxLength } ? text[..CustomerSearch.TextMaxLength] : text,
            Status = Enum.IsDefined(search.Status) ? search.Status : StatusFilter.Active,
            Page = Math.Max(1, search.Page),
        };

        return customers.SearchAsync(normalized, cancellationToken);
    }

    public Task<CustomerDetails?> GetAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? customers.GetDetailsAsync(id, cancellationToken) : Task.FromResult<CustomerDetails?>(null);

    public Task<IReadOnlyList<CustomerOption>> SuggestActiveAsync(string? text, CancellationToken cancellationToken)
    {
        var cleaned = Text.CleanOrNull(text);
        if (cleaned is { Length: > CustomerSearch.TextMaxLength })
        {
            cleaned = cleaned[..CustomerSearch.TextMaxLength];
        }

        return customers.SuggestActiveAsync(cleaned, SuggestionLimit, cancellationToken);
    }

    public Task<CustomerOption?> GetOptionAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? customers.GetOptionAsync(id, cancellationToken) : Task.FromResult<CustomerOption?>(null);

    public async Task<CommandResult<int>> CreateAsync(CustomerInput input, CancellationToken cancellationToken)
    {
        var data = Normalize(input);
        var errors = await ValidateAsync(data, excludingCustomerId: null, cancellationToken);
        if (errors.Count > 0)
        {
            return CommandResult<int>.Invalid(errors);
        }

        var customer = new Customer(data.FullName!, data.Phone!, data.Email, data.Notes, UtcNow);
        customers.Add(customer);

        if (!await TrySaveAsync(cancellationToken))
        {
            return CommandResult<int>.Invalid(EmailTaken());
        }

        return CommandResult<int>.Success(customer.Id);
    }

    public async Task<CommandResult> UpdateAsync(int id, CustomerInput input, CancellationToken cancellationToken)
    {
        var customer = await FindAsync(id, cancellationToken);
        if (customer is null)
        {
            return CommandResult.NotFound();
        }

        var data = Normalize(input);
        var errors = await ValidateAsync(data, excludingCustomerId: id, cancellationToken);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        customer.UpdateDetails(data.FullName!, data.Phone!, data.Email, data.Notes, UtcNow);
        return await TrySaveAsync(cancellationToken) ? CommandResult.Success() : CommandResult.Invalid(EmailTaken());
    }

    public async Task<CommandResult> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        var customer = await FindAsync(id, cancellationToken);
        if (customer is null)
        {
            return CommandResult.NotFound();
        }

        if (!customer.IsActive)
        {
            return CommandResult.Conflict("El cliente ya estaba inactivo.");
        }

        customer.Deactivate(UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CommandResult.Success();
    }

    public async Task<CommandResult> ReactivateAsync(int id, CancellationToken cancellationToken)
    {
        var customer = await FindAsync(id, cancellationToken);
        if (customer is null)
        {
            return CommandResult.NotFound();
        }

        if (customer.IsActive)
        {
            return CommandResult.Conflict("El cliente ya estaba activo.");
        }

        customer.Reactivate(UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CommandResult.Success();
    }

    private Task<Customer?> FindAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? customers.FindAsync(id, cancellationToken) : Task.FromResult<Customer?>(null);

    private static CustomerInput Normalize(CustomerInput input) => new(
        Text.Clean(input.FullName),
        Text.Clean(input.Phone),
        ContactRules.NormalizeEmail(input.Email),
        Text.CleanMultilineOrNull(input.Notes));

    private async Task<IReadOnlyDictionary<string, string>> ValidateAsync(
        CustomerInput data,
        int? excludingCustomerId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(
            CustomerRules.Validate(data.FullName!, data.Phone!, data.Email, data.Notes));

        if (data.Email is not null
            && !errors.ContainsKey(nameof(Customer.Email))
            && await customers.EmailExistsAsync(data.Email, excludingCustomerId, cancellationToken))
        {
            errors[nameof(Customer.Email)] = DuplicateEmailMessage;
        }

        return errors;
    }

    /// <summary>Guarda los cambios. Devuelve <c>false</c> si otro registro tomó el mismo correo entre la validación y el guardado.</summary>
    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DuplicateValueException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> EmailTaken() =>
        new() { [nameof(Customer.Email)] = DuplicateEmailMessage };
}

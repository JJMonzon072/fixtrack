using FixTrack.Application.Common;
using FixTrack.Domain.Common;
using FixTrack.Domain.Technicians;

namespace FixTrack.Application.Technicians;

internal sealed class TechnicianService(
    ITechnicianRepository technicians,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ITechnicianService
{
    internal const string DuplicateEmailMessage = "Ya existe un técnico registrado con este correo electrónico.";

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public Task<TechnicianSearchResult> SearchAsync(TechnicianSearch search, CancellationToken cancellationToken)
    {
        var text = Text.CleanOrNull(search.Text);
        var normalized = search with
        {
            Text = text is { Length: > TechnicianSearch.TextMaxLength } ? text[..TechnicianSearch.TextMaxLength] : text,
            Status = Enum.IsDefined(search.Status) ? search.Status : StatusFilter.Active,
            Page = Math.Max(1, search.Page),
        };

        return technicians.SearchAsync(normalized, cancellationToken);
    }

    public Task<TechnicianDetails?> GetAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? technicians.GetDetailsAsync(id, cancellationToken) : Task.FromResult<TechnicianDetails?>(null);

    public Task<IReadOnlyList<TechnicianWorkload>> ListWorkloadAsync(CancellationToken cancellationToken) =>
        technicians.ListWorkloadAsync(cancellationToken);

    public async Task<CommandResult<int>> CreateAsync(TechnicianInput input, CancellationToken cancellationToken)
    {
        var data = Normalize(input);
        var errors = await ValidateAsync(data, excludingTechnicianId: null, cancellationToken);
        if (errors.Count > 0)
        {
            return CommandResult<int>.Invalid(errors);
        }

        var technician = new Technician(data.FullName!, data.Specialty!, data.Phone, data.Email, UtcNow);
        technicians.Add(technician);

        if (!await TrySaveAsync(cancellationToken))
        {
            return CommandResult<int>.Invalid(EmailTaken());
        }

        return CommandResult<int>.Success(technician.Id);
    }

    public async Task<CommandResult> UpdateAsync(int id, TechnicianInput input, CancellationToken cancellationToken)
    {
        var technician = await FindAsync(id, cancellationToken);
        if (technician is null)
        {
            return CommandResult.NotFound();
        }

        var data = Normalize(input);
        var errors = await ValidateAsync(data, excludingTechnicianId: id, cancellationToken);
        if (errors.Count > 0)
        {
            return CommandResult.Invalid(errors);
        }

        technician.UpdateDetails(data.FullName!, data.Specialty!, data.Phone, data.Email, UtcNow);
        return await TrySaveAsync(cancellationToken) ? CommandResult.Success() : CommandResult.Invalid(EmailTaken());
    }

    public Task<CommandResult> DeactivateAsync(int id, CancellationToken cancellationToken) =>
        id <= 0
            ? Task.FromResult(CommandResult.NotFound())
            : unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    // Bloquea al técnico: una asignación simultánea espera a que esta operación termine
                    // (y después lo encuentra inactivo), o termina antes y su orden se cuenta abajo.
                    var technician = await technicians.FindForAssignmentAsync(id, token);
                    if (technician is null)
                    {
                        return CommandResult.NotFound();
                    }

                    if (!technician.IsActive)
                    {
                        return CommandResult.Conflict("El técnico ya estaba inactivo.");
                    }

                    // Un trabajo en curso no puede quedarse sin responsable: primero hay que reasignar sus órdenes abiertas.
                    var openOrders = await technicians.CountOpenOrdersAsync(id, token);
                    if (openOrders > 0)
                    {
                        return CommandResult.Conflict(openOrders == 1
                            ? "No se puede desactivar: el técnico tiene 1 orden abierta asignada. Reasígnala antes de desactivarlo."
                            : $"No se puede desactivar: el técnico tiene {openOrders} órdenes abiertas asignadas. Reasígnalas antes de desactivarlo.");
                    }

                    technician.Deactivate(UtcNow);
                    await unitOfWork.SaveChangesAsync(token);
                    return CommandResult.Success();
                },
                cancellationToken);

    public async Task<CommandResult> ReactivateAsync(int id, CancellationToken cancellationToken)
    {
        var technician = await FindAsync(id, cancellationToken);
        if (technician is null)
        {
            return CommandResult.NotFound();
        }

        if (technician.IsActive)
        {
            return CommandResult.Conflict("El técnico ya estaba activo.");
        }

        technician.Reactivate(UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CommandResult.Success();
    }

    private Task<Technician?> FindAsync(int id, CancellationToken cancellationToken) =>
        id > 0 ? technicians.FindAsync(id, cancellationToken) : Task.FromResult<Technician?>(null);

    private static TechnicianInput Normalize(TechnicianInput input) => new(
        Text.Clean(input.FullName),
        Text.Clean(input.Specialty),
        Text.CleanOrNull(input.Phone),
        ContactRules.NormalizeEmail(input.Email));

    private async Task<IReadOnlyDictionary<string, string>> ValidateAsync(
        TechnicianInput data,
        int? excludingTechnicianId,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>(
            TechnicianRules.Validate(data.FullName!, data.Specialty!, data.Phone, data.Email));

        if (data.Email is not null
            && !errors.ContainsKey(nameof(Technician.Email))
            && await technicians.EmailExistsAsync(data.Email, excludingTechnicianId, cancellationToken))
        {
            errors[nameof(Technician.Email)] = DuplicateEmailMessage;
        }

        return errors;
    }

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
        new() { [nameof(Technician.Email)] = DuplicateEmailMessage };
}

namespace FixTrack.Application.Common;

public enum CommandStatus
{
    Succeeded,
    Invalid,
    NotFound,
    Conflict,
}

/// <summary>Resultado de una operación que modifica datos.</summary>
public record CommandResult(CommandStatus Status, IReadOnlyDictionary<string, string> Errors, string? Message)
{
    private static readonly IReadOnlyDictionary<string, string> NoErrors = new Dictionary<string, string>();

    public bool Succeeded => Status == CommandStatus.Succeeded;

    public static CommandResult Success() => new(CommandStatus.Succeeded, NoErrors, null);

    public static CommandResult Invalid(IReadOnlyDictionary<string, string> errors) => new(CommandStatus.Invalid, errors, null);

    public static CommandResult NotFound() => new(CommandStatus.NotFound, NoErrors, null);

    public static CommandResult Conflict(string message) => new(CommandStatus.Conflict, NoErrors, message);
}

/// <summary>Resultado de una operación que, si tiene éxito, devuelve un valor (por ejemplo, el Id creado).</summary>
public sealed record CommandResult<T>(CommandStatus Status, IReadOnlyDictionary<string, string> Errors, string? Message, T? Value)
    : CommandResult(Status, Errors, Message)
{
    public static CommandResult<T> Success(T value) => new(CommandStatus.Succeeded, new Dictionary<string, string>(), null, value);

    public static new CommandResult<T> Invalid(IReadOnlyDictionary<string, string> errors) => new(CommandStatus.Invalid, errors, null, default);
}

namespace FixTrack.Domain.Common;

/// <summary>
/// Quién hizo una operación. Se copia en el momento: si la cuenta cambia de nombre después, el registro conserva el de
/// entonces. <see cref="UserId"/> es el identificador de la cuenta de acceso (sin clave foránea: los registros de
/// auditoría no dependen del ciclo de vida de las cuentas).
/// </summary>
public sealed record Actor
{
    public const int UserIdMaxLength = 450;
    public const int NameMaxLength = 150;

    public Actor(string? userId, string? name)
    {
        var cleanId = Text.CleanOrNull(userId);
        var cleanName = Text.CleanOrNull(name);
        if (cleanName is null)
        {
            throw new DomainException("Falta el nombre de quien realiza la operación.");
        }

        if (cleanId is { Length: > UserIdMaxLength })
        {
            throw new DomainException("El identificador de la cuenta es demasiado largo.");
        }

        UserId = cleanId;
        Name = cleanName.Length > NameMaxLength ? cleanName[..NameMaxLength] : cleanName;
    }

    public string? UserId { get; }

    public string Name { get; }

    /// <summary>Operaciones que no inicia una persona (comandos de consola).</summary>
    public static Actor System { get; } = new(null, "Sistema");
}

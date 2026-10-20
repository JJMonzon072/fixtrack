namespace FixTrack.Domain.Common;

/// <summary>
/// Clase base de las entidades del dominio (clientes, equipos, técnicos, órdenes de servicio).
/// Una entidad se distingue por su identidad, no por el valor de sus atributos: dos instancias
/// son iguales cuando son del mismo tipo y comparten el mismo identificador asignado.
/// </summary>
public abstract class Entity : IEquatable<Entity>
{
    /// <summary>Identificador único de la entidad. Vale 0 mientras no ha sido persistida.</summary>
    public int Id { get; protected set; }

    /// <summary>Indica si la entidad todavía no tiene un identificador asignado.</summary>
    public bool IsTransient => Id == 0;

    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Las entidades sin identificador solo son iguales a sí mismas.
        return GetType() == other.GetType()
            && !IsTransient
            && !other.IsTransient
            && Id == other.Id;
    }

    public override bool Equals(object? obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() =>
        IsTransient ? base.GetHashCode() : HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) => Equals(left, right);

    public static bool operator !=(Entity? left, Entity? right) => !Equals(left, right);
}

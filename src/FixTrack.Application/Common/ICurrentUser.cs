using FixTrack.Domain.Common;

namespace FixTrack.Application.Common;

/// <summary>
/// Quién hace la operación en curso, para los registros de historial y auditoría. La implementa la capa web a partir
/// de la sesión; fuera de una solicitud (comandos de consola) es <see cref="Actor.System"/>.
/// </summary>
public interface ICurrentUser
{
    Actor Actor { get; }
}

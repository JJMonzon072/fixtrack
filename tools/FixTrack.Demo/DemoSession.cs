using FixTrack.Application.Common;
using FixTrack.Domain.Common;

namespace FixTrack.Demo;

/// <summary>
/// Quién hace cada operación de la carga: una de las cuentas ficticias (o "Sistema" para lo que en producción se hace
/// desde la consola). Así el historial y la auditoría muestran autores como los de un taller real.
/// </summary>
internal sealed class DemoSession : ICurrentUser
{
    public Actor Actor { get; set; } = Actor.System;
}

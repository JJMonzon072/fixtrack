# FixTrack: referencia para el equipo

Resumen para preparar los diagramas, los manuales y la exposición. Cada sección indica dónde está el detalle completo.

## En una frase

FixTrack es una aplicación web para un taller de reparación de computadoras. Registra clientes, equipos, técnicos y órdenes de servicio, sigue cada orden desde que el equipo llega hasta que se entrega e indica qué trabajo conviene atender primero.

## Actores y roles

| Actor | Usa el sistema | Qué puede hacer |
| --- | --- | --- |
| **Administrador** | Sí | Todo lo del Operador, más gestionar técnicos y cuentas de acceso |
| **Operador** (recepción o personal del taller) | Sí | Panel, cola de trabajo, clientes, equipos y todas las operaciones de órdenes; consultar técnicos |
| **Cliente** | No | Entrega y retira el equipo. Sus datos los registra el personal |
| **Técnico** | No, por ahora | Recibe órdenes asignadas. Sus datos los registra un administrador. Un rol Técnico con acceso propio queda propuesto para otra fase |

## Casos de uso (para el diagrama de casos de uso)

**Operador y Administrador**

- Iniciar y cerrar sesión; cambiar la propia contraseña.
- Consultar el panel general y la cola de trabajo priorizada.
- Registrar, buscar, editar, desactivar y reactivar clientes.
- Registrar, buscar y editar equipos de un cliente.
- Consultar técnicos y su carga de trabajo.
- Registrar una orden de servicio.
- Buscar y filtrar órdenes (vencidas, sin asignar, por estado, prioridad o técnico).
- Asignar o reasignar un técnico.
- Registrar el diagnóstico.
- Cambiar prioridad, fecha comprometida y costo estimado.
- Cambiar el estado de una orden; entregarla con su costo final o cancelarla con un motivo.
- Consultar el historial de una orden.

**Solo Administrador**

- Registrar, editar, desactivar y reactivar técnicos.
- Crear cuentas de acceso, cambiar su rol, deshabilitarlas, habilitarlas y restablecer su contraseña.

**Fuera de la interfaz:** crear el primer administrador y aplicar migraciones, con comandos de consola.

## Datos principales (para los diagramas entidad-relación y de clases)

```
Cliente 1 ──── 0..* Equipo 1 ──── 0..* OrdenDeServicio 1 ──── 1..* RegistroDeHistorial
                                          0..* │
                                               │ 0..1
                                            Técnico
```

- Un **cliente** tiene varios equipos. Un **equipo** pertenece siempre al mismo cliente.
- Un equipo puede tener varias **órdenes** a lo largo del tiempo.
- Una orden puede no tener **técnico** al principio; después tiene uno (se puede reasignar).
- Cada cambio de estado deja un **registro de historial** que no se puede editar ni borrar.
- Las **cuentas de acceso** (tablas de Identity) son independientes de los técnicos.

Tablas, columnas y diagramas listos para copiar: [modelo-de-datos.md](modelo-de-datos.md).

## Estados de una orden (para el diagrama de estados)

```
Recibida → En diagnóstico → En reparación ⇄ En espera de repuestos
                  │               │
                  └──────→ Lista para entrega → Entregada
                                  └→ En reparación (reapertura)
Cancelada: desde cualquier estado abierto, excepto Lista para entrega.
```

- En diagnóstico y En reparación necesitan un técnico asignado.
- Para salir de En diagnóstico hay que registrar el diagnóstico.
- En espera de repuestos y Cancelada piden una nota.
- Entregada pide el costo final.
- Entregada y Cancelada son finales: la orden ya no cambia.

Diagrama completo y reglas: [flujo-de-ordenes.md](flujo-de-ordenes.md).

## Algoritmo de priorización (para la exposición)

```
puntuación = prioridad + bono por fecha de entrega + antigüedad

prioridad:  Baja 10 · Normal 30 · Alta 60 · Urgente 90
fecha:      vencida +50 · vence en ≤ 24 h +30 · en 24 a 72 h +15 · otro caso 0 (solo uno)
antigüedad: +2 por día en el taller, máximo +30
```

- Empates: fecha comprometida más cercana, luego la que llegó primero, luego el número de orden.
- El estado no suma puntos: separa las órdenes en tres listas (reparaciones, en espera de repuestos, listas para entrega) y define la acción recomendada.
- Ejemplo: una orden urgente, recibida hace 3 días, que vence hoy: 90 + 30 + 6 = **126**.
- Es **determinista** (mismos datos, mismo resultado) y **explicable** (la pantalla muestra de dónde sale cada punto).

Justificación de cada valor: [flujo-de-ordenes.md, sección 5](flujo-de-ordenes.md#5-algoritmo-de-priorización).

## Pantallas (para el manual de usuario)

| Pantalla | Menú | Para qué sirve |
| --- | --- | --- |
| Inicio de sesión | — | Entrar con correo y contraseña |
| Panel general | Panel (primera opción) | Ver de un vistazo cuántas órdenes hay en cada estado, cuáles vencen, qué hacer primero y la carga de cada técnico |
| Cola de trabajo | Operación → Cola de trabajo | Lista de trabajo ordenada por el algoritmo, con la acción recomendada |
| Órdenes de servicio | Operación → Órdenes de servicio | Buscar y filtrar órdenes; registrar una nueva |
| Detalle de una orden | Desde el listado | Datos, siguientes pasos, costos y línea de tiempo; botones para asignar, diagnosticar, planificar y cambiar de estado |
| Clientes | Gestión → Clientes | Registrar y buscar clientes; ver sus equipos y órdenes |
| Equipos | Gestión → Equipos | Registrar y buscar equipos; ver sus órdenes |
| Técnicos | Gestión → Técnicos | Consultar técnicos y su carga; el administrador los registra y desactiva |
| Usuarios (cuentas de acceso) | Administración → Usuarios | Solo administradores: crear cuentas y asignar roles |
| Cambiar contraseña | Menú de usuario (arriba a la derecha) | Cambiar la propia contraseña |

Las capturas de pantalla de la revisión de QA están en `qa-evidence/fase-3/`, una carpeta local que no se sube a GitHub.

## Reglas de negocio más importantes

1. No se registran órdenes para clientes inactivos ni se asignan técnicos inactivos.
2. El equipo de una orden debe pertenecer al cliente elegido.
3. La fecha comprometida no puede ser anterior a la recepción.
4. Los costos están en quetzales, no son negativos y tienen como máximo dos decimales.
5. Las transiciones de estado siguen el flujo anterior; una orden cerrada no cambia.
6. El historial no se puede modificar ni borrar.
7. No se puede desactivar a un técnico con órdenes abiertas.
8. Si dos personas modifican la misma orden a la vez, la segunda recibe un aviso en lugar de sobrescribir el cambio.
9. Clientes, técnicos y cuentas no se eliminan: se desactivan o deshabilitan.
10. Siempre queda al menos un administrador habilitado.

## Tecnologías

| Área | Tecnología |
| --- | --- |
| Lenguaje y plataforma | C# 14, .NET 10 (LTS) |
| Aplicación web | ASP.NET Core MVC con vistas Razor |
| Base de datos | SQL Server 2022 (Somee), Entity Framework Core 10 con migraciones |
| Seguridad | ASP.NET Core Identity, cookies seguras, roles, protección CSRF, CSP |
| Interfaz | HTML, CSS y JavaScript propios, sin frameworks; íconos Lucide; tema claro y oscuro |
| Pruebas | xUnit v3: 480 pruebas (unitarias, de integración y 7 opcionales contra SQL Server) |
| Integración continua | GitHub Actions: compila, prueba y construye la imagen Docker |
| Despliegue (preparado) | Docker, previsto para Render |

## Arquitectura en una imagen

```
Presentación (FixTrack.Web)          controladores, vistas, seguridad
        │
Negocio (FixTrack.Application)       casos de uso e interfaces
        │
Dominio (FixTrack.Domain)            entidades, reglas, estados, priorización
        ▲
Datos (FixTrack.Infrastructure)      EF Core, SQL Server, cuentas
```

Las flechas apuntan hacia el dominio: las reglas del negocio no dependen de la base de datos ni de la web. Detalle: [arquitectura.md](arquitectura.md).

## Guion sugerido para la demostración (unos 10 minutos)

1. Iniciar sesión como Operador y mostrar el panel.
2. Registrar una orden urgente con fecha de entrega para hoy.
3. Abrir la cola de trabajo: la orden aparece arriba con su puntuación desglosada.
4. Asignar un técnico, iniciar el diagnóstico, registrarlo y pasar a reparación.
5. Pasar a lista para entrega y entregar con el costo final; mostrar la línea de tiempo.
6. Intentar desactivar a un técnico con órdenes abiertas: el sistema lo impide y explica por qué.
7. Mostrar que el Operador no ve Administración. Iniciar sesión como Administrador y mostrar las cuentas de acceso.
8. Cerrar con la arquitectura por capas, el algoritmo y las pruebas automáticas en GitHub Actions.

## Glosario

| Término | Significado |
| --- | --- |
| Orden de servicio | Registro de una reparación de un equipo, identificado como `OS-000042` |
| Entrega comprometida | Fecha y hora en que el taller prometió entregar el equipo |
| Orden vencida | Orden abierta cuya entrega comprometida ya pasó |
| Orden abierta / cerrada | Abierta: sigue en el taller. Cerrada: entregada o cancelada |
| Cola de trabajo | Lista de órdenes abiertas ordenada por el algoritmo de priorización |
| Carga de trabajo | Cantidad de órdenes abiertas asignadas a un técnico |
| Migración | Cambio versionado del esquema de la base de datos |

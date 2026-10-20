# Modelo de datos de FixTrack

Este documento describe las tablas de la base de datos FixTrackDB (SQL Server), sus relaciones y las reglas que las protegen. Sirve como referencia para preparar los diagramas entidad-relación y de clases del proyecto.

El esquema se crea con las migraciones de Entity Framework Core de `src/FixTrack.Infrastructure/Persistence/Migrations/`:

| Migración | Fase | Cambios |
| --- | --- | --- |
| `20261020031151_InitialCreate` | 2A | Crea las cinco tablas, sus claves, restricciones, índices y el trigger del historial |
| `20261020045452_TechnicianSpecialtyAndDeviceSerialRules` | 2B | `Technicians.Specialty` pasa a ser obligatoria (`NOT NULL` y no vacía); `Devices.SerialNumber`, si existe, no puede ser una cadena vacía |
| `20261020060713_ServiceOrderWorkflowFields` | 3 | Agrega a `ServiceOrders` las observaciones de recepción, la fecha de asignación del técnico y la versión para la concurrencia; dos restricciones nuevas y un índice por fecha de recepción |
| `20261020071400_IdentityAndDataProtectionKeys` | 3 | Tablas de ASP.NET Core Identity (cuentas y roles), los roles Administrador y Operador, y la tabla `DataProtectionKeys` |
| `20261020162649_ServiceOrderAuthorsAndAssignments` | 4 | Autor (`ChangedByUserId`, `ChangedByName`, que admiten `NULL`) en el historial de estados; tabla `ServiceOrderAssignments` con su trigger de solo inserción |
| `20261020164604_AuditEvents` | 4 | Tabla `AuditEvents` con su trigger de solo inserción |
| `20261020165523_RecordVersions` | 4 | Columna `Version` (`int NOT NULL DEFAULT 0`) en `Customers`, `Devices` y `Technicians` |
| `20261020165922_OrderCreationRequests` | 4 | Columna `ServiceOrders.CreationRequestId` (`uniqueidentifier NULL`) con índice único filtrado |
| `20261020174027_BackfillCurrentAssignments` | 4 | Solo inserta: registra en `ServiceOrderAssignments` la asignación vigente de las órdenes que ya tenían técnico, con su fecha real y sin autor |

Ninguna migración borra tablas, columnas, índices ni datos, ni actualiza filas existentes: una prueba (`MigrationTests`) rechaza `DROP TABLE`, `DROP COLUMN`, `DROP INDEX`, `TRUNCATE`, `DELETE FROM` y `UPDATE` en el script completo. Las de las fases 3 y 4 solo agregan columnas que admiten `NULL` o tienen valor inicial, restricciones que los datos existentes ya cumplían, tablas nuevas y filas nuevas. Antes de aplicarlas a FixTrackDB se revisó su script SQL y se ensayaron sobre una copia restaurada de la base real; después de aplicarlas, la base real coincidió tabla por tabla con el ensayo. FixTrackDB tiene aplicadas las nueve migraciones.

Las entidades de C# están en `src/FixTrack.Domain/` y su configuración de base de datos en `src/FixTrack.Infrastructure/Persistence/Configurations/`.

## Diagrama entidad-relación

Los tamaños exactos de cada columna figuran en las tablas de la sección siguiente.

```mermaid
erDiagram
    Customers ||--o{ Devices : "posee"
    Devices ||--o{ ServiceOrders : "recibe"
    Technicians |o--o{ ServiceOrders : "atiende"
    ServiceOrders ||--|{ ServiceOrderStatusHistory : "registra"
    ServiceOrders ||--o{ ServiceOrderAssignments : "asigna"
    Technicians ||--o{ ServiceOrderAssignments : "recibe"

    Customers {
        int Id PK
        nvarchar FullName
        varchar Phone
        nvarchar Email "único si existe"
        nvarchar Notes
        bit IsActive
        datetime2 CreatedAtUtc
        datetime2 UpdatedAtUtc
        datetime2 DeactivatedAtUtc
        int Version "concurrencia"
    }

    Devices {
        int Id PK
        int CustomerId FK
        int Type "1 a 7"
        nvarchar Brand
        nvarchar Model
        varchar SerialNumber "único por marca si existe"
        nvarchar Notes
        datetime2 CreatedAtUtc
        datetime2 UpdatedAtUtc
        int Version "concurrencia"
    }

    Technicians {
        int Id PK
        nvarchar FullName
        varchar Phone
        nvarchar Email "único si existe"
        nvarchar Specialty "obligatoria"
        bit IsActive
        datetime2 CreatedAtUtc
        datetime2 UpdatedAtUtc
        datetime2 DeactivatedAtUtc
        int Version "concurrencia"
    }

    ServiceOrders {
        int Id PK
        int DeviceId FK
        int TechnicianId FK "opcional"
        int Status "1 a 7"
        int Priority "1 a 4"
        nvarchar ReportedIssue
        nvarchar Diagnosis
        nvarchar IntakeNotes
        decimal EstimatedCost
        decimal FinalCost
        datetime2 ReceivedAtUtc
        datetime2 PromisedAtUtc
        datetime2 DeliveredAtUtc
        datetime2 TechnicianAssignedAtUtc
        datetime2 CreatedAtUtc
        datetime2 UpdatedAtUtc
        int Version "concurrencia"
        uniqueidentifier CreationRequestId "único si existe"
    }

    ServiceOrderStatusHistory {
        int Id PK
        int ServiceOrderId FK
        int FromStatus "null en el registro inicial"
        int ToStatus
        datetime2 ChangedAtUtc
        nvarchar Note
        nvarchar ChangedByUserId "autor"
        nvarchar ChangedByName "autor"
    }

    ServiceOrderAssignments {
        int Id PK
        int ServiceOrderId FK
        int FromTechnicianId FK "null en la primera asignación"
        int ToTechnicianId FK
        datetime2 AssignedAtUtc
        nvarchar AssignedByUserId "autor"
        nvarchar AssignedByName "autor"
        nvarchar Note
    }
```

### Auditoría

`AuditEvents` no tiene claves foráneas: identifica el registro afectado por su tipo y su Id (en texto, porque las cuentas usan GUID) y guarda el nombre del autor, para que el evento siga siendo legible aunque el registro o la cuenta cambien.

```mermaid
erDiagram
    AuditEvents {
        int Id PK
        datetime2 OccurredAtUtc
        nvarchar UserId "null si lo hizo el sistema"
        nvarchar UserName
        varchar Action "por ejemplo orden.estado"
        varchar EntityType "orden, tecnico, cliente o cuenta"
        varchar EntityId
        nvarchar Summary
    }
```

### Cuentas de acceso

Las tablas de ASP.NET Core Identity son independientes de las del taller: ninguna clave foránea las une con clientes, equipos, técnicos u órdenes. Solo se muestran las tablas que FixTrack usa; `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` y `AspNetRoleClaims` existen porque Identity las crea, pero están vacías.

```mermaid
erDiagram
    AspNetUsers ||--o{ AspNetUserRoles : "tiene"
    AspNetRoles ||--o{ AspNetUserRoles : "se asigna en"

    AspNetUsers {
        nvarchar Id PK
        nvarchar FullName
        nvarchar Email "único"
        nvarchar UserName "igual al correo"
        nvarchar PasswordHash
        nvarchar SecurityStamp
        datetimeoffset LockoutEnd
        int AccessFailedCount
        datetime2 CreatedAtUtc
        datetime2 LastSignInAtUtc
    }

    AspNetRoles {
        nvarchar Id PK
        nvarchar Name "Administrador u Operador"
    }

    AspNetUserRoles {
        nvarchar UserId PK, FK
        nvarchar RoleId PK, FK
    }

    DataProtectionKeys {
        int Id PK
        nvarchar FriendlyName
        nvarchar Xml
    }
```

## Relaciones y cardinalidades

| Relación | Cardinalidad | Clave foránea | Al eliminar el registro principal |
| --- | --- | --- | --- |
| Un cliente posee equipos | Customers 1 — 0..N Devices | `Devices.CustomerId` (obligatoria) | Se impide (`NO ACTION`) |
| Un equipo recibe órdenes de servicio en distintos momentos | Devices 1 — 0..N ServiceOrders | `ServiceOrders.DeviceId` (obligatoria) | Se impide (`NO ACTION`) |
| Un técnico atiende órdenes; una orden puede no tener técnico todavía | Technicians 0..1 — 0..N ServiceOrders | `ServiceOrders.TechnicianId` (opcional) | Se impide (`NO ACTION`) |
| Cada orden conserva su historial de estados | ServiceOrders 1 — 1..N ServiceOrderStatusHistory | `ServiceOrderStatusHistory.ServiceOrderId` (obligatoria) | Se impide (`NO ACTION`) |
| Cada orden conserva sus asignaciones de técnico | ServiceOrders 1 — 0..N ServiceOrderAssignments | `ServiceOrderAssignments.ServiceOrderId` (obligatoria) | Se impide (`NO ACTION`) |
| Una asignación va a un técnico y, si es una reasignación, viene de otro | Technicians 1 — 0..N ServiceOrderAssignments (dos veces) | `ToTechnicianId` (obligatoria) y `FromTechnicianId` (opcional) | Se impide (`NO ACTION`) |

Ninguna relación del taller borra en cascada. No pueden existir equipos sin cliente, órdenes sin equipo ni historial sin orden. Tampoco se puede eliminar un cliente, equipo, técnico u orden que tenga registros dependientes.

En las tablas de Identity, las claves foráneas sí borran en cascada (por ejemplo, `AspNetUserRoles` → `AspNetUsers`), porque así las define ASP.NET Core Identity. FixTrack no elimina cuentas: las deshabilita. Una prueba verifica que la cascada solo exista en tablas `AspNet*`.

## Tablas

### Customers (clientes)

Personas o empresas que traen equipos al taller. Un cliente **no se elimina**: se desactiva (`IsActive = 0`) y conserva su historial.

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `FullName` | nvarchar(150), intercalación `Modern_Spanish_100_CI_AI` | No | Nombre completo o razón social. La búsqueda no distingue mayúsculas ni tildes |
| `Phone` | varchar(20) | No | De 7 a 15 dígitos; admite `+` inicial, espacios, guiones y paréntesis |
| `Email` | nvarchar(254) | Sí | En minúsculas. Único entre clientes cuando existe |
| `Notes` | nvarchar(500) | Sí | Observaciones del taller |
| `IsActive` | bit | No | Estado del cliente |
| `CreatedAtUtc` | datetime2(3) | No | Fecha de registro (UTC) |
| `UpdatedAtUtc` | datetime2(3) | Sí | Última modificación (UTC) |
| `DeactivatedAtUtc` | datetime2(3) | Sí | Fecha de la desactivación vigente (UTC) |
| `Version` | int | No | Aumenta con cada cambio; token de concurrencia optimista (fase 4, valor inicial 0) |

Restricciones: `CK_Customers_FullName_NotEmpty`, `CK_Customers_Phone_NotEmpty`, `CK_Customers_Email_Format` (formato `usuario@dominio.ext`), `CK_Customers_Deactivation` (activo ⇔ sin fecha de desactivación).

Índices: `UX_Customers_Email` (único, filtrado `WHERE Email IS NOT NULL`), `IX_Customers_IsActive_FullName` (listado por estado ordenado por nombre), `IX_Customers_Phone`.

### Devices (equipos)

Computadoras, laptops y otros dispositivos de un cliente.

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `CustomerId` | int | No | Cliente propietario (FK) |
| `Type` | int | No | 1 Laptop · 2 Escritorio · 3 Todo en uno · 4 Tableta · 5 Impresora · 6 Monitor · 7 Otro |
| `Brand` | nvarchar(60) | No | Marca |
| `Model` | nvarchar(100) | Sí | Modelo |
| `SerialNumber` | varchar(100) | Sí | Número de serie del fabricante |
| `Notes` | nvarchar(500) | Sí | Estado físico, accesorios entregados |
| `CreatedAtUtc`, `UpdatedAtUtc` | datetime2(3) | No / Sí | Fechas de registro y modificación (UTC) |
| `Version` | int | No | Token de concurrencia optimista (fase 4) |

Restricciones: `CK_Devices_Type` (1 a 7), `CK_Devices_Brand_NotEmpty`, `CK_Devices_SerialNumber_NotEmpty` (la serie es `NULL` o tiene texto; una cadena vacía chocaría con otras en el índice único).

El número de serie se guarda en mayúsculas y solo admite letras sin tilde, números, espacios, guiones, puntos y barras (la columna es `varchar`). Su unicidad por marca no distingue mayúsculas: `Dell / dl-01` y `DELL / DL-01` son el mismo equipo.

Índices: `IX_Devices_CustomerId`, `UX_Devices_Brand_SerialNumber` (único por marca, filtrado `WHERE SerialNumber IS NOT NULL`).

### Technicians (técnicos)

Personal técnico del taller. Igual que los clientes, se desactiva en lugar de eliminarse.

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `FullName` | nvarchar(150), `Modern_Spanish_100_CI_AI` | No | Nombre completo |
| `Phone` | varchar(20) | Sí | Teléfono |
| `Email` | nvarchar(254) | Sí | Único entre técnicos cuando existe |
| `Specialty` | nvarchar(100) | No | Especialidad, de 3 a 100 caracteres |
| `IsActive`, `CreatedAtUtc`, `UpdatedAtUtc`, `DeactivatedAtUtc`, `Version` | | | Igual que en Customers |

Restricciones: `CK_Technicians_FullName_NotEmpty`, `CK_Technicians_Specialty_NotEmpty`, `CK_Technicians_Email_Format`, `CK_Technicians_Deactivation`.

Índices: `UX_Technicians_Email` (único filtrado), `IX_Technicians_IsActive_FullName`.

### ServiceOrders (órdenes de servicio)

Cada reparación de un equipo. El número visible (`OS-000042`) se calcula a partir del `Id` y no se guarda.

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `DeviceId` | int | No | Equipo reparado (FK) |
| `TechnicianId` | int | Sí | Técnico asignado (FK). Puede asignarse después |
| `Status` | int | No | 1 Recibida · 2 En diagnóstico · 3 En reparación · 4 En espera de repuestos · 5 Lista para entrega · 6 Entregada · 7 Cancelada |
| `Priority` | int | No | 1 Baja · 2 Normal · 3 Alta · 4 Urgente |
| `ReportedIssue` | nvarchar(1000) | No | Problema reportado por el cliente |
| `IntakeNotes` | nvarchar(500) | Sí | Observaciones de recepción: estado físico, accesorios entregados (fase 3) |
| `Diagnosis` | nvarchar(2000) | Sí | Diagnóstico técnico |
| `EstimatedCost` | decimal(10,2) | Sí | Costo estimado, en quetzales |
| `FinalCost` | decimal(10,2) | Sí | Costo final, en quetzales. Solo existe en órdenes entregadas |
| `ReceivedAtUtc` | datetime2(3) | No | Recepción del equipo. La fija el servidor |
| `PromisedAtUtc` | datetime2(3) | Sí | Entrega comprometida con el cliente |
| `DeliveredAtUtc` | datetime2(3) | Sí | Entrega efectiva |
| `TechnicianAssignedAtUtc` | datetime2(3) | Sí | Última asignación o reasignación del técnico (fase 3) |
| `CreatedAtUtc`, `UpdatedAtUtc` | datetime2(3) | No / Sí | Fechas de registro y modificación (UTC) |
| `Version` | int | No | Aumenta con cada cambio. Es el token de concurrencia optimista: detecta cuando dos personas modifican la misma orden (fase 3) |
| `CreationRequestId` | uniqueidentifier | Sí | Identificador del formulario que registró la orden: un segundo envío del mismo formulario no crea otra (fase 4). Las órdenes anteriores no lo tienen |

Restricciones:

- `CK_ServiceOrders_Status` (1 a 7) y `CK_ServiceOrders_Priority` (1 a 4).
- `CK_ServiceOrders_ReportedIssue_NotEmpty`.
- `CK_ServiceOrders_EstimatedCost` y `CK_ServiceOrders_FinalCost` (no negativos).
- `CK_ServiceOrders_PromisedAt` (la entrega comprometida no es anterior a la recepción).
- `CK_ServiceOrders_Delivery` (existe fecha de entrega solo si el estado es Entregada).
- `CK_ServiceOrders_FinalCost_OnDelivery` (fase 3): una orden entregada tiene costo final y las demás no lo tienen.
- `CK_ServiceOrders_TechnicianAssignment` (fase 3): el técnico y su fecha de asignación existen juntos o faltan juntos.

Índices: `IX_ServiceOrders_DeviceId`, `IX_ServiceOrders_TechnicianId`, `IX_ServiceOrders_Status_PromisedAtUtc` (filtros por estado de la cola, el panel y los conteos; vencidas y próximas entregas), `IX_ServiceOrders_ReceivedAtUtc` (órdenes recientes y antigüedad, fase 3) y `UX_ServiceOrders_CreationRequestId` (único, filtrado `WHERE CreationRequestId IS NOT NULL`, fase 4).

### ServiceOrderStatusHistory (historial de estados)

Un registro por cada cambio de estado de una orden, incluido el inicial (`FromStatus = NULL`, `ToStatus = 1`).

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `ServiceOrderId` | int | No | Orden (FK) |
| `FromStatus` | int | Sí | Estado anterior |
| `ToStatus` | int | No | Estado nuevo |
| `ChangedAtUtc` | datetime2(3) | No | Momento del cambio (UTC) |
| `Note` | nvarchar(500) | Sí | Comentario del cambio |
| `ChangedByUserId` | nvarchar(450) | Sí | Cuenta que hizo el cambio (fase 4). `NULL` en los registros anteriores y en los del sistema |
| `ChangedByName` | nvarchar(150) | Sí | Nombre del autor en ese momento (fase 4) |

Restricciones: `CK_ServiceOrderStatusHistory_ToStatus` (1 a 7), `CK_ServiceOrderStatusHistory_FromStatus` (1 a 7 y distinto del nuevo estado).

Índice: `IX_ServiceOrderStatusHistory_ServiceOrderId_ChangedAtUtc`.

Trigger `TR_ServiceOrderStatusHistory_AppendOnly`: rechaza cualquier `UPDATE` o `DELETE` sobre la tabla con el error 50001. El historial es de solo inserción. Una prueba contra SQL Server real lo verifica dentro de una transacción revertida.

### ServiceOrderAssignments (asignaciones de técnico, fase 4)

Un registro por cada asignación o reasignación de técnico. La orden sigue guardando el técnico actual y la fecha de su última asignación; esta tabla conserva la secuencia completa.

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `ServiceOrderId` | int | No | Orden (FK) |
| `FromTechnicianId` | int | Sí | Técnico anterior (FK). `NULL` en la primera asignación |
| `ToTechnicianId` | int | No | Técnico asignado (FK) |
| `AssignedAtUtc` | datetime2(3) | No | Momento de la asignación (UTC) |
| `AssignedByUserId`, `AssignedByName` | nvarchar(450), nvarchar(150) | Sí | Autor. `NULL` en las asignaciones reconstruidas por la migración |
| `Note` | nvarchar(200) | Sí | Observación o motivo de la reasignación |

Restricción: `CK_ServiceOrderAssignments_DifferentTechnician` (en una reasignación, el técnico nuevo es distinto del anterior). Índices: `IX_ServiceOrderAssignments_ServiceOrderId_AssignedAtUtc`, `IX_ServiceOrderAssignments_FromTechnicianId`, `IX_ServiceOrderAssignments_ToTechnicianId`.

Trigger `TR_ServiceOrderAssignments_AppendOnly`: rechaza `UPDATE` y `DELETE`.

### AuditEvents (auditoría, fase 4)

Operaciones importantes: quién, qué y cuándo. Se escribe en la misma transacción que el cambio y nunca incluye contraseñas.

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `Id` | int, IDENTITY | No | Clave primaria |
| `OccurredAtUtc` | datetime2(3) | No | Momento (UTC) |
| `UserId` | nvarchar(450) | Sí | Cuenta que hizo la operación; `NULL` si fue un comando de consola |
| `UserName` | nvarchar(150) | No | Nombre del autor en ese momento ("Sistema" para los comandos) |
| `Action` | varchar(40) | No | Operación, por ejemplo `orden.tecnico-reasignado`, `cliente.desactivado` o `cuenta.desbloqueada` |
| `EntityType` | varchar(20) | No | `orden`, `tecnico`, `cliente` o `cuenta` |
| `EntityId` | varchar(64) | No | Id del registro afectado |
| `Summary` | nvarchar(400) | No | Descripción legible (`CK_AuditEvents_Summary_NotEmpty`) |

Índices: `IX_AuditEvents_OccurredAtUtc` (página de auditoría) e `IX_AuditEvents_EntityType_EntityId_OccurredAtUtc` (actividad de una orden en su línea de tiempo). Trigger `TR_AuditEvents_AppendOnly`: rechaza `UPDATE` y `DELETE`.

### AspNetUsers, AspNetRoles y AspNetUserRoles (cuentas de acceso, fase 3)

Tablas estándar de ASP.NET Core Identity. FixTrack agrega tres columnas a `AspNetUsers`:

| Columna | Tipo | Nulo | Descripción |
| --- | --- | --- | --- |
| `FullName` | nvarchar(150) | No | Nombre que se muestra en la interfaz (`CK_AspNetUsers_FullName_NotEmpty`) |
| `CreatedAtUtc` | datetime2(3) | No | Fecha de creación de la cuenta |
| `LastSignInAtUtc` | datetime2(3) | Sí | Último inicio de sesión correcto |

- El correo es el usuario: `Email` y `UserName` tienen el mismo valor y son únicos (`UserNameIndex`).
- La contraseña se guarda solo como hash (`PasswordHash`, PBKDF2 de Identity).
- **Cuenta deshabilitada:** `LockoutEnd` = `9999-12-31 23:59:59.9999999 +00:00`. Se distingue del bloqueo temporal por intentos fallidos, que dura 15 minutos. Al deshabilitar se renueva `SecurityStamp`, lo que cierra las sesiones abiertas en menos de un minuto.
- `AspNetRoles` se crea con dos filas de identificador fijo: **Administrador** y **Operador**. Cada cuenta tiene exactamente un rol.

### DataProtectionKeys (fase 3)

Claves de ASP.NET Core Data Protection, que protegen la cookie de sesión y los tokens antifalsificación. Se guardan en la base para que las sesiones sigan siendo válidas tras reiniciar o volver a desplegar el contenedor, que no tiene disco persistente. Data Protection crea y rota las claves por sí solo; nadie debe editarlas. Desde la fase 4, en producción el XML de cada clave nueva guarda su material cifrado con el certificado configurado (`encryptedSecret`); las claves anteriores siguen sin cifrar hasta que el comando `proteger-claves --revocar-sin-cifrar` las revoca (agrega una revocación, no borra filas).

## Reglas del negocio implementadas

| Regla | Dónde se aplica |
| --- | --- |
| Nombre: 3 a 150 caracteres, al menos dos letras, sin símbolos extraños | Dominio (`ContactRules`) |
| Teléfono: 7 a 15 dígitos, `+` solo al inicio | Dominio (`ContactRules`) |
| Correo: formato válido, en minúsculas, único entre clientes | Dominio, aplicación y base de datos (índice único) |
| Los clientes se desactivan y reactivan; no se eliminan | Dominio (`Customer.Deactivate/Reactivate`), restricción `CK_Customers_Deactivation` y claves foráneas |
| Un equipo se registra solo a nombre de un cliente **activo**; los equipos de un cliente inactivo se conservan y pueden consultarse | Dominio (`Customer.RegisterDevice`) y aplicación (`DeviceService`) |
| El propietario de un equipo no cambia: su historial de reparaciones queda vinculado al equipo | Dominio (`Device` no expone un cambio de propietario; `UpdateDetails` solo modifica datos técnicos) |
| Marca obligatoria; modelo, serie y observaciones opcionales con longitud limitada; serie única por marca | Dominio (`DeviceRules`), aplicación e índice `UX_Devices_Brand_SerialNumber` |
| Técnico: nombre y especialidad obligatorios; teléfono y correo opcionales con formato válido; correo único | Dominio (`TechnicianRules`), aplicación e índice `UX_Technicians_Email` |
| Los técnicos se desactivan y reactivan; no se eliminan | Dominio (`Technician.Deactivate/Reactivate`) y restricción `CK_Technicians_Deactivation` |
| No se puede desactivar un técnico con órdenes abiertas (ni entregadas ni canceladas) asignadas; primero hay que reasignarlas | Aplicación (`TechnicianService.DeactivateAsync`) |
| La desactivación y una asignación simultánea no pueden cruzarse: ambas bloquean la fila del técnico (`UPDLOCK`) dentro de una transacción | Aplicación (`TechnicianService`, `ServiceOrderService`) e infraestructura (`TechnicianRepository.FindForAssignmentAsync`) |
| Solo un técnico activo puede recibir asignaciones | Dominio (`ServiceOrder.AssignTechnician`) |
| Una orden solo se registra para un equipo existente cuyo propietario es el cliente elegido, y ese cliente debe estar activo | Dominio (constructor de `ServiceOrder`) y aplicación (`ServiceOrderService.CreateAsync`) |
| Toda orden nace como Recibida y registra ese estado en el historial | Dominio (`ServiceOrder`) |
| Las transiciones de estado siguen el flujo definido; En diagnóstico y En reparación exigen técnico; salir del diagnóstico exige diagnóstico; En espera de repuestos y Cancelada exigen nota; Entregada exige costo final | Dominio (`ServiceOrderWorkflow`, `ServiceOrder.ChangeStatus`) y restricciones `CK_ServiceOrders_FinalCost_OnDelivery` y `CK_ServiceOrders_Delivery` |
| Una orden entregada o cancelada ya no cambia | Dominio (`ServiceOrder`) |
| La entrega comprometida no es anterior a la recepción ni está a más de 365 días; una nueva fecha no puede quedar en el pasado | Dominio (`ServiceOrderRules`, `ServiceOrder.UpdatePlan`) y `CK_ServiceOrders_PromisedAt` |
| Montos en quetzales con dos decimales, no negativos, hasta 99 999 999.99 | Dominio y restricciones CHECK |
| Dos cambios simultáneos sobre la misma orden, cliente, equipo o técnico no se sobrescriben | Dominio (`Version`), aplicación (comparación con la versión del formulario) y EF Core (token de concurrencia) |
| Un mismo formulario no registra dos órdenes | Aplicación (`ServiceOrderService.CreateAsync`) e índice `UX_ServiceOrders_CreationRequestId` |
| Todas las fechas se guardan en UTC | Dominio (rechaza fechas que no son UTC) y conversor de EF Core |
| El historial de estados, las asignaciones y la auditoría no se modifican ni se eliminan; cada cambio y su registro se guardan juntos, con su autor | Triggers, claves foráneas `NO ACTION` y dominio (solo agrega registros en el mismo guardado) |
| Solo los administradores gestionan técnicos y cuentas y consultan la auditoría; no se puede deshabilitar la propia cuenta ni cambiar el propio rol; siempre queda al menos un administrador habilitado, también con cambios simultáneos | Web (políticas de autorización) e infraestructura (`UserAccountService` con el bloqueo de aplicación `FixTrack:cuentas`) |

## Notas para los diagramas

- **Diagrama de clases:** las entidades `Customer`, `Device`, `Technician`, `ServiceOrder`, `ServiceOrderStatusChange`, `ServiceOrderAssignment` y `AuditEvent` heredan de `Entity`, que aporta `Id` y la igualdad por identidad. `ServiceOrderStatusChange` es la clase que corresponde a la tabla `ServiceOrderStatusHistory`. `Actor` (Id y nombre del autor, o el sistema) es un objeto de valor que no tiene tabla propia: sus datos se copian en cada registro.
- **Enumeraciones:** `DeviceType`, `ServiceOrderStatus` y `ServiceOrderPriority` se guardan como enteros. Sus valores están en las tablas anteriores. `WorkQueue` y `RecommendedAction` no se guardan: los calcula el algoritmo de priorización.
- **Clases de reglas sin estado** (estáticas): `ServiceOrderWorkflow` (transiciones), `ServiceOrderRules` (validaciones), `ServiceOrderSchedule` (orden vencida) y `WorkPrioritizer` (puntuación y desempates). Se explican en [flujo-de-ordenes.md](flujo-de-ordenes.md).
- **Cuentas:** `FixTrackUser` (en Infrastructure) hereda de `IdentityUser` y agrega `FullName`, `CreatedAtUtc` y `LastSignInAtUtc`. Los nombres de los roles están en `Roles` (Application).
- **Tablas técnicas:** `__EFMigrationsHistory` (migraciones aplicadas) y `DataProtectionKeys` no forman parte del modelo del negocio. `AuditEvents` registra el uso del sistema, no datos del taller.

### Diagrama de clases del dominio

```mermaid
classDiagram
    direction LR
    class Entity {
        <<abstract>>
        +int Id
    }
    class Customer {
        +string FullName
        +string Phone
        +string? Email
        +bool IsActive
        +UpdateDetails()
        +Deactivate()
        +Reactivate()
        +RegisterDevice() Device
    }
    class Device {
        +DeviceType Type
        +string Brand
        +string? Model
        +string? SerialNumber
        +UpdateDetails()
    }
    class Technician {
        +string FullName
        +string Specialty
        +bool IsActive
        +UpdateDetails()
        +Deactivate()
        +Reactivate()
    }
    class ServiceOrder {
        +string Number
        +ServiceOrderStatus Status
        +ServiceOrderPriority Priority
        +string ReportedIssue
        +string? Diagnosis
        +decimal? EstimatedCost
        +decimal? FinalCost
        +DateTime? PromisedAtUtc
        +int Version
        +bool IsClosed
        +decimal? CostVariance
        +AssignTechnician()
        +RecordDiagnosis()
        +UpdatePlan()
        +ChangeStatus()
        +ValidatePlanChange()
        +ValidateStatusChange()
    }
    class ServiceOrderStatusChange {
        +ServiceOrderStatus? FromStatus
        +ServiceOrderStatus ToStatus
        +DateTime ChangedAtUtc
        +string? Note
        +string? ChangedByName
    }
    class ServiceOrderAssignment {
        +int? FromTechnicianId
        +int ToTechnicianId
        +DateTime AssignedAtUtc
        +string? AssignedByName
        +string? Note
    }
    class AuditEvent {
        +DateTime OccurredAtUtc
        +string UserName
        +string Action
        +string EntityType
        +string EntityId
        +string Summary
    }
    class ServiceOrderWorkflow {
        <<static>>
        +NextStatuses()
        +CanTransition()
        +TransitionBlocker()
    }
    class WorkPrioritizer {
        <<static>>
        +Score() WorkPriorityScore
        +Rank()
    }
    Entity <|-- Customer
    Entity <|-- Device
    Entity <|-- Technician
    Entity <|-- ServiceOrder
    Entity <|-- ServiceOrderStatusChange
    Entity <|-- ServiceOrderAssignment
    Entity <|-- AuditEvent
    Customer "1" --> "0..*" Device : posee
    Device "1" --> "0..*" ServiceOrder : recibe
    Technician "0..1" --> "0..*" ServiceOrder : atiende
    ServiceOrder "1" *-- "1..*" ServiceOrderStatusChange : historial
    ServiceOrder "1" *-- "0..*" ServiceOrderAssignment : asignaciones
    ServiceOrder ..> ServiceOrderWorkflow : valida transiciones
    WorkPrioritizer ..> ServiceOrder : puntúa órdenes abiertas
```

- **Operaciones del dominio:**
  - `Customer`: `UpdateDetails`, `Deactivate`, `Reactivate`, `RegisterDevice`.
  - `Device`: `UpdateDetails`. Se crea únicamente con `Customer.RegisterDevice`.
  - `Technician`: `UpdateDetails`, `Deactivate`, `Reactivate`.
  - `ServiceOrder`: se crea con su equipo (cuyo propietario debe estar activo), quién la registra y, opcionalmente, un técnico activo. Operaciones: `AssignTechnician` (con autor y motivo), `RecordDiagnosis`, `UpdatePlan`, `ChangeStatus` (con autor). `ValidatePlanChange` y `ValidateStatusChange` revisan un cambio sin aplicarlo, para mostrar los errores por campo.
- **Estado de la implementación:** todas las tablas se usan desde la interfaz: Clientes, Equipos, Técnicos, Órdenes de servicio con su historial y sus asignaciones, Cuentas de acceso y Auditoría.

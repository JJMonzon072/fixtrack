# Demostración y presentación de FixTrack

Cómo levantar FixTrack en tu equipo con datos ficticios, qué mostrar en cinco minutos y qué explicar en una entrevista técnica.

FixTrack es un proyecto integrador universitario, ampliado como portafolio: no es un producto comercial ni tiene clientes reales. La demostración usa solo datos inventados y una base de datos local creada para ella.

## Contenido

1. [Ejecutar la demostración](#1-ejecutar-la-demostración)
2. [Recorrido de cinco minutos](#2-recorrido-de-cinco-minutos)
3. [Qué destacar en una entrevista](#3-qué-destacar-en-una-entrevista)
4. [Preguntas probables](#4-preguntas-probables)
5. [Descripciones listas para usar](#5-descripciones-listas-para-usar)
6. [Mejoras posibles](#6-mejoras-posibles)

## 1. Ejecutar la demostración

### Requisitos

- **Docker con Compose v2:** Docker Desktop o Docker Engine.
- **Memoria:** SQL Server pide al menos 2 GB para Docker. En Docker Desktop se ajusta en *Settings → Resources*.
- **Mac con Apple Silicon:** la imagen de SQL Server solo existe para x86-64. Docker Desktop la emula con Rosetta (*Settings → General → Use Rosetta for x86_64/amd64 emulation on Apple Silicon*). Se probó en un MacBook Pro con M4.
- **Bash y curl,** incluidos en macOS y Linux. En Windows, WSL o Git Bash; o Docker Compose directamente (ver más abajo).
- **Navegador:** probado con Chrome y Edge. La cookie de sesión es `Secure`, como en producción, y un navegador que no trate `http://localhost` como origen seguro no la guarda (Safari puede ser el caso).

No hace falta .NET ni una cuenta en ningún servicio: las imágenes se construyen a partir del código.

### Iniciar

```bash
git clone https://github.com/JJMonzon072/fixtrack.git
cd fixtrack
./demo/demo.sh iniciar
```

La primera vez descarga las imágenes base (la de SQL Server ocupa unos 1.7 GB) y compila FixTrack. Tiempos medidos:

- **GitHub Actions** (runner x86 sin nada en caché): 89 s, de la descarga a la carga de los datos.
- **MacBook Pro M4:** compilar las imágenes sin caché tomó unos 3 minutos, sin contar las descargas. Con las imágenes ya construidas, iniciar, volver a iniciar o recrear el entorno tomó entre 28 y 42 segundos.

El script genera `demo/.env` con contraseñas aleatorias (permisos 600, ignorado por Git) y levanta estos servicios, en orden:

| Servicio | Qué hace |
| --- | --- |
| `sqlserver` | SQL Server 2022 Developer, la misma imagen fijada en CI. No publica puertos: solo lo ven los demás servicios |
| `preparar` | Crea la base `FixTrackDemo` y el login `fixtrack_demo`, dueño solo de esa base. También genera el certificado de Data Protection del entorno |
| `migraciones` | Ejecuta `aplicar-migraciones` con la imagen de producción de FixTrack |
| `datos` | Crea las cuentas y carga el taller ficticio con los casos de uso de la aplicación |
| `app` | FixTrack en http://localhost:8080, solo en `127.0.0.1`, con los tres extras de portafolio habilitados |

Al terminar, el script muestra algo así:

```text
Datos ficticios cargados con los servicios de FixTrack.
4 técnicos, 10 clientes, 15 equipos y 25 órdenes: recibida 2, en diagnóstico 2, en reparación 2, en espera de repuestos 2, lista para entrega 2, entregada 13, cancelada 2.

FixTrack (demostración local): http://localhost:8080

  Rol            Correo                            Contraseña
  Administrador  admin@fixtrack-demo.invalid       (generada para este entorno)
  Operador       operador@fixtrack-demo.invalid    (generada para este entorno)
  Consulta       consulta@fixtrack-demo.invalid    (generada para este entorno)
```

### Las cuentas

| Cuenta | Puede | Para mostrar |
| --- | --- | --- |
| Administrador (Marta Solís) | Todo: también técnicos, cuentas y auditoría | El recorrido completo |
| Operador (Valeria Ortiz) | Clientes, equipos, órdenes, reportes, fichas y API | Que no ve Administración: `/usuarios` y `/auditoria` responden 403 |
| Consulta (Visitante) | Ver todas las pantallas, sin guardar nada | Que cualquier envío responde 403, aunque el formulario esté a la vista |

### Comandos

| Comando | Efecto |
| --- | --- |
| `./demo/demo.sh iniciar` | Crea el entorno la primera vez; después lo vuelve a abrir sin recargar los datos |
| `./demo/demo.sh credenciales` | Muestra la dirección, las cuentas y sus contraseñas |
| `./demo/demo.sh verificar` | Comprueba con curl que la base responde, que los extras están activos y los permisos de cada rol |
| `./demo/demo.sh estado` | Muestra los contenedores |
| `./demo/demo.sh detener` | Detiene todo y conserva los datos y las contraseñas |
| `./demo/demo.sh reiniciar` | Borra los datos, el certificado y las contraseñas, y crea un entorno nuevo |
| `./demo/demo.sh eliminar` | Borra contenedores, volúmenes, `demo/.env` y las imágenes de la demostración (conserva la imagen de SQL Server) |

`verificar` también se ejecuta en GitHub Actions: un trabajo levanta la demostración en un runner limpio, la verifica, la reinicia sin perder datos y la elimina.

### El taller ficticio

- **Personas:** 4 técnicos, cada uno con su especialidad, y 10 clientes: personas, un taller gráfico, una distribuidora y un colegio.
- **Equipos:** 15 entre laptops, computadoras de escritorio, todo en uno, impresoras, un monitor y una tableta.
- **Órdenes:** 25 de los últimos 75 días, en los siete estados:
  - 13 entregadas, 4 de ellas con atraso;
  - 6 con un costo final distinto del estimado;
  - 2 vencidas y 2 sin técnico entre las abiertas;
  - 3 reasignaciones, 3 cambios de planificación y una orden reabierta antes de entregarse.
- **Fechas:** se calculan a partir del momento de la carga, así que la demostración siempre luce actual. Con la misma fecha de carga, el resultado es idéntico.
- **Marcas de datos ficticios:** los nombres terminan en "(demo)", los correos usan el dominio reservado `.invalid`, los teléfonos empiezan con `0000` y las series con `DEMO-`.

La carga no inserta filas en la base: ejecuta cada operación con los casos de uso de FixTrack (`tools/FixTrack.Demo`), con la fecha y el autor de cada paso. Por eso cada historial, cada evento de auditoría y cada indicador es coherente con las reglas del negocio.

### Seguridad de la demostración

- **Aislamiento:** solo usa el SQL Server de su contenedor. La herramienta de carga rechaza cualquier servidor que no sea local y cualquier base cuyo nombre no empiece con `FixTrackDemo`, así que no puede apuntar a la base real.
- **Contraseñas por entorno:** se generan al azar para cada entorno nuevo y no están en el repositorio ni en las imágenes. En GitHub Actions se ocultan en el registro.
- **Mínimo privilegio:** `sa` solo prepara el servidor. Las migraciones, la carga y la aplicación usan un login sin permisos de servidor.
- **Certificado propio:** el de Data Protection es autofirmado, se crea dentro de un volumen de Docker y solo lo puede leer el usuario de la imagen. No es el certificado de producción.
- **Uso local:** la aplicación escucha solo en este equipo. **No la publiques en internet ni compartas sus cuentas:** para mostrarla a distancia, comparte la pantalla.

**Lo que depende del equipo anfitrión.** Estas garantías protegen frente a la red y frente a errores de configuración, no frente a quien ya controla el equipo:

- **Quien puede usar Docker puede ver las contraseñas:** `docker inspect` muestra las variables de los contenedores, y los volúmenes guardan la base y el certificado. En Linux, pertenecer al grupo `docker` equivale a ser administrador.
- **`127.0.0.1` aísla de la red, no de este equipo:** cualquier usuario o programa local puede abrir el puerto 8080.
- **El aislamiento de la base real es por configuración, no por red:** los contenedores tienen salida a internet, como en cualquier instalación de Docker. Lo que impide llegar a la base real es que la cadena apunta al servicio `sqlserver` y que la herramienta rechaza otros servidores.
- **`demo/.env` usa permisos 600,** que protegen en macOS y Linux. En Windows (NTFS) no tienen el mismo efecto.
- **`iniciar` y `credenciales` muestran las contraseñas en la terminal.** Si compartes una captura o el registro, cámbialas con `./demo/demo.sh reiniciar`. En GitHub Actions se ocultan.
- **Una sola demostración por equipo:** el proyecto de Compose se llama siempre `fixtrack-demo`. Dos copias del repositorio comparten contenedores y volúmenes: `eliminar` desde cualquiera borra la demostración, e `iniciar` en una copia sin `demo/.env` borra los datos de la otra (lo indica con un mensaje, sin pedir confirmación).
- **Tráfico interno:** entre los contenedores la conexión va cifrada, pero sin validar el certificado autofirmado de SQL Server (`TrustServerCertificate=True`). En producción, la cadena valida el certificado.

### Sin el script

**Con Docker Compose directamente** (por ejemplo, desde PowerShell): copia `demo/.env.example` como `demo/.env`, completa cada contraseña (12 caracteres o más, con mayúsculas, minúsculas y números) y ejecuta:

```bash
docker compose -f demo/compose.yaml up --build --detach app
docker compose -f demo/compose.yaml logs datos      # resumen de la carga
docker compose -f demo/compose.yaml down --volumes  # borrar todo
```

**Sin Docker Compose, con un SQL Server local que ya tengas** (por ejemplo, el contenedor de las pruebas de la [guía de operación](guia-de-operacion.md#pruebas-contra-sql-server-real), en `127.0.0.1:14333`). Necesita el SDK de .NET 10. FixTrack se ejecuta como `Production`: así no lee `dotnet user-secrets`, donde puede estar la cadena de conexión de la base real.

```bash
# Contraseñas de esta sesión de terminal; ninguna se guarda en el repositorio.
generar() { printf '%sFx7' "$(head -c 512 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9' | cut -c 1-24)"; }
read -rs Demo__SaPassword && export Demo__SaPassword          # la de sa de tu contenedor; no se muestra
export Demo__Server=127.0.0.1 Demo__Port=14333 Demo__Database=FixTrackDemo
export Demo__AppPassword="$(generar)" Demo__CertificatePassword="$(generar)"
export Demo__AdminPassword="$(generar)" Demo__OperatorPassword="$(generar)" Demo__ReadOnlyPassword="$(generar)"
mkdir -p ~/FixTrack-demo && chmod 700 ~/FixTrack-demo          # fuera del repositorio
export Demo__CertificatePath=~/FixTrack-demo/fixtrack-demo-dp.pfx

# 1. Base, login y certificado
dotnet run --project tools/FixTrack.Demo -c Release -- preparar

# 2. Esquema, con el comando de FixTrack
export ASPNETCORE_ENVIRONMENT=Production ReverseProxy__IgnoreForwardedHeaders=true
export DataProtection__Certificate__Path="$Demo__CertificatePath" DataProtection__Certificate__Password="$Demo__CertificatePassword"
export ConnectionStrings__FixTrack="Server=127.0.0.1,14333;Database=FixTrackDemo;User ID=fixtrack_demo;Password=$Demo__AppPassword;Encrypt=True;TrustServerCertificate=True"
dotnet run --project src/FixTrack.Web -c Release --no-launch-profile -- aplicar-migraciones

# 3. Cuentas y datos ficticios
dotnet run --project tools/FixTrack.Demo -c Release -- cargar

# 4. FixTrack con los extras, en http://localhost:5104
export PortfolioFeatures__Reports=true PortfolioFeatures__PrintableSheets=true PortfolioFeatures__ReadOnlyApi=true
ASPNETCORE_URLS=http://localhost:5104 dotnet run --project src/FixTrack.Web -c Release --no-launch-profile
```

Las contraseñas de las cuentas quedan en `Demo__AdminPassword`, `Demo__OperatorPassword` y `Demo__ReadOnlyPassword` mientras la terminal siga abierta (`printenv Demo__AdminPassword`). Para empezar de nuevo, elimina la base `FixTrackDemo` y el login `fixtrack_demo` del servidor, y la carpeta `~/FixTrack-demo`.

### Si algo falla

| Síntoma | Solución |
| --- | --- |
| `port is already allocated` | Otro programa usa el puerto 8080: cambia `DEMO_PORT` en `demo/.env` y ejecuta `iniciar` otra vez |
| `sqlserver` no llega a estar saludable | Revisa que Docker tenga al menos 2 GB de memoria y, en Apple Silicon, que la emulación con Rosetta esté activa. Registro: `docker compose -f demo/compose.yaml logs sqlserver` |
| "La base no está vacía y no contiene una demostración completa" | Una carga anterior se interrumpió: `./demo/demo.sh reiniciar` |
| "El certificado … no abre con la contraseña configurada" | `demo/.env` no corresponde a los volúmenes existentes: `./demo/demo.sh reiniciar` |
| El inicio de sesión vuelve al formulario sin mensaje | El navegador no guardó la cookie `Secure` en `http://localhost`: usa Chrome o Edge |
| La aplicación no responde | `./demo/demo.sh estado` y `docker compose -f demo/compose.yaml logs app` |

## 2. Recorrido de cinco minutos

Los números de orden son los mismos en cada carga. Empieza con la cuenta **Administrador**.

1. **Panel** (`/`, 45 s). Diez órdenes abiertas: dos vencidas y dos sin técnico. Las "Acciones recomendadas" no son una lista fija: salen del algoritmo de priorización calculado en SQL. Arriba aparece la OS-000021, urgente y vencida.
2. **Órdenes** (`/ordenes`, 30 s). La vista *Vencidas* muestra la OS-000016 y la OS-000021; el filtro de técnico *Sin asignar*, las dos recibidas hoy. Busca "jose" en *Todas*: aparecen las órdenes de "José Ramírez (demo)", porque la búsqueda ignora tildes y mayúsculas.
3. **Asignación y prioridades** (`/cola-de-trabajo`, 1 min).
   - **Cola:** la OS-000021 encabeza las reparaciones: urgente (+90), vencida (+50) y unos días en el taller. El desglose de cada orden explica su lugar.
   - **Asignar:** abre la OS-000025 (tableta, sin técnico), asígnala a Diego Alvarado, el técnico de tabletas, y vuelve a la cola: su acción recomendada pasa de "Asignar técnico" a "Iniciar diagnóstico".
   - **Repuestos:** las órdenes en espera aparecen en su propia lista y no compiten con las reparaciones que sí se pueden hacer.
4. **Historial y auditoría** (1 min).
   - **Reapertura:** abre la OS-000013 (monitor de Distribuidora El Pino). Estuvo lista para entrega, falló la prueba, volvió a reparación y se entregó con un día de atraso y Q 40 sobre lo estimado. Cada paso tiene su autor y nada se puede editar.
   - **Reasignación:** la OS-000008 muestra una reasignación con su motivo.
   - **Auditoría:** `/auditoria` (filtro *Órdenes*) muestra todo lo anterior, incluida la asignación que acabas de hacer.
5. **Reportes** (`/reportes`, *Últimos 90 días*, 45 s). 25 recibidas, 13 entregadas y 4 de 12 con atraso.
   - **Lectura rápida:** lo explica en una frase.
   - **Datos del gráfico:** cada gráfico tiene su tabla en *Ver los datos del gráfico*.
   - **Exportar CSV:** el archivo no incluye datos de contacto.
6. **Fichas** (30 s). Desde la OS-000013, *Comprobante de entrega* → *Imprimir o guardar PDF*: un documento A4 generado por el navegador, sin dependencias en el servidor.
7. **API** (30 s). En la misma sesión, abre `/api/v1/ordenes?porPagina=5&orden=-prioridad`: JSON paginado, sin teléfonos ni correos. Luego `/api/v1/ordenes?porPagina=500`: un 400 con Problem Details en español.

Si queda tiempo, entra con la cuenta **Consulta** en otra ventana privada. Ve todas las pantallas, pero al enviar cualquier formulario recibe una página 403 que lo explica, y no se guarda nada.

Para dejar la demostración como al principio: `./demo/demo.sh reiniciar`.

## 3. Qué destacar en una entrevista

Cada punto va de una decisión a la evidencia en el código. El resumen técnico completo está en [arquitectura-tecnica.md](arquitectura-tecnica.md).

1. **Reglas en el dominio, no en las vistas.** `ServiceOrder` y `ServiceOrderWorkflow` deciden qué transiciones existen y qué requiere cada una. La interfaz pregunta a esas clases qué botones mostrar, así que no puede ofrecer un paso que el servidor rechazaría.
2. **La historia no se puede reescribir.** Historial, asignaciones y auditoría se guardan en la misma transacción que el cambio. Además, son tablas de solo inserción protegidas por triggers. Las pruebas contra SQL Server real intentan modificarlas y fallan.
3. **Concurrencia sin bloqueos innecesarios:**
   - versión optimista en las ediciones;
   - `UPDLOCK` breve sobre el técnico al asignar;
   - `sp_getapplock` para la regla "siempre queda un administrador";
   - un índice único contra el doble envío.

   Todo funciona en SQL Server Express, sin `SERIALIZABLE`.
4. **Priorizar en la base sin duplicar la lógica a ciegas.** La fórmula vive en el dominio y el repositorio la reproduce en SQL con los mismos parámetros. Una prueba compara ambos órdenes sobre más de 300 casos límite, en SQLite y en SQL Server.
5. **Analítica correcta, no solo rápida:**
   - cada indicador usa la fecha de su propio evento;
   - los períodos se calculan en la hora local del taller;
   - un promedio sin datos dice "Sin datos".

   El reporte completo son 8 consultas fijas. Una función mapeada por proveedor (`DATEDIFF_BIG` en SQL Server, `unixepoch` en SQLite) resuelve lo que EF Core no traduce.
6. **Funciones nuevas sin tocar lo aprobado.** Los extras están detrás de feature flags que quitan sus controladores del enrutamiento. Con las flags apagadas, el HTML de las pantallas académicas es idéntico byte a byte al de la versión base.
7. **Despliegues que fallan antes de causar daño.** Las claves de sesión se guardan en la base, cifradas con un certificado. Si el certificado configurado no puede descifrarlas, la aplicación no arranca, en lugar de invalidar todas las sesiones. Lo mismo ocurre sin una decisión explícita sobre el proxy.
8. **Datos de demostración honestos.** La carga usa los casos de uso con un `TimeProvider` y un `ICurrentUser` propios, en lugar de scripts SQL. Así, los datos respetan las mismas reglas que un usuario real. Inyectar el reloj y el autor desde el principio es lo que lo hizo posible.
9. **Pruebas en el nivel adecuado:**
   - dominio en memoria;
   - repositorios en SQLite;
   - páginas con `WebApplicationFactory` e inicio de sesión real;
   - lo propio de SQL Server (triggers, bloqueos, intercalación) contra un contenedor desechable.

   CI no tiene secretos ni acceso a la base real.

## 4. Preguntas probables

**¿Por qué no microservicios o una SPA?** Es una aplicación para un taller con pocos usuarios. Un monolito en capas, renderizado en el servidor, mantiene una sola fuente de verdad para las reglas, una CSP estricta y un despliegue simple. Las capas permiten separar después lo que haga falta.

**¿Cómo escalaría?**

- **Base de datos:** las consultas ya son agregaciones y proyecciones paginadas, sin consultas por fila. Con más volumen, el siguiente paso medible es un índice en `DeliveredAtUtc` para los reportes.
- **Varias instancias:** habría que llevar a un almacenamiento compartido, como Redis, el límite de solicitudes de la API y el disyuntor, que hoy viven en memoria.

**¿Qué pasa si dos personas editan la misma orden?** La segunda recibe un aviso de que otra persona la modificó, sin sobrescribir nada. Si dos solicitudes llegan a la vez, la base acepta solo la primera: el `UPDATE` compara la versión.

**¿Cómo proteges los datos personales?**

- el rol Consulta no puede enviar nada;
- las exportaciones y la API omiten teléfonos y correos;
- la auditoría no guarda copias de datos de contacto;
- los respaldos se escriben con permisos solo para el dueño y se rechazan dentro de un repositorio Git;
- los secretos viven en `user-secrets` y en variables de entorno.

**¿Qué harías distinto?**

- un rol Técnico con autorización por registro;
- tokens para la API entre servidores;
- notificaciones al cliente.

El diseño del rol Técnico está en [arquitectura.md](arquitectura.md#autorización). Quedó fuera porque hacerlo a medias daría una falsa sensación de control.

## 5. Descripciones listas para usar

**Descripción del repositorio en GitHub** (campo *About*):

> Gestión de órdenes de servicio técnico con ASP.NET Core MVC (.NET 10), EF Core y SQL Server: flujo de estados con historial auditable, cola priorizada en SQL, roles con Identity, reportes, API REST y demo con Docker Compose. Proyecto universitario ampliado como portafolio.

**Temas sugeridos:** `aspnet-core`, `csharp`, `dotnet`, `entity-framework-core`, `sql-server`, `mvc`, `docker`, `github-actions`, `layered-architecture`.

**Para el currículum (una línea):**

> FixTrack: sistema web de órdenes de servicio técnico (C#, ASP.NET Core MVC, EF Core, SQL Server) con historial auditable, priorización en SQL, roles y 867 pruebas automatizadas en GitHub Actions.

**Para el currículum (versión extendida):**

> **FixTrack** · Proyecto integrador universitario, ampliado como portafolio.
>
> - Aplicación web en capas (C#/.NET 10, ASP.NET Core MVC, Entity Framework Core, SQL Server) para gestionar las órdenes de un taller de reparación, con un flujo de estados explícito y un historial de solo inserción guardado en la misma transacción que cada cambio.
> - Cola de trabajo priorizada calculada en SQL, concurrencia optimista y bloqueos de aplicación, autenticación y roles con Identity, y un centro de reportes con agregaciones en SQL, fichas imprimibles y una API REST de solo lectura.
> - 867 pruebas automatizadas (xUnit, SQLite y SQL Server en contenedor), imagen Docker sin privilegios y una demostración reproducible con Docker Compose, todo verificado en GitHub Actions.

## 6. Mejoras posibles

- **Integración:** tokens de API para integraciones de servidor a servidor y un documento OpenAPI disponible solo con autenticación.
- **Funciones:**
  - un rol Técnico limitado a sus órdenes;
  - pagos y facturación, para distinguir ingresos registrados de cobros;
  - notificaciones al cliente cuando la orden está lista;
  - reportes comparativos entre períodos.
- **Operación:** el límite de solicitudes y el disyuntor compartidos entre instancias, y respaldos programados.

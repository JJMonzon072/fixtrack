# FixTrack: presentación del proyecto

Material para presentar FixTrack en GitHub, en el currículum o en una entrevista. FixTrack es un **proyecto integrador universitario** desarrollado por fases con revisión de QA; no es un producto comercial ni tiene clientes reales. Los datos de las capturas y los ejemplos son ficticios.

## El problema

Un taller de reparación de computadoras recibe equipos todos los días y necesita saber:

- qué tiene en el taller;
- quién trabaja cada orden y en qué estado está;
- qué entregas vencen;
- qué conviene atender primero.

Con hojas de cálculo o papel se pierden el historial y la trazabilidad, se duplican registros y nadie puede responder con datos cuánto tarda una reparación o qué tan bien se cumplen las fechas comprometidas.

## La solución

Una aplicación web para el personal del taller:

- **Órdenes de servicio** con un flujo de estados explícito (recibida, en diagnóstico, en reparación, en espera de repuestos, lista para entrega, entregada o cancelada), reglas de transición y una línea de tiempo de solo inserción con el autor de cada cambio.
- **Cola de trabajo priorizada** con un algoritmo determinista y explicable: prioridad, vencimiento y antigüedad, calculados en SQL.
- **Clientes, equipos y técnicos**, con concurrencia optimista (avisa si otra persona modificó el registro) y desactivación en lugar de borrado.
- **Cuentas y roles** (Administrador, Operador y Consulta de solo lectura) y **auditoría** de las operaciones importantes.
- **Extras de portafolio**, opcionales y desactivados por defecto:
  - centro de reportes con exportación CSV;
  - fichas imprimibles en A4;
  - API REST de solo lectura.

## Arquitectura

Arquitectura por capas en una sola solución .NET:

| Capa | Contenido |
| --- | --- |
| Domain | Entidades y reglas de negocio (flujo de estados, priorización, validaciones), sin dependencias |
| Application | Casos de uso, contratos de repositorios y modelos de lectura |
| Infrastructure | EF Core sobre SQL Server, Identity, auditoría, respaldos y resiliencia ante fallas de la base |
| Web | ASP.NET Core MVC con Razor, seguridad, API REST y composición de la aplicación |

Las dependencias apuntan hacia el dominio. Las consultas pesadas (panel, cola, reportes) son agregaciones en SQL Server con proyecciones, no cálculos en memoria.

## Stack tecnológico

- **Backend:** C# y .NET 10, ASP.NET Core MVC, Razor, Entity Framework Core 10, ASP.NET Core Identity y Data Protection con un certificado X.509.
- **Base de datos:** SQL Server 2022, con migraciones de EF Core.
- **Interfaz:** HTML y CSS propios con un sistema de diseño (IBM Plex, temas claro y oscuro, íconos Lucide) y gráficos SVG generados en el servidor.
- **Pruebas:** xUnit v3, `WebApplicationFactory`, SQLite en memoria y SQL Server desechable en contenedor.
- **Entrega:** Docker (imagen sin root, ReadyToRun), GitHub Actions y despliegue en Render.

## Características destacadas

- **Feature flags que no tocan la versión académica.** Los extras se activan por configuración. Desactivados, sus rutas no existen y el HTML de las 22 pantallas comparadas es idéntico byte a byte al de la versión base.
- **Analítica correcta con fechas.** Cada indicador usa la fecha de su propio evento, los períodos se consultan como intervalos [inicio, fin) en UTC calculados en la zona horaria del taller, y los promedios sin datos se muestran como "Sin datos", nunca inventados.
- **Exportación CSV segura:** compatible con Excel, protegida contra la inyección de fórmulas, sin datos de contacto y con un límite explícito.
- **Documentos A4** con CSS de impresión: el navegador genera el PDF, sin dependencias en el servidor.
- **API REST v1** de solo lectura, con DTO explícitos, paginación con límites, Problem Details y autorización por rol.
- **Accesibilidad:** las pantallas de los extras pasan axe-core (WCAG 2.1 AA) en escritorio y móvil, en ambos temas; los gráficos tienen texto alternativo y tabla de datos.

## Retos técnicos resueltos

1. **Promediar duraciones en SQL con EF Core.** EF Core 10 no traduce la resta de fechas dentro de una agregación. Una función mapeada por proveedor (`DATEDIFF_BIG` en SQL Server, `unixepoch` en SQLite) permite sumar segundos en la base, y las mismas pruebas se ejecutan en ambos motores.
2. **Agrupar por día local sin errores de borde.** Los períodos se convierten día por día a UTC, incluso en zonas con horario de verano o días sin medianoche, y las series se agrupan sumando el desfase en SQL, en tramos de desfase constante.
3. **Consultas que no crecen con los datos.** El reporte completo son 8 consultas con `COUNT` y `SUM` condicionales; una prueba comprueba que la cantidad no cambia al triplicar los datos.
4. **Ocultar por completo una función desactivada.** Una convención de MVC elimina los controladores antes de crear las rutas, en lugar de responder 404 después de autorizar, lo que revelaría la ruta.
5. **Gráficos con una CSP estricta.** Sin estilos en línea ni bibliotecas de JavaScript: SVG calculado en C#, en dos tamaños para que el texto sea legible en un teléfono.
6. **Concurrencia en la operación diaria** (fases anteriores): concurrencia optimista con versión, bloqueos de aplicación en SQL Server para las reglas de las cuentas y envíos repetidos de formularios sin órdenes duplicadas.

## Enfoque de seguridad

- **Acceso:** autenticación con cookie (`HttpOnly`, `SameSite`, `Secure` en producción); limita los intentos de inicio de sesión por IP y bloquea temporalmente la cuenta tras varios fallos.
- **Autorización:** por rol, con una política de respaldo que protege cualquier ruta nueva. El rol de solo lectura se aplica en el servidor, no solo en los menús.
- **Tokens y cabeceras:** tokens antifalsificación, CSP con nonce sin `unsafe-inline`, `X-Frame-Options`, `Referrer-Policy` y HSTS detrás del proxy confiable.
- **Claves:** las de Data Protection se guardan en la base cifradas con un certificado; si el certificado no las descifra, la aplicación no inicia.
- **Datos:** minimización en exportaciones y API (sin teléfonos, correos, cuentas ni auditoría); errores sin trazas técnicas.
- **Secretos:** fuera del repositorio (user-secrets y variables de entorno). CI no necesita credenciales de la base real.

## Estrategia de pruebas y CI/CD

- **835 casos automatizados**, con estas capas:
  - **dominio y aplicación:** con repositorios en memoria;
  - **infraestructura:** SQLite en memoria;
  - **SQL Server real:** en un contenedor desechable, para triggers, bloqueos, concurrencia y traducción de consultas;
  - **integración web:** la aplicación en memoria, con inicio de sesión real.
- **Lo que se prueba:**
  - permisos por rol en todos los POST;
  - CSRF, encabezados y resiliencia ante una base caída;
  - los contratos exactos de la API y la protección del CSV.
- **GitHub Actions:** compila y prueba en cada push, ejecuta las pruebas contra un SQL Server efímero y construye y prueba la imagen Docker.
- **Evidencias de QA:** capturas reales con su manifiesto (commit, navegador, dimensiones y SHA-256) en `qa-evidence/`.

## Cómo demostrar el proyecto

1. **Versión académica:** iniciar sesión, mostrar el panel, registrar una orden urgente y llevarla por la cola de trabajo hasta su entrega; la línea de tiempo muestra cada paso y su autor.
2. **Roles:** con una cuenta Operador no aparece Administración; con una cuenta Consulta se ve todo y cualquier envío se rechaza.
3. **Extras**, habilitados en local con una base de demostración (ver [portfolio-extras.md](portfolio-extras.md#1-activación)):
   - **Reportes:** los últimos 90 días, un filtro por técnico, la lectura rápida, la tabla de datos de un gráfico y la exportación CSV.
   - **Fichas:** desde el detalle de una orden entregada, "Ficha de recepción" y "Comprobante de entrega", con "Imprimir o guardar PDF".
   - **API:** con la sesión iniciada, `/api/v1/ordenes?porPagina=5&orden=-prioridad` en el navegador; mostrar un error 400 con `porPagina=500`.
4. **Calidad:** las pruebas en GitHub Actions, la comparación byte a byte con los extras desactivados y el informe de accesibilidad.

### Descripción breve para el currículum

> FixTrack: sistema web de gestión de órdenes de servicio técnico (proyecto integrador universitario). C#/.NET 10, ASP.NET Core MVC, EF Core y SQL Server, con arquitectura por capas. Incluye un flujo de estados con historial auditable, una cola priorizada calculada en SQL, roles con Identity, un centro de reportes con agregaciones en SQL y gráficos SVG accesibles, fichas A4 imprimibles y una API REST de solo lectura con Problem Details. 835 pruebas automatizadas (xUnit, SQL Server en contenedor) en GitHub Actions; despliegue con Docker.

## Posibles mejoras futuras

- **Integración:**
  - tokens de API para integraciones de servidor a servidor;
  - documento OpenAPI disponible solo con autenticación.
- **Funciones nuevas:**
  - un rol Técnico con autorización por registro (cada técnico limitado a sus órdenes);
  - pagos y facturación, para distinguir ingresos registrados de cobros;
  - notificaciones al cliente (correo o mensajería) cuando la orden está lista;
  - exportaciones programadas y reportes comparativos entre períodos.
- **Rendimiento:**
  - índice en `DeliveredAtUtc` si el volumen de órdenes lo justifica;
  - límite de solicitudes compartido entre instancias, por ejemplo con Redis.

# Checklist de lanzamiento: FixTrack en Render Free con SQL Server en Somee

Procedimiento para publicar FixTrack por primera vez y operarlo después. Distingue lo que **ya se verificó**, lo que está **preparado** y lo que **requiere una acción o una autorización del propietario**. No contiene secretos: solo nombres de variables, de elementos del Llavero y de archivos.

> **Estado al cierre de la fase 5:** la aplicación **no está publicada**. Todo lo que se podía comprobar sin exponerla ni modificar la base real se comprobó con la imagen de producción contra una copia aislada de FixTrackDB. Con la autorización del propietario, las claves de Data Protection de FixTrackDB ya se cifraron con el certificado (sección 3.3). Quedan las acciones de las secciones 3.1, 3.4, 4 y 5, que necesitan al propietario.

Referencias: [README](../README.md) (configuración y comandos), [arquitectura.md](arquitectura.md) (seguridad, resiliencia y respaldos).

## 1. Estado de los requisitos de producción

**Verificado:** probado en esta fase con la imagen de producción (`ASPNETCORE_ENVIRONMENT=Production`) contra una copia de FixTrackDB restaurada en un SQL Server local. **Preparado:** implementado y probado localmente, pero solo se puede confirmar en Render. **Pendiente:** requiere al propietario.

| Requisito | Estado | Evidencia o acción |
| --- | --- | --- |
| La imagen se construye y arranca | Verificado | Imagen ReadyToRun de 209 MB (arm64) y para amd64, la arquitectura de Render: compila, arranca, inicia sesión y muestra órdenes |
| Se ejecuta sin root | Verificado | Proceso con UID 1654 (usuario `app` de la imagen *chiseled*, sin shell) |
| Configuración solo por variables de entorno | Verificado | La imagen no contiene cadenas de conexión, certificados ni contraseñas |
| Autenticación obligatoria y sin registro público | Verificado | 10 rutas privadas redirigen al inicio de sesión y el JSON responde 401. No existe página de registro: sin sesión, cualquier ruta (también las de registro habituales) lleva al inicio de sesión, y con sesión responden 404 |
| Autorización por roles | Verificado | Administrador, Operador y Consulta en páginas y en POST manuales con token válido (también el intento de ascenderse); pruebas automáticas sobre **todos** los POST de administración (Operador) y sobre **todos** los POST de la aplicación (Consulta, que solo puede cerrar sesión) |
| Cookies | Verificado | Sesión: `Secure`, `HttpOnly`, `SameSite=Lax`, no persistente. Antifalsificación: `HttpOnly`, `SameSite=Strict` y `Secure` cuando la aplicación ve HTTPS (ver la sección 5.6) |
| Protección CSRF | Verificado | POST sin token: 400 |
| Encabezados | Verificado | CSP con *nonce* y sin `unsafe-inline` en scripts, `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, COOP, `Cache-Control: no-store`, sin `Server` |
| HSTS | Preparado | Verificado detrás de un proxy confiable simulado (`max-age=2592000`). **Pendiente comprobarlo en Render** (sección 5.6) |
| Límite de intentos por IP real | Preparado | Verificado con un proxy confiable simulado: cada cliente tiene su cuota. **Pendiente comprobarlo en Render** (sección 5.6) |
| Sin secretos en HTML, JavaScript ni CSS | Verificado | Se buscaron la cadena de conexión, la contraseña del PFX, su contenido y patrones de credenciales en páginas y recursos: ninguno |
| Errores sin trazas internas | Verificado | 404, 400, 500 y 503 sin excepciones ni rutas de código |
| Health checks | Verificado | `/salud` (proceso) y `/salud/bd` (base, caché de 10 s), sin detalles ni caché del navegador |
| Resiliencia ante desconexiones de Somee | Verificado | Con SQL Server pausado: 503 con `Retry-After` en 20 s la primera solicitud y luego en milisegundos; al volver la base, se recupera solo y las sesiones siguen valiendo |
| Data Protection: claves cifradas con certificado | Verificado | Hecho el 2026-10-21 con autorización del propietario: FixTrackDB tiene la clave cifrada `360794bc…` y la anterior `d266998d…` revocada, sin borrar ni modificar filas. La imagen de producción con el certificado arranca contra FixTrackDB, lee las claves y genera la página de inicio de sesión sin advertencias (sección 3.3) |
| Persistencia de sesiones entre reinicios | Verificado | Sesiones válidas tras reiniciar el contenedor, con la clave anterior y con la cifrada |
| Certificado equivocado | Verificado | La aplicación no arranca, no escribe claves y explica el motivo: en Render, el despliegue falla y queda la versión anterior |
| Migraciones | Verificado | FixTrackDB tiene las 9 migraciones; esta fase no agrega ninguna |
| Respaldos cifrados | Verificado | Bóveda y copia de recuperación cifradas (AES-256), con hashes. **Pendiente: copia externa y contraseñas en un gestor** (sección 3.1) |
| Accesibilidad e interfaz | Verificado | axe-core (WCAG 2.1 A y AA): 0 incumplimientos en 120 cargas de 30 páginas (escritorio y móvil, temas claro y oscuro) y en las páginas de error |
| Recursos del plan gratuito | Verificado localmente | Con `--cpus=0.1 --memory=512m`: escucha a los 16-18 s, 74 MiB de memoria (sección 7) |
| Publicación en Render | Pendiente | Requiere autorización (sección 4) |

## 2. Materiales y dónde están

| Material | Ubicación | Protección |
| --- | --- | --- |
| Certificado de Data Protection (PFX con clave privada) | `certificados/fixtrack-dataprotection-2026.pfx` dentro de la bóveda | PFX cifrado (AES-256) con contraseña aleatoria; la bóveda es una imagen cifrada |
| Parte pública del certificado | `certificados/fixtrack-dataprotection-2026.crt` | No es secreta |
| Respaldos de FixTrackDB | `respaldos/` dentro de la bóveda, con `SHA256SUMS` | Bóveda cifrada |
| Bóveda | `~/FixTrack-boveda/FixTrack-boveda.dmg` (512 MB, APFS) | Imagen cifrada AES-256; carpeta `700`, archivo `600` |
| Copia de recuperación | `~/FixTrack-boveda/recuperacion/FixTrack-recuperacion.dmg` | Otra imagen cifrada AES-256, con **otra contraseña** |
| Contraseñas | Llavero de macOS, cuenta `FixTrack`: `fixtrack-boveda`, `fixtrack-boveda-recuperacion`, `fixtrack-dataprotection-pfx` | Solo en el Llavero de este Mac |
| Respaldos anteriores sin cifrar | `~/FixTrack-respaldos/` (permisos `600`) | Se conservan sin cambios; están también dentro de la bóveda |

Datos públicos del certificado: CN `FixTrack Data Protection`, RSA 3072, SHA-256, vigente del **2026-10-20 al 2031-10-20**, huella SHA-256 `C1:D7:D7:12:B1:DA:6B:58:38:9A:56:FE:76:6D:7E:42:27:EE:A4:E8:44:9B:D5:B5:76:DF:13:BC:60:09:74:79`.

Abrir, comprobar y cerrar la bóveda, sin mostrar la contraseña:

```bash
security find-generic-password -a FixTrack -s fixtrack-boveda -w | tr -d '\n' | hdiutil attach -stdinpass -nobrowse ~/FixTrack-boveda/FixTrack-boveda.dmg
cd /Volumes/FixTrackBoveda && shasum -a 256 -c SHA256SUMS && cd ~
hdiutil detach /Volumes/FixTrackBoveda
```

La copia de recuperación se abre igual con `fixtrack-boveda-recuperacion` y `~/FixTrack-boveda/recuperacion/FixTrack-recuperacion.dmg`; se monta en `/Volumes/FixTrackRecuperacion`.

## 3. Antes de publicar (propietario)

### 3.1 Proteger los materiales contra pérdidas

- [ ] Abrir **Acceso a Llaveros**, buscar los tres elementos `FixTrack: …` y copiar sus contraseñas a tu gestor de contraseñas. Hoy solo existen en el Llavero de este Mac.
- [ ] Copiar `~/FixTrack-boveda/recuperacion/FixTrack-recuperacion.dmg` a un disco externo o memoria USB que guardes aparte. Sin una copia fuera del Mac, perder el equipo significa perder el certificado y los respaldos.
- [ ] Opcional: cuando confirmes que la bóveda abre y verifica sus hashes, puedes borrar `~/FixTrack-respaldos/` (están dentro de la bóveda). No se borró porque así se pidió.

### 3.2 Respaldo previo

Inmediatamente antes de tocar la base real, un respaldo nuevo dentro de la bóveda:

```bash
security find-generic-password -a FixTrack -s fixtrack-boveda -w | tr -d '\n' | hdiutil attach -stdinpass -nobrowse ~/FixTrack-boveda/FixTrack-boveda.dmg
cd ~/Documents/Projects/fixtrack
dotnet run --project src/FixTrack.Web -c Release -- exportar-respaldo /Volumes/FixTrackBoveda/respaldos
dotnet run --project src/FixTrack.Web -c Release -- verificar-respaldo /Volumes/FixTrackBoveda/respaldos/<archivo nuevo>.zip
```

Respaldos de esta fase en la bóveda (y en la copia de recuperación):

- `fixtrack-respaldo-20261021-013454Z.zip`: tomado justo antes de cifrar las claves. 15 tablas y 56 filas; SHA-256 del archivo `8b7066f4…`. Se verificó y se restauró completo en un SQL Server local.
- `fixtrack-respaldo-20261021-013538Z.zip`: tomado justo después. 15 tablas y 58 filas (las dos filas nuevas de `DataProtectionKeys`); SHA-256 `0bc55ef5…`.

### 3.3 Cifrar las claves de Data Protection de FixTrackDB (hecho el 2026-10-21)

**Resultado.** Se ejecutó el 2026-10-21 a las 01:35 UTC, con la autorización del propietario y después del respaldo de la sección 3.2:

- se creó la clave cifrada `360794bc-2dc6-4243-adb1-6097781f0815`, que vence el 2027-01-19;
- se revocó la clave sin cifrar `d266998d…`.

Comprobado después:

- `DataProtectionKeys` pasó de 1 fila a 3; la fila original quedó idéntica byte a byte;
- las otras 14 tablas no cambiaron (mismos hashes que el respaldo previo);
- la imagen de producción con el certificado arrancó contra FixTrackDB sin advertencias: la comprobación de claves pasó, `/salud/bd` respondió Healthy y se generó la página de inicio de sesión.

El procedimiento queda como referencia (por ejemplo, para una rotación).

**Estado anterior:** FixTrackDB tenía una sola clave de Data Protection, **sin cifrar** (`d266998d…`, vence el 2027-01-18). Este paso crea una clave cifrada con el certificado y revoca la anterior. Revocar agrega una fila de revocación: **no borra ni modifica filas existentes**. Las sesiones emitidas con la clave anterior dejan de valer; hoy no hay sesiones reales que dependan de ella (la aplicación no está publicada y el entorno de Desarrollo guarda sus claves en el perfil local).

Ensayado en la copia: crea la clave cifrada, revoca la anterior, las sesiones nuevas usan la clave cifrada y sobreviven a reinicios, y la copia de recuperación del certificado también la descifra.

Con la bóveda abierta (paso 3.2) y desde la carpeta del repositorio:

```bash
export DataProtection__KeyStore=Database
export DataProtection__Certificate__Path=/Volumes/FixTrackBoveda/certificados/fixtrack-dataprotection-2026.pfx
export DataProtection__Certificate__Password="$(security find-generic-password -a FixTrack -s fixtrack-dataprotection-pfx -w)"
dotnet run --project src/FixTrack.Web -c Release -- proteger-claves --revocar-sin-cifrar
unset DataProtection__KeyStore DataProtection__Certificate__Path DataProtection__Certificate__Password
hdiutil detach /Volumes/FixTrackBoveda
```

Resultado esperado: `Se creó la clave cifrada …`, `Claves revocadas: 1` y la lista con la clave anterior `SIN CIFRAR · revocada` y la nueva `cifrada`.

- Si ya hubiera una instancia publicada, después hay que **reiniciarla** (Render → *Manual Deploy* → *Restart service*): conserva el anillo de claves en memoria y seguiría aceptando las sesiones antiguas hasta reiniciarse.
- Si el comando dice que hay claves que el certificado **no puede descifrar**, no hace nada: revisa que el PFX sea el de la bóveda.

### 3.4 Integración a `main`

La rama de producción de Render es `main`. Publicar requiere que QA apruebe la fase e integre `JJ` en `main`.

### 3.5 Rol Consulta: migración y cuenta de demostración

El rol **Consulta** ve todas las pantallas (también usuarios y auditoría) y no puede guardar nada: sirve para que otras personas exploren FixTrack y para preparar el manual de usuario. Llega con la migración `20261021023314_ReadOnlyRole`, que solo inserta una fila en `AspNetRoles`; la versión ya publicada no usa ese rol, así que se puede aplicar antes o después del despliegue. Se ensayó sobre una copia restaurada del último respaldo de FixTrackDB: se aplicó solo esa migración, el comando creó la cuenta, las pantallas mostraron el aviso de solo consulta y un envío respondió 403 sin guardar nada.

- [ ] Respaldo previo (3.2).
- [ ] Aplicar la migración. Debe decir `Aplicando 1 migración(es): 20261021023314_ReadOnlyRole`:

  ```bash
  dotnet run --project src/FixTrack.Web -c Release -- aplicar-migraciones
  ```

- [ ] Crear la cuenta (pide la contraseña dos veces, sin mostrarla). Usa un correo que no sea de una persona, por ejemplo uno de tu dominio o `@fixtrack.invalid`:

  ```bash
  dotnet run --project src/FixTrack.Web -c Release -- crear-cuenta-consulta <correo> "<nombre completo>"
  ```

- [ ] Compartir el correo y la contraseña solo por un canal privado y con personas de confianza: la cuenta ve los datos personales de los clientes y los correos de las cuentas. Nunca en GitHub.
- [ ] Si alguien la bloquea con cinco intentos fallidos, se desbloquea desde `/usuarios`. Cuando ya no haga falta, deshabilitarla ahí mismo.

## 4. Configuración de Render, paso a paso

Según la documentación de Render consultada el 2026-10-20 ([plan gratuito](https://render.com/docs/free), [Docker](https://render.com/docs/docker), [variables](https://render.com/docs/configure-environment-variables), [despliegues](https://render.com/docs/deploys), [health checks](https://render.com/docs/health-checks), [rollbacks](https://render.com/docs/rollbacks), [planes](https://render.com/docs/compute-plans)).

No se usa `render.yaml`: el servicio se crea a mano para controlar el plan y las variables secretas.

1. **New → Web Service** → conectar el repositorio `JJMonzon072/fixtrack`.
2. **Branch:** `main`. **Language/Runtime:** Docker. **Region:** la más cercana (por ejemplo, Ohio). **Instance Type:** **Free**.
3. **Dockerfile Path:** `./Dockerfile`. **Docker Build Context Directory:** `.` (la raíz). **Docker Command:** vacío; la imagen ya ejecuta la aplicación. **No** se ejecutan migraciones en el contenedor.
4. **Health Check Path:** `/salud`. Render da por sana una respuesta 2xx o 3xx en 5 s y reinicia tras 60 s de fallos. **No uses `/salud/bd`**: una caída de Somee reiniciaría la instancia sin resolver nada.
5. **Auto-Deploy:** **Off** para el primer despliegue. Cuando todo esté verificado, puede pasar a *After CI Checks Pass*. Nunca se despliega desde `JJ`.
6. **Environment** (las tres primeras como valores secretos; ver cómo copiarlos sin mostrarlos más abajo):

| Variable | Valor | Notas |
| --- | --- | --- |
| `ConnectionStrings__FixTrack` | La cadena de Somee de tus secretos de usuario | Incluye `Encrypt=True`, `TrustServerCertificate=False` y `HostNameInCertificate=vb4300.mgmt.somee.com` |
| `DataProtection__Certificate__Base64` | El PFX en base64 | Copiarlo con el comando de abajo |
| `DataProtection__Certificate__Password` | La contraseña del PFX | Del Llavero, elemento `fixtrack-dataprotection-pfx` |
| `ReverseProxy__IgnoreForwardedHeaders` | `true` | Solo para el primer despliegue (sección 5.6) |

Ya definidas en la imagen: `ASPNETCORE_ENVIRONMENT=Production`, `TZ=America/Guatemala`. Render define `PORT` (10000) y la aplicación lo usa. **No definas** `DataProtection__AllowUnprotectedKeys` ni `InitialAdmin__*` (el administrador ya existe).

Copiar los valores al portapapeles sin mostrarlos (con la bóveda abierta), pegarlos en Render y vaciar el portapapeles:

```bash
base64 -i /Volumes/FixTrackBoveda/certificados/fixtrack-dataprotection-2026.pfx | tr -d '\n' | pbcopy      # DataProtection__Certificate__Base64
security find-generic-password -a FixTrack -s fixtrack-dataprotection-pfx -w | tr -d '\n' | pbcopy       # DataProtection__Certificate__Password
dotnet user-secrets list --project src/FixTrack.Web | sed -n 's/^ConnectionStrings:FixTrack = //p' | tr -d '\n' | pbcopy   # ConnectionStrings__FixTrack
pbcopy < /dev/null                                                                                         # vaciar el portapapeles
```

Sobre el certificado en Render:

- Render convierte las variables de entorno en *build arguments* de Docker. El `Dockerfile` no declara ninguno con esos nombres, así que no quedan en la imagen.
- **No subas el PFX como *Secret File***: Render copia esos archivos en la raíz del contexto de construcción. Usa las variables.

## 5. Primer despliegue y verificaciones con la URL

Publicar expone el inicio de sesión y la aplicación conectada a la base real: requiere la autorización del propietario.

### 5.1 Bitácora del despliegue

- [ ] Aparece `Certificado de Data Protection vigente hasta el 2031-10-20.` y después `Now listening on: http://[::]:10000`.
- [ ] Si aparece `no puede descifrar … clave(s)`, el certificado no es el correcto: la aplicación no arranca a propósito y Render conserva la versión anterior. Corrige la variable y vuelve a desplegar.
- [ ] Si aparece `En producción hay que indicar qué proxy es confiable`, falta `ReverseProxy__IgnoreForwardedHeaders`.

### 5.2 Salud y HTTPS

```bash
curl -s https://<servicio>.onrender.com/salud        # Healthy
curl -s https://<servicio>.onrender.com/salud/bd     # Healthy (la base responde)
curl -sI http://<servicio>.onrender.com/cuenta/iniciar-sesion | head -3    # Render redirige a https
curl -sI https://<servicio>.onrender.com/cuenta/iniciar-sesion             # 200, CSP, no-store, sin "Server: Kestrel"
```

### 5.3 Primera prueba de inicio de sesión

- [ ] Iniciar sesión con la cuenta del administrador real. En las herramientas del navegador, la cookie `FixTrack.Sesion` debe ser `Secure`, `HttpOnly` y `SameSite=Lax`.
- [ ] Recorrer el panel, órdenes, cola de trabajo, clientes, equipos, técnicos, usuarios y auditoría. Los datos son los de FixTrackDB.
- [ ] Cerrar sesión y comprobar que `/ordenes` vuelve a pedir inicio de sesión.

### 5.4 Roles y permisos

Las cuentas QA de Somee están deshabilitadas y no se deben tocar sin autorización. Para probar el rol Operador hace falta una cuenta Operador:

- [ ] Opción recomendada: crear desde `/usuarios` una cuenta Operador temporal con un correo propio, probar con ella (sin Administración; `/usuarios` y `/auditoria` responden 403) y **deshabilitarla** al terminar. Queda en la auditoría.
- [ ] Con la cuenta Consulta (3.5): todas las pantallas muestran el aviso de solo consulta, también `/usuarios` y `/auditoria`. Enviar cualquier formulario muestra "Esta cuenta es de solo consulta" y no guarda nada; cerrar sesión sí funciona.

### 5.5 Pruebas de funcionamiento

Todo lo que se registre queda en la base real (las órdenes y la auditoría no se borran):

- [ ] Sin escribir: búsquedas, filtros, detalle e historial de una orden, cola de trabajo, auditoría.
- [ ] Con escritura, solo si el propietario lo decide: una orden de prueba identificable, que después se cancela con el motivo "Prueba de lanzamiento".

### 5.6 Proxy de Render: descubrir y verificar su dirección

Render termina HTTPS en su balanceador y reenvía la solicitud por HTTP. FixTrack no confía en ningún proxy por omisión ni usa rangos privados amplios.

**Mientras `ReverseProxy__IgnoreForwardedHeaders=true` (modo seguro inicial).** La aplicación es segura: la cookie de sesión sigue siendo `Secure` y no se puede falsificar la IP del cliente ni el esquema. Las limitaciones temporales:

- No envía **HSTS**, porque no sabe que el visitante usó HTTPS. Render igual redirige `http` a `https`.
- La cookie **antifalsificación no es `Secure`**. Con `Always`, la aplicación respondería 500 a toda solicitud HTTP: hay una prueba automática que lo impide.
- El **límite de 10 inicios de sesión por minuto se comparte entre todos los visitantes**, porque todos llegan desde la dirección del balanceador. Diez intentos fallidos de cualquiera bloquean el formulario a todos durante un minuto.
- La bitácora registra la IP del balanceador en los inicios fallidos.

**Pasos para corregirlo:**

1. [ ] En **Logs** de Render, buscar `Se ignoraron encabezados reenviados (X-Forwarded-*) recibidos desde <dirección>: … Encabezados presentes (solo nombres): …`. Aparece como máximo cada 15 minutos. Anotar varias apariciones, en distintos momentos y después de un reinicio, para saber si la dirección cambia.
2. [ ] Si siempre es la misma dirección: definir `ReverseProxy__KnownProxies__0=<dirección>`. Si cambia dentro de una red: `ReverseProxy__KnownNetworks__0=<red>/<prefijo>`, **la más estrecha** que cubra lo observado (nunca `10.0.0.0/8` "por si acaso"). En ambos casos, **eliminar** `ReverseProxy__IgnoreForwardedHeaders` y desplegar.
3. [ ] Comprobar HSTS: `curl -sI https://<servicio>.onrender.com/cuenta/iniciar-sesion | grep -i strict-transport-security` debe mostrar `max-age=2592000`.
4. [ ] Comprobar la IP real: desde el teléfono con datos móviles, intentar iniciar sesión con una contraseña incorrecta. En la bitácora, `Inicio de sesión rechazado desde <IP>` debe mostrar la IP pública del teléfono (compárala con un sitio como "cuál es mi IP"), no una dirección privada.
5. [ ] Si la IP registrada es la de una red de borde y no la del teléfono, y la advertencia del paso 1 mostraba un encabezado de un solo valor como `True-Client-IP` o `CF-Connecting-IP`: definir `ReverseProxy__ClientIpHeader=<ese encabezado>` y repetir el paso 4.
6. [ ] Comprobar que la advertencia del paso 1 ya no aparece.

No afirmes que HSTS o el límite por IP real funcionan hasta completar los pasos 3 y 4 en Render.

### 5.7 Revisión de errores

- [ ] En la bitácora no hay `fail:` ni `crit:` durante la navegación normal.
- [ ] Una ruta inexistente muestra la página 404 de FixTrack, sin detalles técnicos.

## 6. Procedimientos de recuperación

| Situación | Qué pasa | Qué hacer |
| --- | --- | --- |
| Falla un despliegue (configuración o health check) | Render conserva la instancia anterior | Leer la bitácora: la aplicación indica la variable que falta o el certificado incorrecto |
| Una versión nueva se comporta mal | — | **Deploys → Rollback** a un despliegue anterior. Restaura también las variables de entorno de ese despliegue y **desactiva el despliegue automático** |
| Somee no responde | Las páginas muestran 503 con `Retry-After`; la aplicación se recupera sola cuando vuelve | Nada. Si dura, revisar el panel de Somee |
| Se perdió la base de Somee (por ejemplo, 30 días sin consultas) | Las páginas muestran 503 | Crear una base vacía; restaurar la copia `.bak` de Somee **o** ejecutar `restaurar-respaldo` con el último respaldo de la bóveda; `aplicar-migraciones`; actualizar `ConnectionStrings__FixTrack` si cambió; *Restart* en Render |
| Se perdió el Mac | — | Usar la copia de recuperación del medio externo y las contraseñas del gestor (sección 3.1) |
| Certificado perdido o filtrado | Las sesiones existentes se pierden; los datos no | Generar un certificado nuevo (README); con él, `proteger-claves --revocar-ilegibles` contra FixTrackDB; actualizar las dos variables del certificado en Render y desplegar; todos inician sesión de nuevo |
| Rotación planificada del certificado | Sin interrupción | Configurar el certificado nuevo **y** el anterior como `DataProtection__PreviousCertificates__0__Base64` y `__Password`; desplegar. Las claves nuevas se cifran con el certificado nuevo. Quitar el anterior cuando venza la última clave cifrada con él (hasta 90 días): si se quita antes, la aplicación no arranca y lo indica |

**Estrategia de rotación del certificado:**

- Vence el 2031-10-20. Data Protection no valida la vigencia: un certificado vencido sigue funcionando (hay una prueba).
- Desde 90 días antes, la aplicación avisa en la bitácora al iniciar.
- Plan: revisión anual y rotación planificada antes del vencimiento. Rotación inmediata si el PFX o su contraseña pudieron filtrarse.

## 7. Limitaciones del plan gratuito

**Render Free** (0.1 CPU, 512 MB):

- El servicio se suspende tras 15 minutos sin visitas. Render tarda alrededor de un minuto en reactivarlo.
- Medido localmente con esos recursos y la imagen ReadyToRun:
  - la aplicación escucha a los 16–18 s;
  - el primer inicio de sesión en frío tarda unos 15 s;
  - después, cada página tarda 0.2–0.7 s y un inicio de sesión unos 1.6 s (el cálculo del hash de la contraseña);
  - la memoria ronda los 74 MiB.
- Hay 750 horas gratuitas al mes por espacio de trabajo; un servicio siempre activo usa unas 744 en un mes de 31 días.
- No tiene disco persistente (las claves viven en la base), consola ni tareas puntuales: los comandos se ejecutan desde el Mac. Bloquea SMTP (FixTrack no envía correos).

**Somee gratuito:**

- Límite de 30 MB: hoy se usan unos 6.3 MB; la auditoría crece con el uso.
- Una base sin consultas durante 30 días se elimina, aunque se conservan sus copias diarias.
- Con Render suspendido no hay consultas: si el taller deja de usar FixTrack un mes, la base corre ese riesgo.

**Mantener el servicio despierto** con un monitor externo gratuito (una solicitud a `/salud` cada 10–14 minutos) evitaría las esperas y mantendría las horas dentro del límite. Es un servicio externo: decisión del propietario, no configurado.

## 8. Después del despliegue

- [ ] Cada semana, o antes de cualquier cambio: `exportar-respaldo` en la bóveda y `verificar-respaldo`. Copiar la imagen de recuperación al medio externo una vez al mes.
- [ ] Revisar `/auditoria` y la bitácora de Render.
- [ ] Revisar en el panel de Somee el espacio usado y que existan sus copias diarias.
- [ ] Decidir qué hacer con las dos cuentas QA deshabilitadas (eliminarlas requiere autorización).
- [ ] Opcional: limitar `AllowedHosts` al dominio de Render. Antes, comprobar con qué encabezado `Host` llegan los health checks de Render, o se podrían rechazar.

## 9. Resumen de responsabilidades

**Completado y verificado en la fase 5:**

- certificado definitivo;
- bóveda y copia de recuperación cifradas;
- respaldo previo verificado y restaurado;
- ensayo completo de Data Protection en una copia;
- cifrado de las claves de FixTrackDB, con autorización y respaldos antes y después (3.3);
- auditoría de Production con la imagen;
- ensayo del descubrimiento del proxy;
- resiliencia;
- medición con los recursos de Render;
- accesibilidad;
- rol Consulta (solo lectura), ensayado sobre una copia de FixTrackDB;
- pruebas automáticas.

**Requiere al propietario:**

- guardar las contraseñas en un gestor y la copia de recuperación en un medio externo (3.1);
- integración a `main` por QA (3.4);
- aplicar la migración del rol Consulta y crear la cuenta de demostración (3.5);
- autorizar y crear el servicio en Render (4 y 5);
- crear y deshabilitar la cuenta Operador de prueba (5.4);
- configurar el proxy real (5.6);
- decidir sobre el monitor externo y las cuentas QA (7 y 8).

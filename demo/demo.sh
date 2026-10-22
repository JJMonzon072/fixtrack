#!/usr/bin/env bash
# FixTrack · demostración local con datos ficticios (Docker Compose). Ver docs/demo-portafolio.md.
#
#   ./demo/demo.sh iniciar       crea el entorno (la primera vez) y abre FixTrack en http://localhost:8080
#   ./demo/demo.sh credenciales  muestra las cuentas ficticias y sus contraseñas
#   ./demo/demo.sh verificar     comprueba la demostración en ejecución: datos, extras y permisos de cada rol
#   ./demo/demo.sh estado        muestra los contenedores
#   ./demo/demo.sh detener       detiene todo y conserva los datos
#   ./demo/demo.sh reiniciar     borra los datos y crea un entorno nuevo, con contraseñas nuevas
#   ./demo/demo.sh eliminar      borra contenedores, datos, contraseñas e imágenes de la demostración
#
# Las contraseñas se generan al azar en demo/.env (fuera de Git) cada vez que se crea un entorno nuevo.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$DIR/.env"
PROJECT="fixtrack-demo"

compose() {
  if [ -f "$ENV_FILE" ]; then
    docker compose --file "$DIR/compose.yaml" --env-file "$ENV_FILE" "$@"
  else
    # Sin demo/.env no hay entorno que levantar: valores de relleno solo para poder detener o borrar contenedores.
    DEMO_SA_PASSWORD=- DEMO_APP_PASSWORD=- DEMO_CERTIFICATE_PASSWORD=- DEMO_ADMIN_PASSWORD=- \
      DEMO_OPERATOR_PASSWORD=- DEMO_READONLY_PASSWORD=- docker compose --file "$DIR/compose.yaml" "$@"
  fi
}

value() {
  sed -n "s/^$1=//p" "$ENV_FILE"
}

# 24 caracteres aleatorios más un sufijo con mayúscula, minúscula y número (lo exigen SQL Server e Identity).
password() {
  local random
  random="$(head -c 512 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9' | cut -c 1-24)"
  printf '%sFx7' "$random"
}

require_docker() {
  if ! docker compose version > /dev/null 2>&1; then
    echo "Se necesita Docker con Compose v2 (Docker Desktop o Docker Engine)." >&2
    exit 1
  fi
  if ! docker info > /dev/null 2>&1; then
    echo "Docker no está en ejecución: inícialo y vuelve a intentarlo." >&2
    exit 1
  fi
}

create_env() {
  if docker volume inspect "${PROJECT}_datos-sql" > /dev/null 2>&1; then
    echo "Hay datos de una demostración anterior sin sus contraseñas (falta demo/.env): se borran para empezar de cero."
    compose down --volumes --remove-orphans
  fi
  umask 077
  cat > "$ENV_FILE" <<EOF
# Generado por demo/demo.sh el $(date '+%Y-%m-%d %H:%M'). Contraseñas aleatorias de esta demostración local:
# no las uses en otro lugar y no subas este archivo (está en .gitignore).
DEMO_PORT=${DEMO_PORT:-8080}
DEMO_SA_PASSWORD=$(password)
DEMO_APP_PASSWORD=$(password)
DEMO_CERTIFICATE_PASSWORD=$(password)
DEMO_ADMIN_PASSWORD=$(password)
DEMO_OPERATOR_PASSWORD=$(password)
DEMO_READONLY_PASSWORD=$(password)
EOF
  echo "Contraseñas nuevas generadas en demo/.env."
}

show_credentials() {
  if [ ! -f "$ENV_FILE" ]; then
    echo "No hay una demostración creada: ejecuta ./demo/demo.sh iniciar." >&2
    exit 1
  fi
  cat <<EOF

FixTrack (demostración local): http://localhost:$(value DEMO_PORT)

  Rol            Correo                            Contraseña
  Administrador  admin@fixtrack-demo.invalid       $(value DEMO_ADMIN_PASSWORD)
  Operador       operador@fixtrack-demo.invalid    $(value DEMO_OPERATOR_PASSWORD)
  Consulta       consulta@fixtrack-demo.invalid    $(value DEMO_READONLY_PASSWORD)

Todos los datos son ficticios. Probado con Chrome y Edge: la sesión usa una cookie segura, y un navegador que
no trate http://localhost como origen seguro (Safari puede ser el caso) no la guarda.
EOF
}

start() {
  require_docker
  [ -f "$ENV_FILE" ] || create_env
  local port
  port="$(value DEMO_PORT)"

  echo "Construyendo las imágenes y preparando la base (la primera vez tarda unos minutos)..."
  if ! compose up --build --detach app; then
    echo >&2
    echo "La demostración no pudo iniciarse. Últimas líneas de cada paso:" >&2
    compose logs --tail 20 preparar migraciones datos app >&2 || true
    exit 1
  fi

  echo "Esperando a FixTrack en http://localhost:$port ..."
  for _ in $(seq 1 90); do
    if curl --silent --fail "http://127.0.0.1:$port/salud/bd" > /dev/null 2>&1; then
      compose logs --no-log-prefix datos | grep -E "técnicos|ya contiene|cargados" || true
      show_credentials
      return 0
    fi
    sleep 2
  done
  echo "FixTrack no respondió a tiempo. Revisa: ./demo/demo.sh estado y docker compose logs app." >&2
  exit 1
}

# Comprobaciones con curl contra la demostración en ejecución (también las usa GitHub Actions).
verify() {
  [ -f "$ENV_FILE" ] || { echo "No hay una demostración creada: ejecuta ./demo/demo.sh iniciar." >&2; exit 1; }
  local base jars failures=0
  base="http://localhost:$(value DEMO_PORT)"
  jars="$(mktemp -d)"

  check() {
    if [ "$2" = "$3" ]; then
      echo "  ok     $1"
    else
      echo "  FALLA  $1: se esperaba $3 y se obtuvo $2"
      failures=$((failures + 1))
    fi
  }
  status() {
    curl --silent --output /dev/null --write-out '%{http_code}' "$@"
  }
  token() {
    curl --silent --cookie "$1" --cookie-jar "$1" "$base$2" \
      | sed -n 's/.*name="__RequestVerificationToken" type="hidden" value="\([^"]*\)".*/\1/p' | head -n 1
  }
  login() {
    local jar="$jars/$1"
    status --cookie "$jar" --cookie-jar "$jar" --data-urlencode "Email=$2" --data-urlencode "Password=$3" \
      --data-urlencode "__RequestVerificationToken=$(token "$jar" /cuenta/iniciar-sesion)" "$base/cuenta/iniciar-sesion"
  }

  echo "Comprobando $base"
  check "la base responde (/salud/bd)" "$(status "$base/salud/bd")" 200
  check "la API sin sesión responde 401" "$(status "$base/api/v1/ordenes")" 401

  check "Administrador inicia sesión" "$(login admin admin@fixtrack-demo.invalid "$(value DEMO_ADMIN_PASSWORD)")" 302
  check "Administrador abre la auditoría" "$(status --cookie "$jars/admin" "$base/auditoria")" 200
  check "Administrador abre los reportes (extra habilitado)" "$(status --cookie "$jars/admin" "$base/reportes")" 200
  local orders
  orders="$(curl --silent --cookie "$jars/admin" "$base/api/v1/ordenes?porPagina=1" | sed -n 's/.*"total":\([0-9]*\).*/\1/p')"
  check "la API devuelve las órdenes ficticias (25 o más)" "$([ "${orders:-0}" -ge 25 ] && echo si || echo "${orders:-ninguna}")" si

  check "Operador inicia sesión" "$(login operador operador@fixtrack-demo.invalid "$(value DEMO_OPERATOR_PASSWORD)")" 302
  check "Operador no administra usuarios" "$(status --cookie "$jars/operador" "$base/usuarios")" 403
  check "Operador abre una ficha imprimible (extra habilitado)" "$(status --cookie "$jars/operador" "$base/fichas/recepcion/1")" 200

  check "Consulta inicia sesión" "$(login consulta consulta@fixtrack-demo.invalid "$(value DEMO_READONLY_PASSWORD)")" 302
  check "Consulta ve las órdenes" "$(status --cookie "$jars/consulta" "$base/ordenes")" 200
  check "Consulta no puede registrar clientes" "$(status --cookie "$jars/consulta" --cookie-jar "$jars/consulta" \
    --data-urlencode "FullName=Intento de la cuenta de consulta" --data-urlencode "Phone=0000-0999" \
    --data-urlencode "__RequestVerificationToken=$(token "$jars/consulta" /ordenes)" "$base/clientes/nuevo")" 403

  rm -rf "$jars"
  if [ "$failures" -gt 0 ]; then
    echo "$failures comprobación(es) fallaron." >&2
    exit 1
  fi
  echo "Todo en orden: datos ficticios, extras habilitados y permisos de cada rol."
}

case "${1:-}" in
  iniciar)
    start
    ;;
  credenciales)
    show_credentials
    ;;
  verificar)
    verify
    ;;
  estado)
    require_docker
    compose ps --all
    ;;
  detener)
    require_docker
    compose down
    echo "Demostración detenida. Los datos se conservan: ./demo/demo.sh iniciar la vuelve a abrir."
    ;;
  reiniciar)
    require_docker
    compose down --volumes --remove-orphans
    rm -f "$ENV_FILE"
    start
    ;;
  eliminar)
    require_docker
    compose down --volumes --remove-orphans
    docker image rm --force fixtrack-demo/app:local fixtrack-demo/herramienta:local > /dev/null 2>&1 || true
    # Versiones anteriores de esas imágenes que quedaron sin etiqueta al reconstruirlas: solo las de este proyecto.
    docker image prune --force --filter "label=com.docker.compose.project=$PROJECT" > /dev/null 2>&1 || true
    rm -f "$ENV_FILE"
    echo "Demostración eliminada: contenedores, datos, contraseñas e imágenes propias. La imagen de SQL Server se conserva."
    ;;
  *)
    sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac

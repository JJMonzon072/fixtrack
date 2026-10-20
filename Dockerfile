# FixTrack: imagen de producción para un servicio como Render (sin IIS).
# Construcción en dos etapas: el SDK compila y publica; la imagen final solo lleva el runtime de ASP.NET Core.
#
#   docker build -t fixtrack .
#   docker run -p 8080:8080 -e ConnectionStrings__FixTrack="..." fixtrack
#
# La imagen no contiene secretos: la cadena de conexión y cualquier credencial llegan por variables de entorno.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero los archivos de proyecto: la restauración se reutiliza mientras no cambien las dependencias.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FixTrack.Domain/FixTrack.Domain.csproj src/FixTrack.Domain/
COPY src/FixTrack.Application/FixTrack.Application.csproj src/FixTrack.Application/
COPY src/FixTrack.Infrastructure/FixTrack.Infrastructure.csproj src/FixTrack.Infrastructure/
COPY src/FixTrack.Web/FixTrack.Web.csproj src/FixTrack.Web/
RUN dotnet restore src/FixTrack.Web/FixTrack.Web.csproj

COPY src/ src/
RUN dotnet publish src/FixTrack.Web/FixTrack.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app \
    -p:UseAppHost=false

# Imagen "chiseled": sin shell ni gestor de paquetes, y con usuario sin privilegios (app, UID 1654).
# La variante "extra" incluye ICU y la base de zonas horarias: la interfaz usa la cultura "es" y la variable TZ.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

# Los encabezados X-Forwarded-* solo se aceptan desde la red interna del proxy (Render usa direcciones 10.x).
# Si la plataforma define PORT, la aplicación escucha en ese puerto en lugar de 8080.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    TZ=America/Guatemala \
    ReverseProxy__KnownNetworks__0=10.0.0.0/8

EXPOSE 8080
COPY --from=build /app ./
USER $APP_UID

ENTRYPOINT ["dotnet", "FixTrack.Web.dll"]

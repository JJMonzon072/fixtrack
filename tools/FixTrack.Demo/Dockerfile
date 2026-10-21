# Herramienta de la demostración local (demo/compose.yaml). No se publica ni se usa en Render.
# Se construye desde la raíz del repositorio:  docker build -f tools/FixTrack.Demo/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/FixTrack.Domain/FixTrack.Domain.csproj src/FixTrack.Domain/
COPY src/FixTrack.Application/FixTrack.Application.csproj src/FixTrack.Application/
COPY src/FixTrack.Infrastructure/FixTrack.Infrastructure.csproj src/FixTrack.Infrastructure/
COPY tools/FixTrack.Demo/FixTrack.Demo.csproj tools/FixTrack.Demo/
RUN dotnet restore tools/FixTrack.Demo/FixTrack.Demo.csproj -a $TARGETARCH

COPY src/FixTrack.Domain/ src/FixTrack.Domain/
COPY src/FixTrack.Application/ src/FixTrack.Application/
COPY src/FixTrack.Infrastructure/ src/FixTrack.Infrastructure/
COPY tools/FixTrack.Demo/ tools/FixTrack.Demo/
RUN dotnet publish tools/FixTrack.Demo/FixTrack.Demo.csproj \
    --configuration Release \
    --no-restore \
    -a $TARGETARCH \
    --self-contained false \
    --output /app \
    -p:UseAppHost=false \
 && mkdir /certificado

# La misma base que la imagen de FixTrack (comparten capas) y el mismo usuario sin privilegios: el certificado que crea
# esta herramienta en el volumen compartido solo lo puede leer esa cuenta.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
ENV TZ=America/Guatemala
COPY --from=build /app ./
COPY --from=build --chown=$APP_UID:$APP_UID /certificado /certificado
USER $APP_UID

ENTRYPOINT ["dotnet", "FixTrack.Demo.dll"]

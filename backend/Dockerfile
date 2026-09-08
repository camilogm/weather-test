# ---------------------------------------------------------------- build stage
# The SDK is a build tool, not a runtime. It ships compilers, NuGet and the whole
# toolchain, and none of that belongs in the image that faces the network.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first, on their own layer: as long as no dependency changes,
# Docker reuses the restore instead of downloading NuGet again on every edit.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/WeatherService.Api/WeatherService.Api.csproj src/WeatherService.Api/
COPY src/WeatherService.Application/WeatherService.Application.csproj src/WeatherService.Application/
COPY src/WeatherService.Infrastructure/WeatherService.Infrastructure.csproj src/WeatherService.Infrastructure/

RUN dotnet restore src/WeatherService.Api/WeatherService.Api.csproj

COPY src/ src/
RUN dotnet publish src/WeatherService.Api/WeatherService.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app

# -------------------------------------------------------------- runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# The aspnet runtime image ships neither curl nor wget, so a container health
# check has nothing to run. Added here rather than left to compose, because a
# probe that cannot execute reports "unhealthy" forever against a working service.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

# The base image ships a non-root account; there is no reason to run as root.
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=5 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "WeatherService.Api.dll"]

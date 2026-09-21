# Build context is the repo root (needed for Directory.Build.props / Directory.Packages.props
# central package management), so this is invoked as:
#   docker build -f src/BadgeButler.Api/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/BadgeButler.Api/BadgeButler.Api.csproj src/BadgeButler.Api/
RUN dotnet restore src/BadgeButler.Api/BadgeButler.Api.csproj

COPY src/BadgeButler.Api/ src/BadgeButler.Api/
RUN dotnet publish src/BadgeButler.Api/BadgeButler.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

# Chiseled image already runs as the non-root "app" user and has no shell/package manager.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

COPY --from=build /app .

ENTRYPOINT ["dotnet", "BadgeButler.Api.dll"]

# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
WORKDIR /src

RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    echo "$RID" > /tmp/rid

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/BadgeButler.Api/ src/BadgeButler.Api/

RUN dotnet restore src/BadgeButler.Api/BadgeButler.Api.csproj -r "$(cat /tmp/rid)"
RUN --mount=type=secret,id=sixlabors_license,env=SixLaborsLicenseKey \
    dotnet publish src/BadgeButler.Api/BadgeButler.Api.csproj -r "$(cat /tmp/rid)" \
    -c Release \
    -o /app \
    --self-contained false \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

LABEL org.opencontainers.image.source="https://gitea.team5software.de/t5s/badge-butler" \
      org.opencontainers.image.description="Stores and serves status badges" \
      org.opencontainers.image.licenses="MIT"

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app .

ENTRYPOINT ["dotnet", "BadgeButler.Api.dll"]

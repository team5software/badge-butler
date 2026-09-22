# Build context is the repo root (needed for Directory.Build.props / Directory.Packages.props
# central package management), so this is invoked as:
#   docker build -f src/BadgeButler.Api/Dockerfile .
#
# --platform=$BUILDPLATFORM pins the build stage to the *builder's* native architecture even
# when buildx is producing a different TARGETPLATFORM - the .NET SDK/MSBuild is a heavy,
# multi-threaded process that reliably crashes under QEMU emulation (EventSource/reflection
# failures inside System.Threading.Tasks.Dataflow). Cross-compiling via `-r <rid>
# --self-contained false` instead means dotnet itself always runs natively; only a RID-specific,
# non-executed output gets copied into the (correctly emulated-or-native) runtime stage.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/BadgeButler.Api/BadgeButler.Api.csproj src/BadgeButler.Api/
RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    echo "$RID" > /tmp/rid && \
    dotnet restore src/BadgeButler.Api/BadgeButler.Api.csproj -r "$RID"

COPY src/BadgeButler.Api/ src/BadgeButler.Api/
RUN dotnet publish src/BadgeButler.Api/BadgeButler.Api.csproj \
    -c Release \
    -o /app \
    -r "$(cat /tmp/rid)" \
    --self-contained false \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app

# Points at the GitHub mirror, not the canonical Gitea source - GHCR only auto-links a package
# to a repo page (README, visibility, "used by") when this annotation resolves to a github.com
# repo it can see, so it has to be this URL specifically for that to work.
LABEL org.opencontainers.image.source="https://github.com/team5software/badge-butler" \
      org.opencontainers.image.description="Stores and serves status badges (build, coverage, etc.) for repos" \
      org.opencontainers.image.licenses="MIT"

# Chiseled image already runs as the non-root "app" user and has no shell/package manager.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

COPY --from=build /app .

ENTRYPOINT ["dotnet", "BadgeButler.Api.dll"]

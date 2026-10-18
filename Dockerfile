# syntax=docker/dockerfile:1
# The Shipping host, and the per-module migration bundles the local environment applies before it (SLS-126).
# Build from the repository root. Targets:
#   api       (default) the ASP.NET Core host only: no SDK, no EF tooling, non-root
#   migrator  one EF Core migration bundle per module, applied in turn by compose.yaml in Development
# No connection string or other secret is baked into either image; both read it from the environment.

ARG DOTNET_SDK_VERSION=10.0.402
ARG DOTNET_RUNTIME_VERSION=10.0.13

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
COPY Directory.Build.props Shipping.slnx dotnet-tools.json ./
COPY src/ src/
RUN --mount=type=cache,id=shipping-nuget,target=/root/.nuget/packages \
    dotnet restore src/Shipping.Platform.Api/Shipping.Platform.Api.csproj
RUN --mount=type=cache,id=shipping-nuget,target=/root/.nuget/packages \
    dotnet publish src/Shipping.Platform.Api/Shipping.Platform.Api.csproj \
        -c Release --no-restore -o /app/publish /p:UseAppHost=false

# One bundle per module, found by the ADR-002 convention (Shipping.Modules.<M>.Infrastructure owns <M>DbContext),
# so a new module is migrated without editing this file. Each bundle builds its context through the module's
# design-time factory, which reads ConnectionStrings__PostgreSql only when the bundle runs. The value set for
# the build command is never opened.
FROM build AS bundles
RUN --mount=type=cache,id=shipping-nuget,target=/root/.nuget/packages \
    dotnet tool restore \
 && mkdir /bundles \
 && for project in src/Modules/*/Shipping.Modules.*.Infrastructure/Shipping.Modules.*.Infrastructure.csproj; do \
        module="$(basename "$project" .csproj | sed -E 's/^Shipping\.Modules\.(.+)\.Infrastructure$/\1/')"; \
        echo "Bundling ${module}DbContext"; \
        ConnectionStrings__PostgreSql="Host=build.invalid" dotnet ef migrations bundle \
            --project "$project" --startup-project src/Shipping.Platform.Api/Shipping.Platform.Api.csproj \
            --context "${module}DbContext" --configuration Release \
            --output "/bundles/$(echo "$module" | tr '[:upper:]' '[:lower:]')-efbundle" || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS migrator
WORKDIR /migrations
COPY --from=bundles /bundles/ ./
USER $APP_UID
# Applies each module's bundle in turn, each to its own schema and history. A bundle applies only migrations
# missing from its module's history, so a re-run is a no-op. The first failure stops the run with its exit code.
ENTRYPOINT ["/bin/sh", "-ec", "for bundle in /migrations/*-efbundle; do echo \"Applying $bundle\"; \"$bundle\"; done"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_VERSION} AS api
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shipping.Platform.Api.dll"]

# syntax=docker/dockerfile:1
# Base images are pinned by digest for reproducible builds; Dependabot (docker ecosystem) bumps them.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /source

# Every step that restores or builds mounts the same NuGet cache, so locally packages survive layer
# invalidation (any source change) and later steps find what the restore step downloaded.
COPY .config/dotnet-tools.json .config/
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Domain/ArturRios.Fortuna.Domain/ArturRios.Fortuna.Domain.csproj src/Domain/ArturRios.Fortuna.Domain/
COPY src/Application/ArturRios.Fortuna.Command/ArturRios.Fortuna.Command.csproj src/Application/ArturRios.Fortuna.Command/
COPY src/Application/ArturRios.Fortuna.Query/ArturRios.Fortuna.Query.csproj src/Application/ArturRios.Fortuna.Query/
COPY src/Application/ArturRios.Fortuna.Shared/ArturRios.Fortuna.Shared.csproj src/Application/ArturRios.Fortuna.Shared/
COPY src/Infrastructure/ArturRios.Fortuna.Data/ArturRios.Fortuna.Data.csproj src/Infrastructure/ArturRios.Fortuna.Data/
COPY src/Infrastructure/ArturRios.Fortuna.Data.Sqlite.Migrations/ArturRios.Fortuna.Data.Sqlite.Migrations.csproj src/Infrastructure/ArturRios.Fortuna.Data.Sqlite.Migrations/
COPY src/Infrastructure/ArturRios.Fortuna.Integration/ArturRios.Fortuna.Integration.csproj src/Infrastructure/ArturRios.Fortuna.Integration/
COPY src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj src/Presentation/ArturRios.Fortuna.WebApi/
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj

COPY src/ src/
# Publish restores again rather than passing --no-restore: CI keeps the restore layer in its layer
# cache but not the cache mount, so a source-only change would otherwise publish against an empty
# package folder. When the mount already holds the packages, this restore is a no-op.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj \
    --configuration Release --output /app
RUN find /app -name '.env*' -delete
# The placeholder connection string only satisfies the design-time factory while bundling; the
# bundle reads the real one from FORTUNA_DATA_CONNECTIONSTRING when it runs (docker/entrypoint.sh).
# dotnet-ef is restored in this same step: a tool restored by an earlier step lives only in the cache
# mount, which CI does not keep, so a cached restore layer would leave this step without the tool.
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet tool restore && \
    FORTUNA_DATA_CONNECTIONSTRING="Host=localhost;Database=fortuna;Username=postgres;Search Path=fortuna" \
    dotnet ef migrations bundle \
    --project src/Infrastructure/ArturRios.Fortuna.Data \
    --startup-project src/Infrastructure/ArturRios.Fortuna.Data \
    --configuration Release \
    --output /app/fortuna-migrate \
    --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh /app/fortuna-migrate \
    && mkdir -p /app/logs /app/attachments \
    && chown -R $APP_UID:$APP_UID /app/logs /app/attachments
USER $APP_UID
# 8080 is the public API behind the reverse proxy. Prometheus scrapes GET /metrics on the private
# FORTUNA_METRICS_PORT (default 9464) over the private network only. The base image presets
# ASPNETCORE_HTTP_PORTS=8080; it is cleared here so docker/entrypoint.sh derives the listeners from
# FORTUNA_METRICS_PORT. Setting ASPNETCORE_HTTP_PORTS explicitly at run time still takes precedence.
ENV ASPNETCORE_HTTP_PORTS=
EXPOSE 8080 9464
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "ArturRios.Fortuna.WebApi.dll"]

# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY .config/dotnet-tools.json .config/
RUN dotnet tool restore
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Domain/ArturRios.Fortuna.Domain/ArturRios.Fortuna.Domain.csproj src/Domain/ArturRios.Fortuna.Domain/
COPY src/Application/ArturRios.Fortuna.Command/ArturRios.Fortuna.Command.csproj src/Application/ArturRios.Fortuna.Command/
COPY src/Application/ArturRios.Fortuna.Query/ArturRios.Fortuna.Query.csproj src/Application/ArturRios.Fortuna.Query/
COPY src/Application/ArturRios.Fortuna.Shared/ArturRios.Fortuna.Shared.csproj src/Application/ArturRios.Fortuna.Shared/
COPY src/Infrastructure/ArturRios.Fortuna.Data/ArturRios.Fortuna.Data.csproj src/Infrastructure/ArturRios.Fortuna.Data/
COPY src/Infrastructure/ArturRios.Fortuna.Integration/ArturRios.Fortuna.Integration.csproj src/Infrastructure/ArturRios.Fortuna.Integration/
COPY src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj src/Presentation/ArturRios.Fortuna.WebApi/
RUN dotnet restore src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj -m:1

COPY src/ src/
RUN dotnet publish src/Presentation/ArturRios.Fortuna.WebApi/ArturRios.Fortuna.WebApi.csproj \
    --configuration Release --no-restore --output /app -m:1
RUN find /app -name '.env*' -delete
RUN FORTUNA_DATA_CONNECTIONSTRING="Host=localhost;Database=fortuna;Username=postgres;Search Path=fortuna" \
    dotnet ef migrations bundle \
    --project src/Infrastructure/ArturRios.Fortuna.Data \
    --startup-project src/Infrastructure/ArturRios.Fortuna.Data \
    --configuration Release \
    --output /app/fortuna-migrate \
    --force

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
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
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "ArturRios.Fortuna.WebApi.dll"]

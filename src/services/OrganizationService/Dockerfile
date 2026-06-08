# --- Build stage ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy proto files (needed by Grpc.Tools at build)
COPY proto/ proto/

# Copy solution and project files for restore (cache optimization)
COPY services/HRSystem.sln services/
COPY services/Shared/HRSystem.Shared/HRSystem.Shared.csproj services/Shared/HRSystem.Shared/
COPY services/ServiceDefaults/HRSystem.ServiceDefaults/HRSystem.ServiceDefaults.csproj services/ServiceDefaults/HRSystem.ServiceDefaults/
COPY services/OrganizationService/OrganizationService.csproj services/OrganizationService/

RUN dotnet restore services/OrganizationService/OrganizationService.csproj

# Copy all source
COPY services/Shared/ services/Shared/
COPY services/ServiceDefaults/ services/ServiceDefaults/
COPY services/OrganizationService/ services/OrganizationService/

RUN dotnet publish services/OrganizationService/OrganizationService.csproj -c Release -o /app/publish

# --- Runtime stage ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

RUN mkdir -p /tmp /data && \
    chown -R app:app /app /tmp /data

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    GRPC_PORT=50053

EXPOSE 50053

USER app:app

ENTRYPOINT ["dotnet", "OrganizationService.dll"]

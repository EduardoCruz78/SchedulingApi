# syntax=docker/dockerfile:1

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first: the restore layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Scheduling.Domain/Scheduling.Domain.csproj src/Scheduling.Domain/
COPY src/Scheduling.Application/Scheduling.Application.csproj src/Scheduling.Application/
COPY src/Scheduling.Infrastructure/Scheduling.Infrastructure.csproj src/Scheduling.Infrastructure/
COPY src/Scheduling.Api/Scheduling.Api.csproj src/Scheduling.Api/
RUN dotnet restore src/Scheduling.Api/Scheduling.Api.csproj

COPY src/ src/
RUN dotnet publish src/Scheduling.Api/Scheduling.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ---------- Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=build /app/publish .

# Non-root user that ships with the official .NET images.
USER $APP_UID

ENTRYPOINT ["dotnet", "Scheduling.Api.dll"]

# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached unless dependencies change.
COPY src/Lakaskezelo.Domain/Lakaskezelo.Domain.csproj src/Lakaskezelo.Domain/
COPY src/Lakaskezelo.Data/Lakaskezelo.Data.csproj src/Lakaskezelo.Data/
COPY src/Lakaskezelo.Api/Lakaskezelo.Api.csproj src/Lakaskezelo.Api/
RUN dotnet restore src/Lakaskezelo.Api/Lakaskezelo.Api.csproj

COPY src/ src/
RUN dotnet publish src/Lakaskezelo.Api/Lakaskezelo.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Run as non-root.
RUN adduser --disabled-password --gecos "" appuser
USER appuser

COPY --chown=appuser:appuser --from=build /app .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Render performs its own HTTP health checks against /health (configured via render.yaml's
# healthCheckPath) — no Docker-level HEALTHCHECK needed here.

ENTRYPOINT ["dotnet", "Lakaskezelo.Api.dll"]

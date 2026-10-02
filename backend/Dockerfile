# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached unless dependencies change.
COPY src/Lakaskezelo.Domain/Lakaskezelo.Domain.csproj src/Lakaskezelo.Domain/
COPY src/Lakaskezelo.Data/Lakaskezelo.Data.csproj src/Lakaskezelo.Data/
COPY src/Lakaskezelo.Api/Lakaskezelo.Api.csproj src/Lakaskezelo.Api/
RUN dotnet restore src/Lakaskezelo.Api/Lakaskezelo.Api.csproj

COPY src/ src/
RUN dotnet publish src/Lakaskezelo.Api/Lakaskezelo.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root — the .NET 8+ images ship a built-in unprivileged "app" user.
USER app

COPY --chown=app:app --from=build /app .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Health: the /health endpoint is proxied by nginx (deploy/nginx.conf); no Docker-level
# HEALTHCHECK needed.

ENTRYPOINT ["dotnet", "Lakaskezelo.Api.dll"]

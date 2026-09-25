FROM mcr.microsoft.com/dotnet/sdk:8.0.425 AS build
WORKDIR /source
COPY . .
RUN dotnet restore Salon.sln --locked-mode
RUN dotnet publish src/Salon.Api/Salon.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0.31-bookworm-slim AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /home/app/keys && chown app:app /home/app/keys
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER app
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 CMD curl --fail --silent http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Salon.Api.dll"]

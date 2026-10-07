# ---------- Stage 1: runtime base (ASP.NET runtime is pulled here) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=18080
EXPOSE 18080

# ---------- Stage 2: build (full SDK is pulled here, discarded after build) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TaskApi.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish TaskApi.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---------- Stage 3: final image = runtime base + published app ----------
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# SQLite file lives in /data, which the non-root "app" user must be able to write.
RUN mkdir -p /data && chown app:app /data
ENV ConnectionStrings__Default="Data Source=/data/tasks.db"

USER app
ENTRYPOINT ["dotnet", "TaskApi.dll"]

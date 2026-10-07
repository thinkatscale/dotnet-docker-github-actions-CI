# ---------- Stage 1: runtime base (ASP.NET runtime is pulled here) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# .NET 8+ images listen on 8080 by default and ship a non-root "app" user.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# ---------- Stage 2: build (full SDK is pulled here, discarded after build) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TaskApi.csproj .
RUN dotnet restore

COPY . .
RUN dotnet publish TaskApi.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Stage 3: final image = runtime base + published app ----------
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# SQLite file lives in /data. Create it and give the non-root user write access,
# otherwise SQLite fails with "unable to open database file" / "readonly database".
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
ENV ConnectionStrings__Default="Data Source=/data/tasks.db"

USER $APP_UID
ENTRYPOINT ["dotnet", "TaskApi.dll"]

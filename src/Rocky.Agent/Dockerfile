# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer — copy only csproj files first for cache efficiency
COPY src/Mediahost.Shared/Mediahost.Shared.csproj     src/Mediahost.Shared/
COPY src/Mediahost.Vault/Mediahost.Vault.csproj       src/Mediahost.Vault/
COPY src/Mediahost.Llm/Mediahost.Llm.csproj           src/Mediahost.Llm/
COPY src/Mediahost.Tools/Mediahost.Tools.csproj       src/Mediahost.Tools/
COPY src/Mediahost.Agents/Mediahost.Agents.csproj     src/Mediahost.Agents/
COPY src/Rocky.Agent/Rocky.Agent.csproj               src/Rocky.Agent/
RUN dotnet restore src/Rocky.Agent/Rocky.Agent.csproj -r linux-x64

# Copy source and publish
COPY src/Mediahost.Shared/  src/Mediahost.Shared/
COPY src/Mediahost.Vault/   src/Mediahost.Vault/
COPY src/Mediahost.Llm/     src/Mediahost.Llm/
COPY src/Mediahost.Tools/   src/Mediahost.Tools/
COPY src/Mediahost.Agents/  src/Mediahost.Agents/
COPY src/Rocky.Agent/       src/Rocky.Agent/
RUN dotnet publish src/Rocky.Agent/Rocky.Agent.csproj \
      -c Release -o /app/publish -r linux-x64 --self-contained false

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Timezone data (required for TimeZoneInfo on Alpine)
RUN apk add --no-cache tzdata

# Non-root user
RUN addgroup --gid 1001 appuser && \
    adduser  --uid 1001 --ingroup appuser --disabled-password --gecos "" appuser && \
    chown -R appuser:appuser /app

COPY --from=build --chown=appuser:appuser /app/publish .

USER appuser
EXPOSE 5006
ENV ASPNETCORE_URLS=http://+:5006

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:5006/health || exit 1

ENTRYPOINT ["dotnet", "Rocky.Agent.dll"]

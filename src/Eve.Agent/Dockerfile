# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer — copy only csproj files first for cache efficiency
COPY src/Mediahost.Shared/Mediahost.Shared.csproj     src/Mediahost.Shared/
COPY src/Mediahost.Vault/Mediahost.Vault.csproj       src/Mediahost.Vault/
COPY src/Mediahost.Llm/Mediahost.Llm.csproj           src/Mediahost.Llm/
COPY src/Mediahost.Agents/Mediahost.Agents.csproj     src/Mediahost.Agents/
COPY src/Eve.Agent/Eve.Agent.csproj                   src/Eve.Agent/
RUN dotnet restore src/Eve.Agent/Eve.Agent.csproj -r linux-x64

# Copy source and publish
COPY src/Mediahost.Shared/  src/Mediahost.Shared/
COPY src/Mediahost.Vault/   src/Mediahost.Vault/
COPY src/Mediahost.Llm/     src/Mediahost.Llm/
COPY src/Mediahost.Agents/  src/Mediahost.Agents/
COPY src/Eve.Agent/         src/Eve.Agent/
RUN dotnet publish src/Eve.Agent/Eve.Agent.csproj \
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
EXPOSE 5003
ENV ASPNETCORE_URLS=http://+:5003

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:5003/health || exit 1

ENTRYPOINT ["dotnet", "Eve.Agent.dll"]

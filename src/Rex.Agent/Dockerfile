# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer — copy only csproj files first for cache efficiency
COPY src/Mediahost.Shared/Mediahost.Shared.csproj     src/Mediahost.Shared/
COPY src/Mediahost.Vault/Mediahost.Vault.csproj       src/Mediahost.Vault/
COPY src/Mediahost.Llm/Mediahost.Llm.csproj           src/Mediahost.Llm/
COPY src/Mediahost.Agents/Mediahost.Agents.csproj     src/Mediahost.Agents/
COPY src/Rex.Agent/Rex.Agent.csproj                   src/Rex.Agent/
RUN dotnet restore src/Rex.Agent/Rex.Agent.csproj -r linux-x64

# Copy source and publish
COPY src/Mediahost.Shared/  src/Mediahost.Shared/
COPY src/Mediahost.Vault/   src/Mediahost.Vault/
COPY src/Mediahost.Llm/     src/Mediahost.Llm/
COPY src/Mediahost.Agents/  src/Mediahost.Agents/
COPY src/Rex.Agent/         src/Rex.Agent/
RUN dotnet publish src/Rex.Agent/Rex.Agent.csproj \
      -c Release -o /app/publish -r linux-x64 --self-contained false

# ── Runtime stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# Timezone data, git, curl (git needed for git CLI operations)
RUN apk add --no-cache tzdata git curl

# Non-root user
RUN addgroup --gid 1001 appuser && \
    adduser  --uid 1001 --ingroup appuser --disabled-password --gecos "" appuser && \
    chown -R appuser:appuser /app

COPY --from=build --chown=appuser:appuser /app/publish .

USER appuser
EXPOSE 5005
ENV ASPNETCORE_URLS=http://+:5005

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:5005/health || exit 1

ENTRYPOINT ["dotnet", "Rex.Agent.dll"]

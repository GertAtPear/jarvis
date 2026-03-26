# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer — copy only csproj files first for cache efficiency
COPY src/Mediahost.Shared/Mediahost.Shared.csproj   src/Mediahost.Shared/
COPY src/Mediahost.Vault/Mediahost.Vault.csproj     src/Mediahost.Vault/
COPY src/Mediahost.Llm/Mediahost.Llm.csproj         src/Mediahost.Llm/
COPY src/Browser.Agent/Browser.Agent.csproj         src/Browser.Agent/
RUN dotnet restore src/Browser.Agent/Browser.Agent.csproj -r linux-x64

# Copy source and publish
COPY src/Mediahost.Shared/ src/Mediahost.Shared/
COPY src/Mediahost.Vault/  src/Mediahost.Vault/
COPY src/Mediahost.Llm/    src/Mediahost.Llm/
COPY src/Browser.Agent/    src/Browser.Agent/
RUN dotnet publish src/Browser.Agent/Browser.Agent.csproj \
      -c Release -o /app/publish \
      --self-contained true -r linux-x64

# ── Runtime stage ──────────────────────────────────────────────────────────────
# Playwright's runtime image includes Chromium, Firefox, WebKit pre-installed
FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-jammy AS runtime
WORKDIR /app

# Non-root user (Ubuntu/Debian syntax)
RUN groupadd --gid 1001 appuser && \
    useradd  --uid 1001 --gid appuser --no-create-home --shell /usr/sbin/nologin appuser && \
    chown -R appuser:appuser /app

COPY --from=build --chown=appuser:appuser /app/publish .

USER appuser
EXPOSE 5004
ENV ASPNETCORE_URLS=http://+:5004

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:5004/health || exit 1

ENTRYPOINT ["dotnet", "Browser.Agent.dll"]

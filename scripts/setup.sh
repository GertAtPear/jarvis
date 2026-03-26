#!/bin/bash
set -e

echo "=== Mediahost AI Platform Setup ==="

# Check prerequisites
command -v podman >/dev/null || { echo "ERROR: Podman not found. Install with: sudo apt install podman"; exit 1; }
podman compose version >/dev/null 2>&1 || { echo "ERROR: podman-compose not found. Install with: pip3 install podman-compose"; exit 1; }
command -v openssl >/dev/null || { echo "ERROR: openssl not found"; exit 1; }

# Load domain names from nginx.env if it exists
AI_DOMAIN="ai.mediahost.co.za"
VAULT_DOMAIN="vault.mediahost.co.za"
[ -f nginx/nginx.env ] && source nginx/nginx.env

# Create required directories
mkdir -p logs/{jarvis,andrew,eve,browser} ssh-keys db/init nginx/{certs/ai,certs/vault} scripts

# Generate .env if not exists
if [ ! -f .env ]; then
  cp .env.example .env

  # Replace each placeholder with a unique strong password
  for PLACEHOLDER in change_me_postgres change_me_redis change_me_infisical_db change_me_infisical_redis; do
    PASS=$(openssl rand -base64 24 | tr -dc 'a-zA-Z0-9' | head -c 32)
    sed -i "s/$PLACEHOLDER/$PASS/" .env
  done

  INFISICAL_KEY=$(openssl rand -hex 16)
  INFISICAL_SECRET=$(openssl rand -base64 48 | tr -dc 'a-zA-Z0-9' | head -c 48)
  sed -i "s|INFISICAL_ENCRYPTION_KEY=|INFISICAL_ENCRYPTION_KEY=$INFISICAL_KEY|" .env
  sed -i "s|INFISICAL_AUTH_SECRET=|INFISICAL_AUTH_SECRET=$INFISICAL_SECRET|" .env

  echo "✓ .env created with generated passwords"
fi

# Generate dev certs if production certs are not yet present
if [ ! -f nginx/certs/ai/fullchain.pem ]; then
  echo "No TLS certs found — generating self-signed dev certs..."
  bash scripts/gen-dev-certs.sh
  echo ""
  echo "  ⚠  Self-signed certs generated. For production, run:"
  echo "     bash scripts/get-certs.sh"
  echo "  Add to /etc/hosts for local testing:"
  echo "     127.0.0.1  ai.mediahost.local  vault.mediahost.local"
  echo ""
fi

# Build all images (app service images are commented out; this builds infra images)
echo "Pulling base images..."
podman compose pull --ignore-buildable 2>/dev/null || true

# Start infrastructure services
echo "Starting infrastructure services (postgres, redis, infisical, nginx)..."
podman compose up -d

# Wait for services to become healthy
echo "Waiting for services to become healthy..."
for i in $(seq 1 24); do
  sleep 5
  HEALTHY=$(podman ps --filter health=healthy --format '{{.Names}}' 2>/dev/null | wc -l || echo 0)
  echo "  $i/24 — healthy containers: $HEALTHY"
  [ "$HEALTHY" -ge 2 ] && break
done

echo ""
podman compose ps
echo ""
echo "========================================"
echo "  Mediahost AI Platform — Infrastructure Running"
echo "========================================"
echo ""
echo "NEXT STEPS:"
echo "  1. Open vault:  https://$VAULT_DOMAIN  (or http://localhost:8080 locally)"
echo "     Create your admin account on first visit."
echo "  2. In Infisical: create a Project, then a Machine Identity."
echo "     Copy ClientId + ClientSecret into .env:"
echo "     INFISICAL_CLIENT_ID=..."
echo "     INFISICAL_CLIENT_SECRET=..."
echo "     INFISICAL_PROJECT_ID=..."
echo "  3. In Infisical secrets: add /apis/anthropic → api_key = sk-ant-..."
echo "  4. Uncomment the app services in docker-compose.yml, then:"
echo "     podman compose up -d"
echo "  5. Open Jarvis:  https://$AI_DOMAIN"
echo ""
echo "  For production TLS: bash scripts/get-certs.sh"

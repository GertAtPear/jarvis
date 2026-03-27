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
echo ""

# ─── Phase 4: Create Jarvis admin user ───────────────────────────────────────
# Run after postgres is healthy and 004_phase4.sql has been applied.
create_jarvis_admin() {
  echo "=== Creating Jarvis admin user ==="

  # Check if bcrypt python module is available
  if ! python3 -c "import bcrypt" 2>/dev/null; then
    echo "Installing bcrypt Python module..."
    pip3 install bcrypt --quiet
  fi

  # Generate a random 20-character alphanumeric password
  ADMIN_PASS=$(openssl rand -base64 32 | tr -dc 'a-zA-Z0-9' | head -c 20)

  # Generate bcrypt hash at cost 12
  ADMIN_HASH=$(python3 -c "
import bcrypt, sys
pw = sys.argv[1].encode()
print(bcrypt.hashpw(pw, bcrypt.gensalt(12)).decode())
" "$ADMIN_PASS")

  # Insert admin user and assign admin role
  podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai <<SQL
INSERT INTO jarvis_schema.users (username, display_name, email, password_hash)
VALUES ('gert', 'Gert', 'gert@mediahost.co.za', '$ADMIN_HASH')
ON CONFLICT (username) DO UPDATE
  SET password_hash = EXCLUDED.password_hash,
      display_name  = EXCLUDED.display_name;

INSERT INTO jarvis_schema.user_roles (user_id, role_id)
SELECT u.id, r.id
FROM jarvis_schema.users u, jarvis_schema.roles r
WHERE u.username = 'gert' AND r.role_name = 'admin'
ON CONFLICT DO NOTHING;
SQL

  echo ""
  echo "╔══════════════════════════════════════════════════════╗"
  echo "║  Jarvis Admin Password (save this — shown ONCE):    ║"
  echo "║                                                      ║"
  echo "║  Username: gert                                      ║"
  printf  "║  Password: %-42s║\n" "$ADMIN_PASS"
  echo "║                                                      ║"
  echo "║  Change this password after first login.             ║"
  echo "╚══════════════════════════════════════════════════════╝"
  echo ""
}

# Only create admin if the users table exists (i.e. 004_phase4.sql has been applied)
USER_TABLE_EXISTS=$(podman exec mediahost-ai-postgres psql -U mediahostai -d mediahostai -tAc \
  "SELECT 1 FROM information_schema.tables WHERE table_schema='jarvis_schema' AND table_name='users'" 2>/dev/null || echo "")

if [ "$USER_TABLE_EXISTS" = "1" ]; then
  # Check if admin user already exists
  ADMIN_EXISTS=$(podman exec mediahost-ai-postgres psql -U mediahostai -d mediahostai -tAc \
    "SELECT 1 FROM jarvis_schema.users WHERE username='gert'" 2>/dev/null || echo "")

  if [ "$ADMIN_EXISTS" = "1" ]; then
    echo "ℹ  Jarvis admin user 'gert' already exists. To reset password, re-run: bash scripts/setup.sh --reset-admin"
  else
    create_jarvis_admin
  fi
elif [ "${1}" = "--create-admin" ]; then
  echo "ERROR: 004_phase4.sql has not been applied yet. Run:"
  echo "  podman exec -i mediahost-ai-postgres psql -U mediahostai -d mediahostai < db/init/004_phase4.sql"
  exit 1
fi

# Allow forced password reset
if [ "${1}" = "--reset-admin" ]; then
  create_jarvis_admin
fi

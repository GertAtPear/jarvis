#!/bin/bash
# Run ONCE after DNS is pointed at this server.
# Nginx must be running (for the ACME HTTP challenge on port 80).
source ./nginx/nginx.env

echo "Getting cert for: $AI_DOMAIN"
podman compose run --rm certbot certonly \
  --webroot --webroot-path=/var/www/certbot \
  --email admin@mediahost.co.za \
  --agree-tos --no-eff-email \
  -d "$AI_DOMAIN"

echo "Getting cert for: $VAULT_DOMAIN"
podman compose run --rm certbot certonly \
  --webroot --webroot-path=/var/www/certbot \
  --email admin@mediahost.co.za \
  --agree-tos --no-eff-email \
  -d "$VAULT_DOMAIN"

# Certbot writes to /etc/letsencrypt (= nginx_certs volume).
# Symlink into the layout Nginx expects:
mkdir -p nginx/certs/ai nginx/certs/vault

podman compose run --rm certbot sh -c "
  ln -sf /etc/letsencrypt/live/$AI_DOMAIN/fullchain.pem /etc/letsencrypt/ai/fullchain.pem
  ln -sf /etc/letsencrypt/live/$AI_DOMAIN/privkey.pem   /etc/letsencrypt/ai/privkey.pem
  ln -sf /etc/letsencrypt/live/$VAULT_DOMAIN/fullchain.pem /etc/letsencrypt/vault/fullchain.pem
  ln -sf /etc/letsencrypt/live/$VAULT_DOMAIN/privkey.pem   /etc/letsencrypt/vault/privkey.pem
"

podman compose restart nginx
echo "Certs installed. Nginx reloaded."

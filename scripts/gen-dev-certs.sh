#!/bin/bash
mkdir -p nginx/certs/ai nginx/certs/vault

openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout nginx/certs/ai/privkey.pem \
  -out nginx/certs/ai/fullchain.pem \
  -subj "/C=ZA/ST=Gauteng/O=Mediahost/CN=ai.mediahost.local"

openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout nginx/certs/vault/privkey.pem \
  -out nginx/certs/vault/fullchain.pem \
  -subj "/C=ZA/ST=Gauteng/O=Mediahost/CN=vault.mediahost.local"

echo "Dev certs generated for both domains."
echo "Add to /etc/hosts:  127.0.0.1  ai.mediahost.local  vault.mediahost.local"

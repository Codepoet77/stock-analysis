#!/bin/bash
# Run this once on the droplet to set up SSL
# Usage: bash init-ssl.sh your@email.com

set -e

EMAIL=${1:?Usage: bash init-ssl.sh your@email.com}
DOMAIN="signal-scout-app.com"

cd /opt/stock-analysis

echo "==> Step 1: Ensure containers are running (HTTP mode)..."
docker compose up -d web

echo "==> Step 2: Request certificate from Let's Encrypt..."
docker compose run --rm certbot certonly \
  --webroot \
  --webroot-path /var/www/certbot \
  --email "$EMAIL" \
  --agree-tos \
  --no-eff-email \
  -d "$DOMAIN"

echo "==> Step 3: Switch nginx to SSL config..."
docker compose cp src/stock-analysis-ui/nginx-ssl.conf web:/etc/nginx/conf.d/default.conf 2>/dev/null || \
  docker cp /opt/stock-analysis/src/stock-analysis-ui/nginx-ssl.conf stock-analysis-web-1:/etc/nginx/conf.d/default.conf

echo "==> Step 4: Reload nginx..."
docker compose exec web nginx -s reload

echo "==> Done! https://$DOMAIN should now work."
echo ""
echo "To auto-renew, add this cron job:"
echo "  0 3 * * * cd /opt/stock-analysis && docker compose run --rm certbot renew && docker compose exec web nginx -s reload"

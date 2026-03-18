#!/bin/sh
# If SSL certs exist, use the SSL nginx config
if [ -f /etc/nginx/ssl/live/signal-scout-app.com/fullchain.pem ]; then
  cp /etc/nginx/nginx-ssl.conf /etc/nginx/conf.d/default.conf
fi
exec nginx -g 'daemon off;'

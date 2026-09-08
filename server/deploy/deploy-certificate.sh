#!/bin/sh
set -eu

source_dir=/etc/letsencrypt/live/wardogs.kaermorh.cloud
target_dir=/opt/1panel/www/sites/wardogs.kaermorh.cloud/ssl

install -d -m 0755 "$target_dir"
install -m 0644 "$source_dir/fullchain.pem" "$target_dir/fullchain.pem"
install -m 0600 "$source_dir/privkey.pem" "$target_dir/privkey.pem"
docker exec 1Panel-openresty-X6Z2 openresty -t
docker exec 1Panel-openresty-X6Z2 openresty -s reload

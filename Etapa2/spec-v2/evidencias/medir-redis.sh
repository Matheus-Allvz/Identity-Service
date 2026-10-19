#!/usr/bin/env bash
# Mede EXT-02 (Redis 7.4.6). Uso: bash medir-redis.sh -> escreve medir-redis.out.txt
set -u
cd "$(dirname "$0")"
docker run -d --rm --name idsvc-redis redis:7.4.6 >/dev/null; sleep 3
r(){ echo "> redis-cli $*"; docker exec idsvc-redis redis-cli "$@" 2>&1; }
{ r INFO server | grep redis_version
  echo "# R0 sem autenticacao: PING sem palavra-passe"; r PING
  echo "# R1 SET substitui TTL (espera 50)"; r SET denylist:tenant:a 1 EX 900; r SET denylist:tenant:a 1 EX 50; r TTL denylist:tenant:a
  echo "# R2 MGET existente e inexistente"; r MGET denylist:tenant:a denylist:tenant:b
  echo "# R3 EX 0 (espera erro)"; r SET k v EX 0
  echo "# R4 DEL inexistente (espera 0)"; r DEL denylist:tenant:zz; } > medir-redis.out.txt
docker stop idsvc-redis >/dev/null

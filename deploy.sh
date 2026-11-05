#!/usr/bin/env bash
set -euo pipefail

cd /home/deploy/identity-service

echo "=================================================="
echo "🚀 INICIANDO DEPLOY ZERO DOWNTIME (BLUE/GREEN)"
echo "=================================================="

# 1. Garantir existência da rede compartilhada
echo "==> [1/6] Verificando rede docker compartilhada (identity-net)..."
docker network create identity-net 2>/dev/null || true

# 2. Garantir infraestrutura persistente ativa
echo "==> [2/6] Garantindo infraestrutura persistente (PostgreSQL, Redis, Kafka)..."
if [ -f docker-compose.infra.yml ]; then
    docker compose -f docker-compose.infra.yml up -d
fi

# 3. Identificar slot ativo e definir alvo
echo "==> [3/6] Identificando slot ativo..."
BLUE_HEALTH=$(curl -sf -o /dev/null -w "%{http_code}" http://127.0.0.1:8081/health/live 2>/dev/null || echo "000")
GREEN_HEALTH=$(curl -sf -o /dev/null -w "%{http_code}" http://127.0.0.1:8082/health/live 2>/dev/null || echo "000")

echo "Status Blue (8081): $BLUE_HEALTH | Status Green (8082): $GREEN_HEALTH"

if [ "$BLUE_HEALTH" == "200" ]; then
    ACTIVE_SLOT="blue"
    TARGET_SLOT="green"
    TARGET_PORT="8082"
    OLD_CONTAINER="identity-service-blue"
elif [ "$GREEN_HEALTH" == "200" ]; then
    ACTIVE_SLOT="green"
    TARGET_SLOT="blue"
    TARGET_PORT="8081"
    OLD_CONTAINER="identity-service-green"
else
    echo "Nenhum slot ativo detectado. Inicializando no slot Blue..."
    ACTIVE_SLOT="none"
    TARGET_SLOT="blue"
    TARGET_PORT="8081"
    OLD_CONTAINER=""
fi

TARGET_SERVICE="identity-service-$TARGET_SLOT"
echo "==> Slot Ativo: $ACTIVE_SLOT | Alvo do Deploy: $TARGET_SERVICE (porta $TARGET_PORT)"

# 4. Construir e subir nova versão no slot alvo (ocioso)
echo "==> [4/6] Construindo e iniciando $TARGET_SERVICE..."
docker compose build "$TARGET_SERVICE"
docker compose up -d --no-deps "$TARGET_SERVICE"

# Aguardar nova versão ficar saudável
echo "Aguardando $TARGET_SERVICE ficar saudável na porta $TARGET_PORT..."
READY=false
for i in {1..60}; do
    HTTP_CODE=$(curl -sf -o /dev/null -w "%{http_code}" "http://127.0.0.1:$TARGET_PORT/health/live" 2>/dev/null || echo "000")
    if [ "$HTTP_CODE" == "200" ]; then
        echo "✅ $TARGET_SERVICE está saudável (HTTP 200) na tentativa $i/60!"
        READY=true
        break
    fi
    echo "Aguardando $TARGET_SERVICE inicializar ($i/60)... HTTP: $HTTP_CODE"
    sleep 1
done

if [ "$READY" != "true" ]; then
    echo "❌ Falha no healthcheck do $TARGET_SERVICE. Abortando deploy e mantendo slot antigo!"
    docker stop "$TARGET_SERVICE" || true
    exit 1
fi

# 5. Pausa de 3 segundos para o Caddy começar a rotear tráfego
echo "==> [5/6] Aguardando 3 segundos para active health check do Caddy comutar tráfego..."
sleep 3

# 6. Desligar container antigo e containers legados
echo "==> [6/6] Desligando container antigo..."
if [ -n "$OLD_CONTAINER" ]; then
    echo "Parando $OLD_CONTAINER..."
    docker stop "$OLD_CONTAINER" || true
    docker rm "$OLD_CONTAINER" || true
    echo "Container $OLD_CONTAINER desligado com sucesso!"
fi

# Limpar container legado da porta 8080 se ainda existir
if docker ps -a --format '{{.Names}}' | grep -q "^identity-service-identity-service-1$"; then
    echo "Removendo container legado identity-service-identity-service-1..."
    docker stop identity-service-identity-service-1 || true
    docker rm identity-service-identity-service-1 || true
fi

# Seed e migrações se aplicável
if [ -f seed-demo.sql ]; then
    echo "Aplicando seed-demo.sql na infraestrutura persistente..."
    docker exec identity-postgres psql -U identity -d identity < seed-demo.sql >/dev/null 2>&1 || true
fi

echo "=================================================="
echo "🎉 DEPLOY BLUE/GREEN CONCLUÍDO COM SUCESSO!"
echo "Slot em produção: $TARGET_SERVICE (porta $TARGET_PORT)"
echo "Saúde Pública: $(curl -s -o /dev/null -w "HTTP %{http_code}" https://identity.matheus-alves.dev/.well-known/jwks.json)"
echo "=================================================="

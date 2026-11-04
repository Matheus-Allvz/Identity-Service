#!/usr/bin/env bash
set -euo pipefail

cd /home/deploy/identity-service

echo "==> [1/4] Parando containers antigos (se existirem)..."
docker compose down --remove-orphans || true

echo "==> [2/4] Construindo e iniciando serviços..."
docker compose up -d --build

echo "==> [3/4] Aguardando containers ficarem saudáveis..."
for i in {1..60}; do
    STATUS=$(docker compose ps --format json | jq -r 'select(.Service=="identity-service") | .Health' 2>/dev/null || true)
    if [ "$STATUS" == "healthy" ]; then
        echo "identity-service está saudável!"
        break
    fi
    echo "Aguardando inicialização ($i/60)... status atual: ${STATUS:-desconhecido}"
    sleep 2
done

echo "==> [4/4] Executando Seed de Demonstração no PostgreSQL..."
if [ -f seed-demo.sql ]; then
    docker compose exec -T postgres psql -U identity -d identity < seed-demo.sql
    echo "Seed executado com sucesso!"
else
    echo "Aviso: seed-demo.sql não encontrado."
fi

echo "==> Verificação de saúde local:"
curl -s -X GET http://127.0.0.1:8080/.well-known/jwks.json -o /dev/null -w "HTTP Status: %{http_code}\n"

echo "==> Deploy concluído com sucesso!"

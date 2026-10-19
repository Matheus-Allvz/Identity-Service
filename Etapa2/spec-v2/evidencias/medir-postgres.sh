#!/usr/bin/env bash
# Mede EXT-01 (PostgreSQL 17.6) e a matriz de isolamento de 30 §5.
# Uso: bash medir-postgres.sh -> escreve medir-postgres.out.txt e matriz-isolamento.out.txt
set -u
cd "$(dirname "$0")"
ESPERADO_M_OK=22
ESPERADO_OK=33

cleanup() {
  docker stop idsvc-pg >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run -d --rm --name idsvc-pg -e POSTGRES_PASSWORD=pw postgres:17.6 >/dev/null
for i in $(seq 1 30); do docker exec idsvc-pg pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1; done
sleep 2
{ echo "### pg-rls.sql"; docker exec -i idsvc-pg psql -U postgres -q < pg-rls.sql 2>&1 | sed "s/^.*NOTICE:  //";
  echo "### pg-ddl.sql"; docker exec -i idsvc-pg psql -U postgres -q < pg-ddl.sql 2>&1 | sed "s/^.*NOTICE:  //"; } > medir-postgres.out.txt

M_OK_COUNT=$(grep -c '^OK | M' medir-postgres.out.txt || true)
M_FALHA_COUNT=$(grep -c '^FALHA | M' medir-postgres.out.txt || true)

echo "medicoes M: ${M_OK_COUNT} OK, ${M_FALHA_COUNT} FALHA"

if [ "${M_FALHA_COUNT}" -gt 0 ] || [ "${M_OK_COUNT}" -ne "${ESPERADO_M_OK}" ]; then
  echo "ERRO: medicoes M com falha ou contagem inesperada (esperado ${ESPERADO_M_OK} OK e 0 FALHA; obtido ${M_OK_COUNT} OK e ${M_FALHA_COUNT} FALHA)" >&2
  exit 1
fi

# Matriz: base nova, pg-ddl.sql seguido de matriz-isolamento.sql na mesma sessão
docker exec idsvc-pg psql -U postgres -q -c "CREATE DATABASE matriz" >/dev/null
cat pg-ddl.sql matriz-isolamento.sql | docker exec -i idsvc-pg psql -U postgres -q -d matriz 2>&1 \
  | grep -E "(OK|FALHA) \||ERROR" | sed "s/^.*NOTICE:  //" > matriz-isolamento.out.txt

OK_COUNT=$(grep -c '^OK | I' matriz-isolamento.out.txt || true)
FALHA_COUNT=$(grep -c '^FALHA | I' matriz-isolamento.out.txt || true)

echo "matriz: ${OK_COUNT} OK, ${FALHA_COUNT} FALHA"

if [ "${FALHA_COUNT}" -gt 0 ] || [ "${OK_COUNT}" -ne "${ESPERADO_OK}" ]; then
  echo "ERRO: matriz com falha ou contagem inesperada (esperado ${ESPERADO_OK} OK e 0 FALHA; obtido ${OK_COUNT} OK e ${FALHA_COUNT} FALHA)" >&2
  exit 1
fi

exit 0

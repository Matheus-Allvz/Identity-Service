#!/usr/bin/env bash
# Medição do ambiente env70 (A-66 e A-61)
# Uso: bash medir-env70.sh -> gera medir-env70.trx e medir-env70.out.txt
set -euo pipefail
cd "$(dirname "$0")"

# Localizar dotnet se não estiver diretamente no PATH
if ! command -v dotnet >/dev/null 2>&1; then
    if command -v dotnet.exe >/dev/null 2>&1; then
        dotnet() { dotnet.exe "$@"; }
    elif [ -f "/c/Program Files/dotnet/dotnet.exe" ]; then
        export PATH="$PATH:/c/Program Files/dotnet"
    elif [ -f "/mnt/c/Program Files/dotnet/dotnet.exe" ]; then
        export PATH="$PATH:/mnt/c/Program Files/dotnet"
        dotnet() { "/mnt/c/Program Files/dotnet/dotnet.exe" "$@"; }
    fi
fi

OUT="$PWD/medir-env70.out.txt"
TRX="$PWD/medir-env70.trx"
ENV70_DIR="$PWD/env70"
TEST_PROJ="$ENV70_DIR/tests/IdentityService.Tests/IdentityService.Tests.csproj"
MED_FILE="$PWD/medicoes-excecoes.out"

rm -f "$TRX" "$OUT" "$MED_FILE"
export MEDICOES_OUT_FILE="$MED_FILE"

echo "=== Executando medições de env70 (A-66 e A-61) ==="
echo "=== Medição env70: testes automatizados e aferição de contratos ===" > "$OUT"
echo "Data: $(date -u +"%Y-%m-%dT%H:%M:%SZ")" >> "$OUT"
echo "SDK: $(dotnet --version)" >> "$OUT"
echo "" >> "$OUT"

# Executa os testes e captura a saída
set +e
dotnet test "$TEST_PROJ" \
    --logger "trx;LogFileName=medir-env70.trx" \
    --results-directory "$PWD" \
    --logger "console;verbosity=normal" 2>&1 | tee -a "$OUT"
TEST_EXIT=${PIPESTATUS[0]}
set -e

# Limpar pastas bin e obj para garantir que não ficam artefactos versionados
find "$ENV70_DIR" -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true

# Verificar se o TRX foi gerado
if [ ! -f "$TRX" ]; then
    echo "ERRO: Ficheiro $TRX não foi gerado!" >> "$OUT"
    exit 1
fi

TOTAL=$(grep -o 'total="[0-9]*"' "$TRX" | head -1 | grep -o '[0-9]*' || echo 0)
PASSED=$(grep -o 'passed="[0-9]*"' "$TRX" | head -1 | grep -o '[0-9]*' || echo 0)
FAILED=$(grep -o 'failed="[0-9]*"' "$TRX" | head -1 | grep -o '[0-9]*' || echo 0)

echo "" >> "$OUT"
echo "=== RESUMO DA EXECUÇÃO ===" >> "$OUT"
echo "Total de testes : $TOTAL" >> "$OUT"
echo "Aprovados       : $PASSED" >> "$OUT"
echo "Com falha       : $FAILED" >> "$OUT"
echo "Código de saída : $TEST_EXIT" >> "$OUT"
echo "" >> "$OUT"

# Gerar a tabela dinamicamente a partir dos resultados reais dos testes
echo "=== TABELA DE EXCEÇÕES MEDIDAS (A-61 / A-66) ===" >> "$OUT"
echo "Para uso do autor dos contratos (10-nucleo.md §7 / 40-interfaces.md §1 / 70-ambiente-teste.md §1 C6):" >> "$OUT"
echo "" >> "$OUT"
echo "| Situação | Tipo Exterior | Cadeia de Exceções (InnerException) | SQLSTATE | Comportamento Requerido |" >> "$OUT"
echo "|---|---|---|---|---|" >> "$OUT"

ITENS=(
  "RLS:Violação de RLS (EF Core SaveChangesAsync)"
  "UNICIDADE:Violação de Unicidade (EF Core SaveChangesAsync)"
  "PG_PARADO_DEFAULT:PostgreSQL Parado (EF Core: estratégia por omissão)"
  "PG_PARADO_DESLIGADA:PostgreSQL Parado (EF Core: estratégia desligada)"
  "PG_PARADO_RETRY:PostgreSQL Parado (EF Core: EnableRetryOnFailure)"
  "REDIS_TIMEOUT:Redis: Timeout de Operação"
  "REDIS_CONEXAO:Redis: Conexão Indisponível / Parado"
  "JWT_RESOLVER_EXCEPTION:Chave Indisponível no IssuerSigningKeyResolver"
)

for item in "${ITENS[@]}"; do
    IFS=":" read -r ID SIT <<< "$item"
    if [ -f "$MED_FILE" ] && grep -q "^${ID}|" "$MED_FILE"; then
        LINHA=$(grep "^${ID}|" "$MED_FILE" | head -1)
        # Formato: ID|Situação|Tipo Exterior|Cadeia|SQLSTATE|Comportamento
        IFS="|" read -r _R_ID R_SIT R_TIPO R_CADEIA R_SQL R_COMP <<< "$LINHA"
        echo "| $R_SIT | $R_TIPO | $R_CADEIA | $R_SQL | $R_COMP |" >> "$OUT"
    else
        echo "| $SIT | não medido (teste falhou) | não medido | não medido | não medido |" >> "$OUT"
    fi
done

rm -f "$MED_FILE"

if [ "$TEST_EXIT" -ne 0 ] || [ "$FAILED" -ne 0 ]; then
    echo "" >> "$OUT"
    echo "FALHA: Testes falharam ou código de saída ≠ 0 ($TEST_EXIT)" >> "$OUT"
    echo "FALHA: Testes falharam ou código de saída ≠ 0 ($TEST_EXIT)"
    exit 1
fi

echo "" >> "$OUT"
echo "SUCESSO: Todos os testes passaram ($PASSED/$TOTAL)." >> "$OUT"
echo "SUCESSO: Todos os testes passaram ($PASSED/$TOTAL)."
exit 0

#!/usr/bin/env bash
# Verificacao de pins (atividade 4.7). Uso: bash medir-pins.sh -> medir-pins.out.txt (corre sobre uma copia temporaria de ./pins)
set -u
cd "$(dirname "$0")"
OUT="$PWD/medir-pins.out.txt"; T=$(mktemp -d); cp -r pins/. "$T"; cd "$T"
{ echo "# dotnet --version"; dotnet --version
  echo "# tool restore"; dotnet tool restore 2>&1 | tail -1
  echo "# restore"; dotnet restore 2>&1 | grep -E "NU1|error|erro|restaurado|Restored" 
  echo "# build"; dotnet build -nologo 2>&1 | grep -E "Aviso\(s\)|Erro\(s\)|Warning\(s\)|Error\(s\)"
  echo "# test"; dotnet test --no-build 2>&1 | tail -1
  echo "# versoes resolvidas"; dotnet list package --include-transitive 2>/dev/null | grep -iE "EntityFrameworkCore |EntityFrameworkCore.Relational |IdentityModel" | sort -u
  echo "# vulneraveis"; dotnet list package --vulnerable 2>&1 | grep -i vulner
  echo "# docker build"; docker build -q -t idsvc-pins-check . 2>&1 | tail -1 && docker rmi idsvc-pins-check >/dev/null && echo DOCKER_OK; } > "$OUT" 2>&1
cd / && rm -rf "$T"

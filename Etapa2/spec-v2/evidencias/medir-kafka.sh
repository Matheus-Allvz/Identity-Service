#!/usr/bin/env bash
# Mede EXT-03 (Apache Kafka 3.9.1 KRaft, configuracao por omissao). Uso: bash medir-kafka.sh -> medir-kafka.out.txt
set -u
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")"
docker run -d --rm --name idsvc-kafka apache/kafka:3.9.1 >/dev/null; sleep 20
B=/opt/kafka/bin
{ echo "# K1 configuracao do broker"
  docker exec idsvc-kafka $B/kafka-configs.sh --bootstrap-server localhost:9092 --entity-type brokers --entity-name 1 --describe --all 2>/dev/null | grep -E " auto.create.topics.enable| num.partitions"
  echo "# K2 primeiro envio para topico inexistente (tempo em ms)"
  s=$(date +%s%3N); echo '{"x":1}' | docker exec -i idsvc-kafka $B/kafka-console-producer.sh --bootstrap-server localhost:9092 --topic empresa_cadastrada 2>&1 | grep -E "WARN|ERROR"; e=$(date +%s%3N); echo "duracao_ms=$((e-s))"
  docker exec idsvc-kafka $B/kafka-topics.sh --bootstrap-server localhost:9092 --describe --topic empresa_cadastrada 2>&1 | head -1
  echo "# K3 segundo envio para o mesmo topico (tempo em ms)"
  s=$(date +%s%3N); echo '{"x":2}' | docker exec -i idsvc-kafka $B/kafka-console-producer.sh --bootstrap-server localhost:9092 --topic empresa_cadastrada 2>&1 | grep -E "WARN|ERROR"; e=$(date +%s%3N); echo "duracao_ms=$((e-s))"; } > medir-kafka.out.txt
docker stop idsvc-kafka >/dev/null

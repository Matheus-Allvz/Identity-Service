# Observabilidade do Identity-Service (OpenTelemetry)

> **Estado:** Ativo (NEW-07) · **Âmbito:** métricas e traces por OTLP. Os logs continuam em stdout, em JSON, com `correlation_id`.

O serviço instrumenta-se com as APIs nativas do .NET (`System.Diagnostics.Metrics` e `Activity`) e exporta por **OTLP/gRPC** para um OpenTelemetry Collector. O Collector decide o destino (um backend compatível com Prometheus para métricas, um backend de traces como o Tempo ou o Jaeger). O serviço não conhece os backends.

## 1. Ativação

A exportação é **opt-in por variável de ambiente**. Sem `OTEL_EXPORTER_OTLP_ENDPOINT` o SDK nem é registado: não há threads de exportação, tráfego nem custo. É assim que correm os testes e o desenvolvimento local.

| Variável | Exemplo | Efeito |
|---|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://otel-collector:4317` | Liga a exportação de métricas e traces |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (omissão) ou `http/protobuf` | Protocolo OTLP |
| `OTEL_TRACES_SAMPLER` | `parentbased_traceidratio` | Estratégia de amostragem de traces |
| `OTEL_TRACES_SAMPLER_ARG` | `0.1` | Fração de traces amostrados (10 %) |
| `OTEL_METRIC_EXPORT_INTERVAL` | `60000` (omissão) | Intervalo de envio das métricas, em ms |
| `OTEL_RESOURCE_ATTRIBUTES` | `deployment.environment=homolog` | Atributos adicionais do recurso |

O recurso é identificado por `service.name = identity-service` e `service.version` (versão do assembly).

No `docker-compose.yml` do repositório as variáveis já estão definidas e os contentores da aplicação ligam-se à rede externa `observability-net`, onde se espera um Collector com o nome `otel-collector`. Se o Collector estiver em baixo, o serviço continua a responder normalmente: a falha de exportação é silenciosa.

## 2. Métricas

Meter: `IdentityService`.

| Instrumento | Tipo | Etiquetas | O que mede |
|---|---|---|---|
| `identity.tokens.issued` | contador | `kind` (`user`, `impersonation`, `service`), `view_mode` | Access tokens emitidos (login, refresh, troca de contexto, impersonation, client credentials) |
| `identity.problems` | contador | `code` (`ERR-*`), `status` | Respostas de erro RFC 9457 |
| `identity.denylist.hits` | contador | `kind` (`jti`, `user`, `tenant`) | Pedidos autenticados recusados pela denylist Redis |
| `identity.jwks.requests` | contador | — | Pedidos a `/.well-known/jwks.json` |
| `identity.outbox.published` | contador | `topic` | Eventos confirmados pelo Kafka |
| `identity.outbox.failures` | contador | `topic`, `final` | Falhas de publicação; `final=true` quando a mensagem esgotou as 5 tentativas |
| `http.server.request.duration` | histograma | rota, método, status | Latência HTTP (instrumentação do ASP.NET Core) |
| `dotnet.*` | várias | — | GC, heap, thread pool, exceções (instrumentação de runtime) |

Nenhuma etiqueta transporta identificadores de utilizador, tenant, token ou `jti`: a cardinalidade é limitada e não há dados pessoais na telemetria.

### Consultas úteis (PromQL)

Num backend Prometheus os nomes ganham `_` no lugar de `.` e os contadores o sufixo `_total`.

```promql
# Taxa de emissão de tokens por tipo
sum by (kind) (rate(identity_tokens_issued_total[5m]))

# Taxa de 401 e 403 por código de erro
sum by (code) (rate(identity_problems_total{status=~"401|403"}[5m]))

# Proporção de erros de servidor (5xx)
sum(rate(identity_problems_total{status=~"5.."}[5m])) / sum(rate(http_server_request_duration_seconds_count[5m]))

# p95 de latência por rota
histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket[5m])))

# Sessões cortadas pela denylist
sum by (kind) (rate(identity_denylist_hits_total[5m]))

# Eventos que esgotaram as tentativas (deve ser sempre 0)
sum by (topic) (increase(identity_outbox_failures_total{final="true"}[1h]))

# Consumidores sem cache de JWKS: o valor esperado é de poucos pedidos por hora por consumidor
sum(rate(identity_jwks_requests_total[5m]))
```

### Alertas sugeridos

| Condição | Significado provável |
|---|---|
| `identity_outbox_failures_total{final="true"}` a crescer | Eventos perdidos para o Kafka: broker indisponível ou tópico em falta |
| `identity_problems_total{code="ERR-SYS-002"}` a crescer | PostgreSQL, Redis ou chave de assinatura indisponíveis |
| Pico de `identity_problems_total{code="ERR-AUTH-001"}` | Tentativas de força bruta ou cliente com credenciais erradas |
| Pico de `identity_problems_total{code="ERR-AUTH-006"}` | Reuso de refresh token: sessões comprometidas |
| `identity_jwks_requests_total` muito acima do normal | Algum consumidor a buscar o JWKS a cada pedido |

## 3. Traces

- Um span de servidor por pedido HTTP, com o atributo `correlation.id` igual ao cabeçalho `X-Correlation-Id` da resposta. Serve para saltar de um erro reportado por um cliente para o trace correspondente.
- Spans de cliente para as consultas PostgreSQL (Npgsql).
- `/health/live` e `/health/ready` não geram traces.
- O contexto W3C (`traceparent`) recebido é respeitado: se o chamador já amostrou o trace, o Identity continua-o.

## 4. Limites conhecidos

- Não há spans para Redis nem para a publicação no Kafka; as métricas de denylist e de outbox cobrem esses caminhos.
- Os logs não são exportados por OTLP. Correlacione-os com os traces pelo `correlation_id`.
- A fila pendente da outbox não é exposta como gauge; acompanhe `identity.outbox.published` e `identity.outbox.failures`.

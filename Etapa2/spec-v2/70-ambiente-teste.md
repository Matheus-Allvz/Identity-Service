# 70 · Contrato do ambiente de teste — Identity-Service

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** AUD (UT-2-03; reconciliado em UT-2-90) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Medições:** cada `[MEDIDO]` deste ficheiro cita um teste do esqueleto `evidencias/env70/` (`Medicoes.cs`, `Cunhagem.cs`, `Kestrel.cs`), executado por `evidencias/medir-env70.sh` com resultado em `evidencias/medir-env70.out.txt` (UT-2-93; Docker 29.7.2).
> **Parte comum = cabeçalho + §1: entra em todos os pacotes.** Tudo o que obriga o código está na §1; as secções extra só entram nos pacotes da tabela abaixo.

| Secção | Entra em | Bytes |
|---|---|---|
| §2 Arranque e G1 | ORQ, UT-5-01, validadores, M-11 (UT-3-11 e a sua fase) | 4 160 |
| §3 Dados, tokens, relógio, inspeção | UT-5-01, validadores | 3 200 |
| §4 Injeção de falhas | UT-5-01, validadores | 1 900 |
| §5 Convenções da suite | UT-5-01, validadores, ORQ | 900 |
| §6 Lacunas, PRS, pedidos | ORQ (`90`) | 3 100 |

## 1. Contrato para o código (comum)
A suite de aceitação corre **em processo**: `WebApplicationFactory<Program>` com Kestrel (`UseKestrel(0)`), ambiente `Testing`, PostgreSQL, Redis e Kafka reais em Testcontainers (imagens de `10` §4). **Sem fakes:** nenhum sistema externo é chamado pelo serviço; EXT-04 e EXT-05 só consomem. O código **DEVE**:

| # | Obrigação | Uso na suite |
|---|---|---|
| C1 | Ter `public partial class Program;` e ler a configuração só por `IConfiguration` | A suite define as variáveis no processo antes de criar a fábrica |
| C2 | Em `Testing`, usar `JWT_RSA_PRIVATE_KEY_PEM` quando preenchida | Chave RSA 2048 gerada por sessão; a suite cunha tokens com ela |
| C3 | Registar `TimeProvider` como singleton e obtê-lo do DI em todo o código, incluindo a validação de `exp` (`TokenValidationParameters.TimeProvider`), os TTL do Redis e o worker da outbox | A suite substitui-o (`ConfigureServices`) por um relógio que só avança quando ela manda |
| C4 | Migrar uma BD vazia no arranque (M-11); nenhum dado além do seed de `role`; o tráfego de pedidos corre como `identity_app` | Dados de partida por SQL antes de cada cenário; tabelas truncadas entre cenários |
| C5 | Obter `ISigningKey` do DI em cada uso, incluindo a validação de tokens, sem copiar a chave para estado estático | A suite troca `ISigningKey` por uma que lança `CryptographicException` |
| C6 | Responder 503 `ERR-SYS-002` (nunca 500) em ≤ 15 s às falhas de dependência, classificadas pelo tratador global do M-00 pela cadeia de `InnerException` (`10` §7); sem `EnableRetryOnFailure` | A suite para o contentor ou substitui a chave (PRS-16) |
| C7 | Manter o worker da outbox vivo com o Kafka indisponível e retomar quando volta | A suite pausa o contentor Kafka |
| C8 | Expor as interfaces internas com as assinaturas e os tipos exatos de `40` §4.3 | Testes de componente resolvem-nas do DI da fábrica |

**Testes do construtor** (`tests/IdentityService.Tests`): um por CA da fase, `CA_<nn>_<m>_<Descricao>`; bordas `CA_<nn>_<m>_Borda_<Descricao>`. Builders do Testcontainers com a imagem no construtor (`new PostgreSqlBuilder("postgres:17.6")`): o construtor sem parâmetros é obsoleto e falha o build (`CS0618` com `TreatWarningsAsErrors`) [MEDIDO] build de `evidencias/env70`. Esperas só ativas com teto; nunca um atraso fixo como sincronização.

**Dados pessoais proibidos.** E-mail só `@example.test`; CNPJ com raiz `99` e dígitos verificadores válidos (`99000001000101`); nomes `Utilizador Teste NN`; IP de 192.0.2.0/24, 198.51.100.0/24 ou 203.0.113.0/24. Varredura obrigatória antes de cada entrega de código (comando no brief da fase).

## 2. Arranque e G1
**`docker-compose.yml`** (texto fixo e normativo, referido por `10` §3.11; escrito na raiz pelo construtor da fase do M-11). Validado com o esqueleto de pins: todos `healthy` em 29 s [MEDIDO] `evidencias/env70/docker-compose.yml`, 2026-10-19.
```yaml
name: identity-service
services:
  postgres:
    image: postgres:17.6
    environment: { POSTGRES_DB: identity, POSTGRES_USER: identity, POSTGRES_PASSWORD: identity-test }
    healthcheck: { test: ["CMD-SHELL", "pg_isready -U identity -d identity"], interval: 2s, timeout: 2s, retries: 30 }
  redis:
    image: redis:7.4.6
    healthcheck: { test: ["CMD", "redis-cli", "ping"], interval: 2s, timeout: 2s, retries: 30 }
  kafka:
    image: apache/kafka:3.9.1
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: broker,controller
      KAFKA_LISTENERS: PLAINTEXT://:9092,CONTROLLER://:9093
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:9092
      KAFKA_CONTROLLER_LISTENER_NAMES: CONTROLLER
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT
      KAFKA_CONTROLLER_QUORUM_VOTERS: 1@kafka:9093
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR: 1
      KAFKA_TRANSACTION_STATE_LOG_MIN_ISR: 1
    healthcheck: { test: ["CMD-SHELL", "/opt/kafka/bin/kafka-broker-api-versions.sh --bootstrap-server localhost:9092 > /dev/null"], interval: 5s, timeout: 10s, retries: 24 }
  identity-service:
    build: .
    environment:
      ASPNETCORE_ENVIRONMENT: Testing
      DB_CONNECTION_STRING: Host=postgres;Port=5432;Database=identity;Username=identity;Password=identity-test
      REDIS_CONNECTION_STRING: redis:6379
      KAFKA_BOOTSTRAP_SERVERS: kafka:9092
    ports: ["8080:8080"]
    depends_on:
      postgres: { condition: service_healthy }
      redis: { condition: service_healthy }
      kafka: { condition: service_healthy }
    healthcheck:
      test: ["CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /.well-known/jwks.json HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n 1 <&3 | grep -q ' 200 '"]
      interval: 2s
      timeout: 3s
      retries: 60
```
Só a porta 8080 é publicada no anfitrião. Pronto quando: `identity-service` `healthy`, isto é, EP-10 = 200 (o health check está fora do âmbito; a imagem `aspnet:10.0.13` tem `bash` e não tem `curl` [MEDIDO] `evidencias/env70/docker-compose.yml`). EP-10 só responde depois de `MigrateAsync()` (`10` §3.11).

**Comando de G1** (raiz do repositório, bash; teto 300 s):
```bash
docker compose down -v --remove-orphans && docker compose up -d --build --wait --wait-timeout 300 && test "$(docker compose exec -T postgres psql -U identity -d identity -tAc 'SELECT count(*) FROM role')" = 4 && docker compose down -v
```
Cobre build da imagem (`Dockerfile` de `10` §3.11), arranque, migração numa BD vazia (volume apagado) e seed (`30` §6).

**Suite.** Projeto `tests/IdentityService.Acceptance/` com os mesmos `PackageReference` de teste de `10` §4 e `ProjectReference` para `src/IdentityService`; no ciclo 1 o ORQ copia-o para o worktree só depois da entrega. Requer Docker; corre com o compose em baixo.
```bash
dotnet test tests/IdentityService.Acceptance/IdentityService.Acceptance.csproj -c Release --filter "Fase=F01" --blame-hang-timeout 5m --logger "trx;LogFileName=F01.trx"
```
Regressão (G5): `--filter "Fase=F00|Fase=F01"`.

**Variáveis definidas pela suite** (no processo, antes de criar a fábrica):
| Variável | Valor de teste |
|---|---|
| `DB_CONNECTION_STRING` | `GetConnectionString()` do PostgreSQL (utilizador `postgres`, superutilizador e dono das tabelas) |
| `REDIS_CONNECTION_STRING` | `GetConnectionString()` do Redis |
| `KAFKA_BOOTSTRAP_SERVERS` | `127.0.0.1:<porta>`: a suite retira o prefixo `plaintext://` e a `/` final que `GetBootstrapAddress()` devolve [MEDIDO] `Medicoes.L03_Kafka` |
| Ambiente | `UseEnvironment("Testing")` (a fábrica usa `Development` por omissão) |
| `JWT_RSA_PRIVATE_KEY_PEM` | `RSA.Create(2048).ExportPkcs8PrivateKeyPem()`, nova por sessão; nunca gravada em ficheiro |
| `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_KEY_ID`, `OUTBOX_POLLING_INTERVAL_MS` | Não definidas: valem os defaults de `10` §3.7 |

## 3. Dados, tokens, relógio e inspeção
**Contentores.** Um conjunto (PostgreSQL, Redis, Kafka) por sessão, numa fixture de coleção; uma fábrica por conjunto. Os testes de falha usam um conjunto próprio (§4).

**Carga dos dados de partida.** SQL direto pela ligação de `DB_CONNECTION_STRING`, sem `SET ROLE`: o superutilizador ignora a RLS [MEDIDO] `20` EXT-01 n.º 1, por isso escreve nas 3 tabelas `ITenantScoped`. Ordem imposta pelas FK: `economic_group` → `tenant` → `tenant_scope` → `user_account` → `tenant_member` → `member_role` → `refresh_token`. Todas as colunas `NÃO` nulas preenchidas (as temporais com o relógio de teste). `password_hash`: Argon2id com os parâmetros de RNF-01, calculado pela suite (Konscious, transitivo pelo `ProjectReference`). `refresh_token`: 32 bytes aleatórios em base64url (PRS-34), `token_hash` = SHA-256 hex; só com `user_id`, `group_id`, `active_tenant_id` e `allowed_tenants` já carregados e com o `tenant_member` `ACTIVE` que o contexto exige; respeita `ck_refresh_token_context` e `ck_refresh_token_status_dates`.

**Limpeza** antes de cada cenário: `TRUNCATE economic_group, tenant, tenant_scope, user_account, tenant_member, member_role, refresh_token, auth_audit_log, outbox_message;` (ficam `role` e `__EFMigrationsHistory`) e `FLUSHDB` no Redis (ligação da suite com `allowAdmin=true`). O Kafka não se limpa: cada leitura filtra por `event_id`. Testes em série (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`).

**Tokens.** Por omissão, pelo fluxo real (EP-01 e depois EP-02). Cunhados só quando o CA exige um token que o fluxo não produz (outra chave, `alg`, `kid` desconhecido, expirado, claim em falta ou de tipo errado, `act`):
```csharp
new JsonWebTokenHandler().CreateToken(payloadJson,
  new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "key-2026-01" }, SecurityAlgorithms.RsaSha256));
```
`payloadJson` é JSON literal com todas as claims de `10` §3.5 e nulos como `null`; o handler gera o header `{"alg":"RS256","kid":"key-2026-01","typ":"JWT"}` e não acrescenta claims [MEDIDO] `Cunhagem.cs`. Um token cunhado só aponta para dados carregados: `sub` em `user_account`; `fid` com `refresh_token` `ACTIVE` da família; cada tenant de `allowed_tenants` existente. "Chave externa" = outra RSA 2048 gerada na sessão.

**Relógio.** Depois de um passo que grava `denylist:user` (bloqueio, reuso, suspensão), a suite avança o relógio pelo menos 1 s antes de emitir tokens novos para o mesmo utilizador: a regra `iat ≤ V` (PRS-61) rejeita tokens do mesmo segundo, e um `refresh_token` criado nesse segundo fica rejeitado enquanto a chave existir. `RelogioTeste : TimeProvider` com `GetUtcNow()` a devolver o instante guardado e `Avancar(TimeSpan)`; reposto em cada cenário para o instante real truncado ao segundo; só avança. Os TTL do Redis correm em tempo real: verificações de TTL aceitam `[esperado − 2, esperado]` s; expirações do Redis não se testam avançando o relógio. Colunas temporais e `iat`/`exp` comparam-se por igualdade com o relógio.

**Inspeção.** BD por SQL com a ligação da suite. Camada 4 (RLS): na ligação da suite, `SET ROLE identity_app` e `set_config` de `app.allowed_tenants`, `app.write_tenants` e `app.auth_flow` como em `10` §3.9 e `30` §5. `denylist:user` nunca é apagada pelo serviço: um teste de reativação verifica que a chave continua até expirar (PRS-48). Redis: `GET`, `TTL`, `EXISTS`. Kafka: consumidor Confluent com `GroupId` novo, `Assign(new TopicPartitionOffset(topico, 0, Offset.Beginning))`, filtrando pelo `event_id` (= `outbox_message.id`). Componentes: `factory.Services.CreateScope()` e `GetRequiredService<IAccessQueries>()` (IF-09).

**Concorrência** (CA de pedidos simultâneos, E1 §3.5). A suite envia N pedidos (N = 2 por omissão) a partir de N tarefas que esperam numa `Barrier` e só depois chamam `HttpClient.SendAsync`, com `Task.WhenAll`; não controla a intercalação dentro do serviço. O oráculo de um CA de concorrência **DEVE** valer para qualquer intercalação: afirma o multiconjunto de respostas (por exemplo, "exatamente uma 200 e uma 401 `ERR-AUTH-006`") e o estado final (por exemplo, "nenhuma linha `ACTIVE` na família" depois de uma rotação concorrente, tratada como reuso), nunca qual dos pedidos ganha. Cada CA de concorrência corre 20 repetições no mesmo teste; falha se uma falhar (PRS-97). "Processo morto a meio" e "resposta perdida do broker" não têm CA (PRS-62).

## 4. Injeção de falhas
Os conjuntos dedicados são descartados no fim do teste: um contentor parado e reiniciado muda de porta no anfitrião.

| Falha | Mecanismo (controlado pela suite, de fora do código) | Resultado exigido | Medido | Usado por |
|---|---|---|---|---|
| Redis indisponível | Conjunto dedicado; fábrica criada; `StopAsync()` do Redis | 503 `ERR-SYS-002` em ≤ 15 s; transação desfeita; com `DEL` posterior ao commit, BD confirmada (`10` §7, PRS-15, PRS-16) | [MEDIDO] `Medicoes.L05_FalhasViaWaf`: `RedisConnectionException` em 5,5 s (2.º pedido 6,0 s). `MedicoesA61.Medicao_C`: `RedisTimeoutException` deriva de `System.TimeoutException`, não de `RedisException`; ambas em `evidencias/medir-env70.out.txt` | M-00 (RG-06 a RG-08); escrita por IF-04 em M-01, M-02, M-03, M-09, M-10, M-12 |
| PostgreSQL indisponível | Idem, `StopAsync()` do PostgreSQL | 503 `ERR-SYS-002` em ≤ 15 s | [MEDIDO] em `evidencias/medir-env70.out.txt`: `Medicoes.L05_FalhasViaWaf`, Npgsql sem EF: `NpgsqlException` (`IOException`) em 18 ms. `MedicoesA61.Medicao_B`, EF Core: estratégia por omissão → `InvalidOperationException` → `NpgsqlException`; com `EnableRetryOnFailure` (proibido, PRS-70) → `RetryLimitExceededException` → `NpgsqlException`; a cadeia classifica ambas. `MedicoesA61.Medicao_A`: 42501 e 23505 → `DbUpdateException` → `PostgresException` | M-00 |
| Kafka indisponível | Conjunto dedicado; endereço lido **antes** de `PauseAsync()` (em pausa, `GetBootstrapAddress()` lança); `UnpauseAsync()` para recuperar | `retry_count` sobe; `FAILED` na 5.ª tentativa, com `retry_count = 5` (`ck_outbox_message_failed_retry`); com o Kafka de volta antes disso, `PUBLISHED` (PRS-43) | [MEDIDO] `Medicoes.L03_Kafka`: produtor com `MessageTimeoutMs = 3000` (o valor fixado, PRS-63) → `Local_MsgTimedOut` em 3,0 s, em pausa e parado; após `UnpauseAsync()`, entrega em 17 ms | M-14 (RF-14) |
| Chave de assinatura indisponível | `ConfigureServices` regista uma `ISigningKey` cujo `KeyId` devolve `JWT_KEY_ID` e cujos restantes membros lançam `CryptographicException` | 503 `ERR-SYS-002` em cada EP que usa a chave: EP-02 e EP-04 a EP-09 na leitura pelo middleware, antes da RG-06 e também sem token (PRS-71); EP-01 e EP-03 só com pedido válido, ao assinar (credenciais ou refresh inválidos dão o 401 normal, sem usar a chave); EP-10 na exportação; sem linha de auditoria nem de outbox | Não se aplica (substituição por DI) | M-07 (RF-11), M-00 |
| Processo morto a meio | Nenhum: nenhum CA o exige. Um CA que o exija exige um `CR-nn` | Não se aplica | Não se aplica | Nenhum |

Teto de espera com Kafka indisponível: `5 × (3000 ms + OUTBOX_POLLING_INTERVAL_MS) + 10 s` = 27,5 s com os valores por omissão.

## 5. Convenções da suite
**Varredura de dados pessoais** (saída 0 = limpo):
```bash
! grep -rnoIE --include=*.cs --include=*.json --include=*.sql "[[:alnum:]._%+-]+@[[:alnum:].-]+\.[[:alpha:]]{2,}|[0-9]{3}\.[0-9]{3}\.[0-9]{3}-[0-9]{2}|\b[0-8][0-9]{13}\b|\b9[0-8][0-9]{12}\b" src tests | grep -vE "@example\.test$"
```

| Item | Convenção |
|---|---|
| Nome do teste de um CA | `CA_<nn>_<m>_<Descricao>` |
| Nome do teste de uma borda | `CA_<nn>_<m>_Borda_<Descricao>` |
| Agrupamento | Uma classe e um ficheiro por fase, `F<nn>AceitacaoTests`, com `[Trait("Fase", "F<nn>")]`; CA `@critico` também com `[Trait("Critico", "sim")]` |
| Testes | Todos `async Task` (o analisador xUnit1031 rejeita `.Result` [MEDIDO] build de `evidencias/env70`) |
| Timeout | `[Fact(Timeout = 60000)]`; testes de falha `Timeout = 180000` (xUnit 2.9.3 aplica-o a testes `async` [MEDIDO] `Medicoes.TimeoutXunit`); sessão `--blame-hang-timeout 5m` |
| Esperas | `EsperarAte(condição, teto)` com sondagem a cada 100 ms. Tetos: resposta HTTP 15 s; linha da outbox e mensagem no Kafka 10 s; RNF-05 3 s medidos desde a resposta do EP; Kafka indisponível pela fórmula da §4 |
| Resultado | Ficheiro `trx` por fase; o ORQ calcula G2, G3 e G5 (`E1` §7.2) |

## 6. Lacunas, PRS e pedidos de alteração
**Lacunas** (`20`, `90` §5; medições de 2026-10-19 com o esqueleto `evidencias/env70/`, pins de `10` §4):
| ID | Estado | Resultado |
|---|---|---|
| L-03 | Fechada | [MEDIDO] `Medicoes.L03_Kafka`: `Testcontainers.Kafka@4.16.0` arranca `apache/kafka:3.9.1` em 4,5 s; produzir e consumir funciona; `GetBootstrapAddress()` = `plaintext://127.0.0.1:<porta>/` |
| L-04 | Fechada | [MEDIDO] `Medicoes.L04_PoolReset`: Npgsql 10.0.3 por omissão: a mesma ligação física (mesmo `pg_backend_pid`) volta ao pool com `current_user` = dono e `app.auth_flow`, `app.allowed_tenants` vazios. Com `No Reset On Close=true` herda `identity_app` e `auth_flow = true`; o interceptor de `30` §5 redefine ambos em cada abertura |
| L-05 | Aberta (parcial) | [MEDIDO] `evidencias/medir-env70.out.txt`: driver e cadeia através do EF Core (`MedicoesA61.Medicao_A` a `Medicao_D`, `Medicoes.L05_FalhasViaWaf`; §4). `Medicao_D`: com `IssuerSigningKeyResolver` a falha da chave não sai de `ValidateTokenAsync` (fica em `TokenValidationResult.Exception`), por isso a chave é lida antes (PRS-71). A resposta do serviço só se verifica com a implementação, nos CA de M-00 |
| L-06 | Fechada | [MEDIDO] `Medicoes.L03_Kafka`: com `MessageTimeoutMs = 3000` (PRS-63) o produtor devolve erro em 3,0 s e recupera após `UnpauseAsync()` |

**PRS deste contrato** (estado em `90` §2.1: aprovados por D-10 e D-16):
| ID | Pressuposto |
|---|---|
| PRS-90 | Suite em processo: `WebApplicationFactory` com Kestrel, `Testing`, Testcontainers; configuração por variáveis de ambiente |
| PRS-91 | Chave de teste gerada por sessão; tokens pelo fluxo real por omissão; cunhagem por payload literal |
| PRS-92 | Relógio de teste substituível, truncado ao segundo, só avança; TTL do Redis com tolerância de 2 s |
| PRS-93 | Dados fictícios (`@example.test`, CNPJ de raiz `99`, IP de RFC 5737) e varredura obrigatória |
| PRS-94 | Falhas por paragem ou pausa de contentor dedicado; 503 em ≤ 15 s; sem `EnableRetryOnFailure` |
| PRS-95 | Timeouts de 60 s, 180 s e 5 min; sondagem a 100 ms com os tetos da §5 |
| PRS-96 | `docker-compose.yml` fixo (§2): health checks, `service_healthy`, Kafka KRaft anunciado em `kafka:9092`, só a porta 8080 publicada |
| PRS-97 | CA de concorrência com `Barrier`, oráculo válido para qualquer intercalação e 20 repetições (§3) |

**Pedidos de alteração:** aplicados em UT-2-90 como CR-06 a CR-11 (`90` §7.2).

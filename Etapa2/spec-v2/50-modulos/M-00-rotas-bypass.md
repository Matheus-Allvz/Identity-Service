# M-00 · Rotas, bypass e tratamento global

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-00) · **Estado:** Rascunho (ciclo 5, UT-3-00) · **Atualizado em:** 2026-10-19
> **Pacote:** este ficheiro + `10` (§1-§2, §3, §3.7, §3.9, §4, §6-§10) + `30` (§1-§3 ENT-09, ENUM-08, §4) + `40` (§1-§3, §1.1, IF-03, IF-04, IF-05, IF-08, tipos) + `70` (§1) + `20` (EXT-02) + `90` (UT-3-00). Total: 57 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Middleware de autenticação, RG-06 a RG-08 e resolução de `IAuthContext` | Lógica de negócio específica de cada endpoint (M-01 a M-14) |
| Tabela normativa de rotas e política de `BypassTenantFilter` (`40` §1.1) | Implementação de filtros e interceptores RLS das camadas 3 e 4 (M-13) |
| Tratador global de exceções, classificação RFC 9457 e `InnerException` | Emissão de tokens de acesso JWT ou refresh tokens (M-01, M-02, M-03) |
| Logging estruturado JSON com scope e correlação `X-Correlation-Id` | Publicação de mensagens e worker de outbox no Kafka (M-14) |
| Implementação de `IDenylist` (Redis), `IAuditLog` e `ISigningKey` | Regras globais de negócio de catálogo e permissões RG-01 a RG-05 (M-08) |

**Escreve:** `auth_audit_log` (IF-03.1), Redis `denylist:*` (IF-04) · **Lê:** Redis `denylist:*`, material de chave `ISigningKey` · **Usa:** Nenhuma interface externa (é fornecedor de IF-03, IF-04, IF-05, IF-08)

## 2. Endpoints e eventos (detalhe)
O M-00 não expõe rotas próprias de negócio. O M-00 intercepta todos os endpoints do serviço (EP-01 a EP-10), executa a validação de transporte, autenticação (camadas 1 e 2), classificação global de exceções e registo de auditoria/denylist.

## 3. Requisitos funcionais

### RF-15 — Autenticação transversal, contexto e bypass
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Pedido HTTP recebido em qualquer endpoint do pipeline Kestrel |
| Pré-condições | `ISigningKey` operacional |
| RN | RN-01, RN-02, RN-03, RN-04, RN-05 |

**Fluxo principal:**
1. O middleware extrai `X-Correlation-Id` (ou gera UUID v7 se ausente/inválido) e determina o IP de origem conforme PRS-52.
2. Inicia scope de log com `correlation_id`, `user_id`, `actor_user_id`, `tenant_id`, `group_id`, `active_scope`.
3. Valida transporte: corpo ≤ 65 536 bytes e Content-Type JSON (camada 1).
4. Obtém `ISigningKey.SigningKey` do DI antes de validar autorização (PRS-71).
5. Se rota pública (EP-01, EP-03, EP-10), ignora `Authorization` e define `IsAuthenticated = false`.
6. Se rota com token obrigatório (EP-02, EP-04 a EP-09), executa em ordem estrita: RG-06 (token ausente/inválido/expirado/`denylist:jti` → 401 `ERR-AUTH-002`), RG-07 (`denylist:user` com valor ≥ `iat` → 403 `ERR-AUTH-004`), RG-08 (`denylist:tenant` em `allowed_tenants` → 403 `ERR-CTX-002`).
7. Hidrata `IAuthContext` e define `BypassTenantFilter = true` se rota for EP-01 a EP-05 ou `platform_role = PLATFORM_ADMIN`; `false` nos restantes.
8. Encaminha execução para o endpoint de destino.

**Fluxos de exceção:** EX-00.1 (corpo > 65 536 bytes → 413 `ERR-REQ-003`); EX-00.2 (Content-Type inválido → 415 `ERR-REQ-004`); EX-00.3 (falha na chave `ISigningKey` → 503 `ERR-SYS-002`); EX-00.4 (violação RG-06 → 401 `ERR-AUTH-002`); EX-00.5 (violação RG-07 → 403 `ERR-AUTH-004`); EX-00.6 (violação RG-08 → 403 `ERR-CTX-002`).

---

### RF-17 — Tratamento global de exceções, RFC 9457 e logging
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Exceção não tratada disparada durante a execução de qualquer pedido |
| Pré-condições | Nenhuma |
| RN | RN-06, RN-07, RN-08, RN-09 |

**Fluxo principal:**
1. Tratador global intercepta a exceção inspecionando toda a cadeia de `InnerException`.
2. Se `ApiErrorException`, mapeia para o código de catálogo correspondente.
3. Se `PostgresException` com SqlState 42501, mapeia para 403 `ERR-CTX-007`.
4. Se `PostgresException` com SqlState 23505 não tratado, mapeia para 409 `ERR-CTX-005`.
5. Se falha de conectividade (SqlState 08*, 57P01-57P03, `NpgsqlException`, `RedisException`, `CryptographicException`), mapeia para 503 `ERR-SYS-002`.
6. Se nenhuma anterior, mapeia para 500 `ERR-SYS-001`.
7. Em 500 e 503, emite registo em log JSON estruturado com o `correlation_id` no scope, omitindo segredos e payloads.
8. Retorna resposta RFC 9457 (`application/problem+json; charset=utf-8`) com 6 campos obrigatórios: `type`, `title`, `status`, `code`, `detail`, `correlation_id`.

**Fluxos de exceção:** EX-17.1 (exceção desconhecida mapeada para 500); EX-17.2 (falha de infraestrutura mapeada para 503).

**Concorrência e idempotência:**
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Múltiplos pedidos concorrentes com falha de dependência | Todos os pedidos respondem 503 `ERR-SYS-002` em ≤ 15 s sem travar o runtime | CA-00.4 |
| Cabeçalho `X-Correlation-Id` repetido ou concorrente | O módulo preserva o ID válido por pedido de forma isolada no contexto scoped | CA-00.3 |
| Queda simultânea de Postgres e Redis | Tratador classifica o primeiro elemento da cadeia de exceções como 503 | CA-00.4 |

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-01 | Validação | **SE** corpo > 65 536 bytes OU (corpo ≥ 1 byte E `Content-Type` não for `application/json` com `charset=utf-8`) **ENTÃO** responder 413 `ERR-REQ-003` ou 415 `ERR-REQ-004` **SENÃO** prosseguir | 65 536 bytes | `ERR-REQ-003`, `ERR-REQ-004` | RF-15 | `40` §2 |
| RN-02 | Validação | **SE** resolver `ISigningKey` lançar `CryptographicException` **ENTÃO** responder 503 `ERR-SYS-002` em todos os endpoints que a usam **SENÃO** prosseguir | – | `ERR-SYS-002` | RF-15 | PRS-71, C6 |
| RN-03 | Autorização | **SE** endpoint exige token E (JWT ausente, assinatura inválida, expirado, `kid` errado OU existir `denylist:jti:{jti}`) **ENTÃO** responder 401 `ERR-AUTH-002` **SENÃO** prosseguir | Tolerância exp 0 | `ERR-AUTH-002` | RF-15 | RG-06, `10` §3.9 |
| RN-04 | Autorização | **SE** endpoint exige token E existir `denylist:user:{sub}` com valor ≥ `iat` **ENTÃO** responder 403 `ERR-AUTH-004` **SENÃO** prosseguir | Comparação ≤ (PRS-61) | `ERR-AUTH-004` | RF-15 | RG-07, `10` §3.9 |
| RN-05 | Autorização | **SE** endpoint exige token E existir `denylist:tenant:{t}` para algum `t` em `allowed_tenants` **ENTÃO** responder 403 `ERR-CTX-002` **SENÃO** prosseguir | Consulta MGET | `ERR-CTX-002` | RF-15 | RG-08, `10` §3.9 |
| RN-06 | Integridade | **SE** pedido for para EP-01 a EP-05 OU chamador tiver `platform_role = PLATFORM_ADMIN` **ENTÃO** `BypassTenantFilter` DEVE ser `true` **SENÃO** DEVE ser `false` | Relação de rotas `40` §1.1 | – | RF-15 | `40` §1.1 |
| RN-07 | Integridade | **SE** exceção não tratada ocorrer **ENTÃO** o tratador global DEVE inspecionar a cadeia de `InnerException` na ordem exata de `40` §1.2 **SENÃO** responder 500 `ERR-SYS-001` | Ordem 1 a 5 | – | RF-17 | PRS-65, `40` §1.2 |
| RN-08 | Formato | **SE** erro for gerado **ENTÃO** resposta DEVE ter `Content-Type: application/problem+json; charset=utf-8` com exatamente 6 campos: `type`, `title`, `status`, `code`, `detail`, `correlation_id` **SENÃO** falha | RFC 9457 | – | RF-17 | `10` §3.4, PRS-26 |
| RN-09 | Integridade | **SE** resposta resultar em status 500 ou 503 **ENTÃO** DEVE registar em log JSON com scope de correlação sem dados sensíveis (passwords, tokens, segredos) **SENÃO** não logar nível erro | Log estruturado | – | RF-17 | `10` §3.4, UT-3-00 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-15 e RF-17 — Comportamentos transversais, rotas e tratamento global

  Contexto:
    Dado que o serviço Kestrel está em execução

  @CA-00.1 @RF-15 @RN-03 @RN-04 @RN-05 @MUST @critico
  Cenário: Validação transversal de autenticação e denylist na camada 2
    Dado que existe um token JWT válido para o utilizador "01900000-0000-7000-8000-000000000001"
    E que a chave "denylist:user:01900000-0000-7000-8000-000000000001" existe no Redis com valor maior ou igual ao "iat" do token
    Quando é enviado POST "/api/v1/auth/switch-context" com o token no header "Authorization"
    Então o status da resposta é 403
    E o campo "code" é "ERR-AUTH-004"
    E o campo "title" é "Utilizador suspenso ou revogado globalmente"

  @CA-00.2 @RF-15 @RN-06 @MUST
  Cenário: Ativação correta da política de BypassTenantFilter
    Dado que é enviado POST "/api/v1/auth/login" sem token
    Quando o pipeline de autenticação resolve o contexto
    Então "BypassTenantFilter" é definido como verdadeiro
    E quando é enviado POST "/api/v1/tenants" por utilizador com "platform_role" igual a "NONE"
    Então "BypassTenantFilter" é definido como falso

  @CA-00.3 @RF-15 @RN-01 @RN-08 @MUST @borda
  Esquema do Cenário: Validação de limites de transporte e correlação
    Quando é enviado POST "/api/v1/auth/login" com cabeçalho "X-Correlation-Id" preenchido com <correlacao>
    Então o status da resposta é <status_http>
    E o cabeçalho "X-Correlation-Id" na resposta é retornado em formato UUID D minúsculo

    Exemplos:
      | correlacao                             | status_http |
      | "01900000-0000-7000-8000-000000000001" | 400         |
      | "invalido-formato"                     | 400         |

  @CA-00.4 @RF-17 @RN-02 @RN-07 @RN-09 @MUST @critico @borda
  Cenário: Tratamento de indisponibilidade de infraestrutura e chave com resposta 503
    Dado que a chave "ISigningKey" lança "CryptographicException" ao ser resolvida
    Quando é enviado POST "/api/v1/auth/switch-context" com token no header "Authorization"
    Então o status da resposta é 503
    E o campo "code" é "ERR-SYS-002"
    E a resposta contém o campo "detail" igual a "Dependência indisponível"
    E a mensagem de log estruturado JSON é emitida contendo "correlation_id"
    E nenhum dado sensível de token ou corpo é exposto no log

  @CA-00.5 @RF-17 @RN-07 @RN-08 @MUST
  Cenário: Classificação de violação de concorrência e unicidade via InnerException
    Dado que uma operação gera exceção de base de dados com SqlState "23505" na cadeia de exceções internas
    Quando o tratador global de exceções processa a falha
    Então o status da resposta é 409
    E o campo "code" é "ERR-CTX-005"
    E o corpo cumpre rigorosamente o formato RFC 9457 com 6 campos
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-15 | MUST | RN-01 a RN-06 | Transversal | EX-00.1 a EX-00.6 | CA-00.1, CA-00.2, CA-00.3 | F-00 |
| RF-17 | MUST | RN-07 a RN-09 | Transversal | EX-17.1, EX-17.2 | CA-00.4, CA-00.5 | F-00 |

**Totais:** RF = 2 · RN = 9 · EP = 0 · EV = 0 · CA = 5 (`@critico` = 2) · Σw = 7

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-100 | Resolução de `ISigningKey` ocorre antes da inspeção de `Authorization` em todas as rotas autenticadas | Garante que falha de chave retorne 503 `ERR-SYS-002` antes de qualquer verificação de formato (PRS-71) | PO, 2026-10-19 |
| PRS-101 | Inspeção de `InnerException` pelo tratador global não utiliza `EnableRetryOnFailure` | Falhas de BD falham imediatamente e cumprem tempo de resposta ≤ 15 s (PRS-70) | PO, 2026-10-19 |
| PRS-102 | Comparação de `denylist:user` com `iat` utiliza operador menor ou igual | Bloqueia tokens cunhados no mesmo segundo exato da revogação (PRS-61) | PO, 2026-10-19 |

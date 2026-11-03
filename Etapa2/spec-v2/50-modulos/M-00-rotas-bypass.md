# M-00 · Rotas, bypass e tratamento global

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-00) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** este ficheiro + `10` + `30` (ENT-09, ENUM-08) + `40` (IF-03, IF-04, IF-05, IF-08) + `70` + `20` + `90`. Total: 57 KiB (orçamento: 60 KiB).

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
O M-00 intercepta todos os endpoints (EP-01 a EP-10), executando validação de transporte, autenticação (camadas 1 e 2), classificação global de exceções e registo de auditoria/denylist.

### Interfaces internas exportadas (`40` §4.3)
- **`IF-03 IAuditLog`** (`IF-03.1 Append`): Registo síncrono em `auth_audit_log` no `DbContext` do chamador.
- **`IF-04 IDenylist`** (`IF-04.1..6`): Gestão no Redis (`RevokeJtiAsync`, `BlockUserAsync`, `BlockTenantAsync`, `UnblockTenantAsync`, `GetUserRevokedAtAsync`, `GetBlockedTenantsAsync`).
- **`IF-05 ISigningKey`** (`IF-05.1`): Material RSA 2048 bits para assinatura e JWKS (`KeyId`, `SigningKey`, `Modulus`, `Exponent`).
- **`IF-08 IAuthContext`** (`IF-08.1`, `IF-08.2`): Contexto scoped (`UserId`, `TenantId`, `AllowedTenants`, `BypassTenantFilter`) e `AuthorizeTenantWrite`.

## 3. Requisitos funcionais

### RF-15 — Autenticação transversal, contexto e bypass
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Pedido HTTP recebido em qualquer endpoint do pipeline Kestrel |
| Pré-condições | `ISigningKey` operacional |
| RN | RN-20, RN-21, RN-22, RN-23, RN-24, RN-25, RN-26 |

**Fluxo principal:**
1. Extrai `X-Correlation-Id` (ou gera UUID v7 se ausente/inválido) e determina o IP de origem conforme PRS-52.
2. Inicia scope de log com `correlation_id`, `user_id`, `actor_user_id`, `tenant_id`, `group_id`, `active_scope`.
3. Valida transporte: corpo ≤ 65 536 bytes e Content-Type JSON (camada 1).
4. Obtém `ISigningKey.SigningKey` do DI antes de validar autorização (PRS-71).
5. Se rota pública (EP-01, EP-03, EP-10), ignora `Authorization` e define `IsAuthenticated = false`.
6. Se rota com token obrigatório (EP-02, EP-04 a EP-09), executa em ordem estrita: RG-06 (token ausente/inválido/expirado/`denylist:jti` → 401 `ERR-AUTH-002`), RG-07 (`denylist:user` com valor ≥ `iat` → 403 `ERR-AUTH-004`), RG-08 (`denylist:tenant` em `allowed_tenants` → 403 `ERR-CTX-002`).
7. Hidrata `IAuthContext` e define `BypassTenantFilter = true` se rota for EP-01 a EP-05 ou `platform_role = PLATFORM_ADMIN`; `false` nos restantes.
8. Encaminha execução para o endpoint de destino.

**Fluxos de exceção:** EX-15.1 (corpo > 65 536 bytes → 413 `ERR-REQ-003`); EX-15.2 (Content-Type inválido → 415 `ERR-REQ-004`); EX-15.3 (falha na chave `ISigningKey` → 503 `ERR-SYS-002`); EX-15.4 (violação RG-06 → 401 `ERR-AUTH-002`); EX-15.5 (violação RG-07 → 403 `ERR-AUTH-004`); EX-15.6 (violação RG-08 → 403 `ERR-CTX-002`).

---

### RF-17 — Tratamento global de exceções, RFC 9457 e logging
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Exceção não tratada disparada durante a execução de qualquer pedido |
| Pré-condições | Nenhuma |
| RN | RN-27, RN-28, RN-29 |

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
| Pedidos concorrentes com falha de dependência | Respondem 503 `ERR-SYS-002` em ≤ 15 s sem travar o runtime | CA-17.1 |
| Cabeçalho `X-Correlation-Id` concorrente | Preserva o ID válido por pedido de forma isolada no contexto | CA-15.3 |
| Queda simultânea de Postgres e Redis | Classifica o primeiro elemento da cadeia de exceções como 503 | CA-17.1 |

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-20 | Validação | **SE** corpo > 65 536 bytes **ENTÃO** responder 413 `ERR-REQ-003` **SENÃO** prosseguir | 65 536 bytes | `ERR-REQ-003` | RF-15 | `40` §2 |
| RN-21 | Validação | **SE** corpo ≥ 1 byte E `Content-Type` não for `application/json` com `charset=utf-8` **ENTÃO** responder 415 `ERR-REQ-004` **SENÃO** prosseguir | `charset=utf-8` | `ERR-REQ-004` | RF-15 | `40` §2 |
| RN-22 | Validação | **SE** resolver `ISigningKey` lançar `CryptographicException` **ENTÃO** responder 503 `ERR-SYS-002` em todos os endpoints que a usam **SENÃO** prosseguir | – | `ERR-SYS-002` | RF-15 | PRS-71, C6 |
| RN-23 | Autorização | **SE** endpoint exige token E (JWT ausente, assinatura inválida, expirado, `kid` errado OU existir `denylist:jti:{jti}`) **ENTÃO** responder 401 `ERR-AUTH-002` **SENÃO** prosseguir | Tolerância exp 0 | `ERR-AUTH-002` | RF-15 | RG-06, `10` §3.9 |
| RN-24 | Autorização | **SE** endpoint exige token E existir `denylist:user:{sub}` com valor ≥ `iat` **ENTÃO** responder 403 `ERR-AUTH-004` **SENÃO** prosseguir | Comparação ≤ (PRS-61) | `ERR-AUTH-004` | RF-15 | RG-07, `10` §3.9 |
| RN-25 | Autorização | **SE** endpoint exige token E existir `denylist:tenant:{t}` para algum `t` em `allowed_tenants` **ENTÃO** responder 403 `ERR-CTX-002` **SENÃO** prosseguir | Consulta MGET | `ERR-CTX-002` | RF-15 | RG-08, `10` §3.9 |
| RN-26 | Integridade | **SE** pedido for para EP-01 a EP-05 OU chamador tiver `platform_role = PLATFORM_ADMIN` **ENTÃO** `BypassTenantFilter` DEVE ser `true` **SENÃO** DEVE ser `false` | Relação de rotas `40` §1.1 | – | RF-15 | `40` §1.1 |
| RN-27 | Integridade | **SE** exceção não tratada ocorrer **ENTÃO** o tratador global DEVE inspecionar a cadeia de `InnerException` na ordem exata de `40` §1.2 **SENÃO** responder 500 `ERR-SYS-001` | Ordem 1 a 5 | – | RF-17 | PRS-65, `40` §1.2 |
| RN-28 | Formato | **SE** erro for gerado **ENTÃO** resposta DEVE ter `Content-Type: application/problem+json; charset=utf-8` com exatamente 6 campos: `type`, `title`, `status`, `code`, `detail`, `correlation_id` **SENÃO** falha | RFC 9457 | – | RF-17 | `10` §3.4, PRS-26 |
| RN-29 | Integridade | **SE** resposta resultar em status 500 ou 503 **ENTÃO** DEVE registar em log JSON com scope de correlação sem dados sensíveis (passwords, tokens, segredos) **SENÃO** não logar nível erro | Log estruturado | – | RF-17 | `10` §3.4, UT-3-00 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-15 e RF-17 — Comportamentos transversais, rotas e tratamento global

  Contexto:
    Dado que o serviço Kestrel está em execução

  @CA-15.1 @RF-15 @RN-23 @RN-24 @RN-25 @MUST @critico @borda
  Cenário: Validação transversal de autenticação e denylist na camada 2
    Dado que existe um token JWT válido para o utilizador "01900000-0000-7000-8000-000000000001"
    E que a chave "denylist:user:01900000-0000-7000-8000-000000000001" existe no Redis com valor maior ou igual ao "iat" do token
    Quando é enviado POST "/api/v1/auth/switch-context" com o token no header "Authorization"
    Então o status da resposta é 403
    E o campo "code" é "ERR-AUTH-004"
    E o campo "title" é "Utilizador suspenso ou revogado globalmente"

  @CA-15.2 @RF-15 @RN-26 @MUST @borda
  Cenário: Comportamento observável de rota pública e bypass sem token
    Dado que é enviado POST "/api/v1/auth/login" sem cabeçalho "Authorization" e com corpo JSON inválido "{}"
    Quando o pipeline de middleware processa o pedido
    Então o pedido não é barrado com status 401
    E chega ao endpoint respondendo status 400
    E o campo "code" é "ERR-VAL-001"

  @CA-15.3 @RF-15 @RN-20 @RN-21 @RN-28 @MUST @borda
  Esquema do Cenário: Validação de limites de transporte e correlação
    Quando é enviado POST "/api/v1/auth/login" com cabeçalho "X-Correlation-Id" preenchido com <correlacao>
    Então o status da resposta é <status_http>
    E o cabeçalho "X-Correlation-Id" na resposta é retornado em formato UUID D minúsculo

    Exemplos:
      | correlacao                             | status_http |
      | "01900000-0000-7000-8000-000000000001" | 400         |
      | "invalido-formato"                     | 400         |

  @CA-17.1 @RF-17 @RN-22 @RN-27 @RN-29 @MUST @critico @borda
  Cenário: Tratamento de indisponibilidade de infraestrutura e chave com resposta 503
    Dado que a chave "ISigningKey" lança "CryptographicException" ao ser resolvida
    Quando é enviado POST "/api/v1/auth/switch-context" com token no header "Authorization"
    Então o status da resposta é 503
    E o campo "code" é "ERR-SYS-002"
    E a resposta contém o campo "detail" igual a "Dependência indisponível"
    E a mensagem de log estruturado JSON é emitida contendo "correlation_id"
    E nenhum dado sensível de token ou corpo é exposto no log

  @CA-17.2 @RF-17 @RN-27 @RN-28 @MUST @borda
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
| RF-15 | MUST | RN-20 a RN-26 | Transversal | EX-15.1 a EX-15.6 | CA-15.1, CA-15.2, CA-15.3 | F-00 |
| RF-17 | MUST | RN-27 a RN-29 | Transversal | EX-17.1, EX-17.2 | CA-17.1, CA-17.2 | F-00 |

**Totais:** RF = 2 · RN = 10 · EP = 0 · EV = 0 · CA = 5 (`@critico` = 2) · Σw = 15

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-100 | Resolução de `ISigningKey` ocorre antes da inspeção de `Authorization` em todas as rotas autenticadas | Garante que falha de chave retorne 503 `ERR-SYS-002` antes de qualquer verificação de formato (PRS-71) | PO, 2026-11-02 (D-23) |
| PRS-101 | Inspeção de `InnerException` pelo tratador global não utiliza `EnableRetryOnFailure` | Falhas de BD falham imediatamente e cumprem tempo de resposta ≤ 15 s (PRS-70) | PO, 2026-11-02 (D-23) |
| PRS-102 | Comparação de `denylist:user` com `iat` utiliza operador menor ou igual | Bloqueia tokens cunhados no mesmo segundo exato da revogação (PRS-61) | PO, 2026-11-02 (D-23) |

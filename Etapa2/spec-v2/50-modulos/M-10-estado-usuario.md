# M-10 · Estado do utilizador

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-10) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** `10` + `30` + `40` (EP-09, EV-03) + `70` + `90`. Total: 47 KiB.

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Alteração de estado de utilizador (EP-09) em `GLOBAL` ou `GROUP` | Login e bloqueio por credenciais (M-01) |
| Suspensão e reativação (IF-02.1, IF-12.2) | Emissão Kafka (M-14), denylist Redis (M-00) |
| Revogação de sessões (IF-01.4/01.6), auditoria | Membros (M-05) e acessos (M-08) |

**Escreve:** Sem escrita direta · **Lê:** `user_account` · **Usa:** M-00 (IF-03/04/08), M-01 (IF-02), M-02 (IF-01), M-05 (IF-12), M-08 (IF-09), M-14 (IF-07)

## 2. Endpoints e eventos (detalhe)

### EP-09 `PATCH /api/v1/users/{user_id}/status`
| Campo | Valor |
|---|---|
| RF / Idempotência | RF-10, RF-13 / Sim (200 com dados atualizados) |
| Permissão | PLATFORM_ADMIN OU C9 (TENANT_ADMIN, usuario:suspender, alvo ≠ sub, IF-09.7 = MEMBER, alvo NONE) |
| RN | RN-120 a RN-129 |

**Request:** `user_id` (UUID `D`); body `{"status":"SUSPENDED","reason":"Violação de termos"}` (`ACTIVE`\|`SUSPENDED`, `reason` 5..255).
**Ordem:** 1. Rota (404/405). 2. Transporte (413/415). 3. Formato (RN-120 → 400). 4. Autorização (RN-121 → 403). 5. Existência (RN-122 → 404). 6. Execução (RN-123..129 → 200).
**Respostas:** 200; 400 ERR-VAL-001; 401 ERR-AUTH-002; 403 ERR-AUTH-004/ERR-CTX-002/007; 404 ERR-REQ-001/ERR-CTX-008; 405 ERR-REQ-002; 413 ERR-REQ-003; 415 ERR-REQ-004; 500 ERR-SYS-001; 503 ERR-SYS-002.
**Sucesso (200):** `{"user_id":"0192e2c0-0001-7000-8000-000000000001","status":"SUSPENDED","scope":"GLOBAL","group_id":null,"updated_at":"2026-10-19T18:00:00Z"}`.
**Efeitos:** `GLOBAL`: IF-02.1; se `SUSPENDED`: IF-01.4, IF-04.2, IF-07.4, auditoria. `GROUP`: IF-12.2; se `SUSPENDED` e vínculos alterados: IF-01.6, auditoria. Transação atómica.

## 3. Requisitos funcionais

### RF-10 — Suspender utilizador
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / `PLATFORM_ADMIN` ou `TENANT_ADMIN` via EP-09 (`SUSPENDED`) |
| Pré-condições / RN | BD e Redis operacionais; token válido / RN-120..123, RN-125, RN-127..129 |

**Fluxo principal:** 1. Valida rota, auth e formato (RN-120). 2. Autoriza `PLATFORM_ADMIN` ou C9 (RN-121). 3. Se `PLATFORM_ADMIN`: valida existência (RN-122); em transação única atualiza conta (IF-02.1), revoga famílias (IF-01.4), denylist (IF-04.2), emite EV-03 (IF-07.4) e grava auditoria. 4. Se `TENANT_ADMIN`: atualiza vínculos (IF-12.2); revoga famílias alteradas (IF-01.6); grava auditoria. 5. 200 OK.
**Fluxos de exceção:** EX-10.1 (formato → 400); EX-10.2 (sem permissão/C9 → 403); EX-10.3 (inexistente → 404); EX-10.4 (falha BD/Redis → 503).

### RF-13 — Reativar utilizador
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / `PLATFORM_ADMIN` ou `TENANT_ADMIN` via EP-09 (`ACTIVE`) |
| Pré-condições / RN | BD e Redis operacionais; token válido / RN-120..122, RN-124, RN-126..129 |

**Fluxo principal:** 1. Valida rota, auth e formato (RN-120). 2. Autoriza `PLATFORM_ADMIN` ou C9 (RN-121). 3. Se `PLATFORM_ADMIN`: valida existência (RN-122); em transação única atualiza conta (IF-02.1), zera falhas, limpa `locked_until` e grava auditoria. 4. Se `TENANT_ADMIN`: atualiza vínculos para `ACTIVE` (IF-12.2) e grava auditoria. 5. 200 OK.
**Fluxos de exceção:** EX-13.1 (formato → 400); EX-13.2 (sem permissão/C9 → 403); EX-13.3 (inexistente → 404); EX-13.4 (falha BD/Redis → 503).

**Concorrência e pós-condições:** Repetição de estado devolve 200 (CA-10.5, CA-13.3). Repetição `GLOBAL`+`SUSPENDED` reexecuta revogação, denylist e `EV-03` (RN-127). Rollback total na falha (RN-129). Pós-condição: status/vínculos e sessões atualizados.

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-120 | Formato | **SE** EP-09 **ENTÃO** `user_id` UUID `D`, `status` ∈ {`ACTIVE`, `SUSPENDED`}, `reason` 5..255 **SENÃO** 400 | 5..255 | `ERR-VAL-001` | RF-10, 13 | PRS-37 |
| RN-121 | Autorização | **SE** EP-09 **ENTÃO** autoriza `PLATFORM_ADMIN` OU C9 (`TENANT_ADMIN`, `usuario:suspender`, alvo ≠ `sub`, IF-09.7 = `MEMBER`, alvo `NONE`) **SENÃO** 403 | C9 | `ERR-CTX-007` | RF-10, 13 | D-22, PRS-81 |
| RN-122 | Existência | **SE** `PLATFORM_ADMIN` e `user_id` ausente em `user_account` **ENTÃO** 404 **SENÃO** segue | `user_account` | `ERR-CTX-008` | RF-10, 13 | 40 §2 |
| RN-123 | Integridade | **SE** `PLATFORM_ADMIN` e `SUSPENDED` **ENTÃO** atualiza conta (IF-02.1), revoga famílias (IF-01.4), denylist (IF-04.2), emite `EV-03` (IF-07.4) **SENÃO** falha | TTL 900 s | – | RF-10 | D-07, PRS-39 |
| RN-124 | Integridade | **SE** `PLATFORM_ADMIN` e `ACTIVE` **ENTÃO** atualiza conta (IF-02.1), zera falhas, limpa `locked_until`, sem Redis nem `EV-03` **SENÃO** falha | – | – | RF-13 | PRS-39, PRS-48 |
| RN-125 | Integridade | **SE** `TENANT_ADMIN` e `SUSPENDED` **ENTÃO** atualiza vínculos não-dono (IF-12.2), revoga famílias alteradas (IF-01.6), sem conta, Redis nem `EV-03` **SENÃO** falha | – | – | RF-10 | D-07, PRS-86 |
| RN-126 | Integridade | **SE** `TENANT_ADMIN` e `ACTIVE` **ENTÃO** atualiza vínculos não-dono (IF-12.2) para `ACTIVE`, sem revogação nem `EV-03` **SENÃO** falha | – | – | RF-13 | D-07, PRS-81 |
| RN-127 | Integridade | **SE** repete estado **ENTÃO** 200: em `GLOBAL`+`SUSPENDED` reexecuta IF-01.4/04.2 e `EV-03`; em `ACTIVE` sem efeitos **SENÃO** falha | – | – | RF-10, 13 | UT-3-10 |
| RN-128 | Temporal | **SE** 200 gerado **ENTÃO** `TimeProvider` truncado ao segundo e grava auditoria `USER_STATUS_CHANGED` **SENÃO** falha | Truncado s | – | RF-10, 13 | UT-3-10, PRS-82 |
| RN-129 | Integridade | **SE** escrita falhar **ENTÃO** rollback de transação **SENÃO** commit | – | `ERR-SYS-001`/`002` | RF-10, 13 | 10 §3.4 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-10 / RF-13 — Estado do utilizador
  Contexto:
    Dado grupo "0192e2c0-00g1-7000-8000-000000000001" e alvo "0192e2c0-0001-7000-8000-000000000001" (NONE)

  @CA-10.1 @RF-10 @RN-121 @RN-123 @RN-128 @MUST @critico @borda
  Cenário: Suspensão global por PLATFORM_ADMIN (5 chars)
    Dado alvo ACTIVE e família ativa
    Quando PATCH status por PLATFORM_ADMIN com status "SUSPENDED" e reason "12345"
    Então status 200, scope "GLOBAL", group_id null, updated_at "2026-04-19T12:00:00Z"
    E alvo SUSPENDED, família revogada, denylist ativa, EV-03 enfileirado e auditoria gravada

  @CA-10.2 @RF-10 @RN-121 @RN-125 @MUST @critico @borda
  Cenário: Suspensão em grupo por TENANT_ADMIN altera não-donos
    Dado alvo membro no grupo com is_owner false e família vinculada
    Quando PATCH status por TENANT_ADMIN no grupo com status "SUSPENDED" e reason "Suspenso"
    Então status 200, scope "GROUP", vínculos SUSPENDED, família vinculada revogada e sem EV-03

  @CA-10.3 @RF-10 @RN-121 @MUST @borda
  Esquema do Cenário: Rejeição se C9 for violada
    Quando PATCH status de alvo <tipo> por TENANT_ADMIN no grupo
    Então status 403 e código "ERR-CTX-007"
    Exemplos:
      | tipo |
      | "proprio_usuario" |
      | "proprietario" |
      | "sem_vinculo" |

  @CA-10.4 @RF-10 @RN-122 @MUST @borda
  Esquema do Cenário: Alvo inexistente devolve 404 ou 403
    Quando PATCH status inexistente por <ator>
    Então status é <status> e erro é <erro>
    Exemplos:
      | ator | status | erro |
      | "PLATFORM_ADMIN" | 404 | "ERR-CTX-008" |
      | "TENANT_ADMIN" | 403 | "ERR-CTX-007" |

  @CA-10.5 @RF-10 @RN-127 @MUST @critico @borda
  Cenário: Repetição de SUSPENDED em GLOBAL reexecuta efeitos
    Dado alvo já SUSPENDED em user_account
    Quando PATCH status por PLATFORM_ADMIN com status "SUSPENDED" e reason "Repetir"
    Então status 200, IF-01.4 reexecutada, denylist renovada, novo EV-03 e nova auditoria

  @CA-13.1 @RF-13 @RN-121 @RN-124 @MUST @borda
  Cenário: Reativação global por PLATFORM_ADMIN desbloqueia LOCKED
    Dado alvo LOCKED com 5 falhas e locked_until 1776600900
    Quando PATCH status por PLATFORM_ADMIN com status "ACTIVE" e reason "Reativa"
    Então status 200, status "ACTIVE", scope "GLOBAL", falhas 0, locked_until null e sem EV-03

  @CA-13.2 @RF-13 @RN-121 @RN-126 @MUST @borda
  Cenário: Reativação em grupo por TENANT_ADMIN sem alteração
    Dado vínculos do alvo no grupo já ACTIVE
    Quando PATCH status por TENANT_ADMIN no grupo com status "ACTIVE" e reason "Reativa"
    Então status 200, status "ACTIVE", scope "GROUP" e auditoria gravada

  @CA-13.3 @RF-13 @RN-120 @RN-127 @MUST @borda
  Esquema do Cenário: Validação de corpo malformado e repetição
    Quando PATCH status por PLATFORM_ADMIN com corpo <corpo>
    Então status é <status> e erro é <erro>
    Exemplos:
      | corpo | status | erro |
      | "{\"status\":\"ACTIVE\",\"reason\":\"Reativa\"}" | 200 | "Nenhum" |
      | "{\"status\":\"LOCKED\",\"reason\":\"Bloqueio\"}" | 400 | "ERR-VAL-001" |
      | "{\"status\":\"ACTIVE\",\"reason\":\"1234\"}" | 400 | "ERR-VAL-001" |
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-10 | MUST | RN-120 a RN-123, RN-125, RN-127 a RN-129 | EP-09, EV-03 | EX-10.1 a EX-10.4 | CA-10.1 a CA-10.5 | F-03 |
| RF-13 | MUST | RN-120 a RN-122, RN-124, RN-126 a RN-129 | EP-09 | EX-13.1 a EX-13.4 | CA-13.1 a CA-13.3 | F-03 |

**Totais:** RF = 2 · RN = 10 · EP = 1 · EV = 1 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-200 | `updated_at` retornado em `GROUP` utiliza instante do TimeProvider mesmo com 0 vínculos alterados | Coerência temporal | PO, 2026-11-02 (D-23) |
| PRS-201 | Em suspensão `GROUP`, apenas famílias com contexto nos tenants alterados por IF-12.2 são revogadas | Limitar escopo | PO, 2026-11-02 (D-23) |
| PRS-202 | Repetição de `SUSPENDED` em `GLOBAL` reemite `EV-03` e renova TTL em Redis | Bloquear sessões | PO, 2026-11-02 (D-23) |

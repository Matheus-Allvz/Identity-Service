# M-10 · Estado do utilizador

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-10) · **Estado:** Proposta · **Atualizado:** 2026-10-19
> **Pacote:** este doc + `10` + `30` + `40` (EP-09, EV-03, IFs) + `70` + `90` (UT-3-10, PRS-37/39). Total: ~47 KiB (teto ≤ 60 KiB).

## 1. Âmbito do módulo

| Dentro | Fora |
|---|---|
| Alteração de estado de utilizador (EP-09) em âmbito `GLOBAL` ou `GROUP` | Autenticação, login e bloqueio por falhas de credenciais (M-01) |
| Orquestração de suspensão e reativação via `IF-02.1` e `IF-12.2` | Emissão física no Kafka (M-14) e denylist no Redis (M-00) |
| Revogação de sessões (`IF-01.4`/`IF-01.6`) e auditoria `USER_STATUS_CHANGED` | Gestão de membros e papéis (M-05) e resolução de acessos (M-08) |

**Escreve:** Sem escrita direta (usa interfaces) · **Lê:** `user_account` via `IF-02.1` · **Usa:** M-00 (IF-03, IF-04, IF-08), M-01 (IF-02), M-02 (IF-01), M-05 (IF-12), M-08 (IF-09), M-14 (IF-07)

## 2. Endpoints e eventos (detalhe)

### EP-09 `PATCH /api/v1/users/{user_id}/status`

| Campo | Valor |
|---|---|
| RF | RF-10, RF-13 |
| Permissão | `PLATFORM_ADMIN` OU C9 (`TENANT_ADMIN`, `usuario:suspender`, alvo ≠ `sub`, IF-09.7 = `MEMBER`, alvo `platform_role = NONE`) |
| Idempotência | Sim. Repetição do estado devolve 200 com dados atualizados |
| RN | RN-120 a RN-129 |

**Request:** path `user_id` (UUID `D`); body:
```json
{"status":"SUSPENDED","reason":"Violação de termos"}
```

**Ordem de avaliação:** 1. Rota (404/405). 2. Transporte (413/415). 3. Formato (RN-120 → 400 `ERR-VAL-001`). 4. Autorização (RN-121 → 403 `ERR-CTX-007`). 5. Existência (RN-122 → 404 `ERR-CTX-008`). 6. Execução (RN-123 a RN-129 → 200).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`/`ERR-CTX-002`/`007`; 404 `ERR-REQ-001`/`ERR-CTX-008`; 405 `ERR-REQ-002`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Sucesso (200):** `user_id` (UUID `D`), `status` (`ACTIVE` | `SUSPENDED`), `scope` (`"GLOBAL"` | `"GROUP"`), `group_id` (UUID `D` em `GROUP`, `null` em `GLOBAL`), `updated_at` (instante truncado ao segundo).
```json
{"user_id":"0192e2c0-0001-7000-8000-000000000001","status":"SUSPENDED","scope":"GLOBAL","group_id":null,"updated_at":"2026-10-19T18:00:00Z"}
```
**Efeitos:** `GLOBAL`: IF-02.1; se `SUSPENDED`: IF-01.4, IF-04.2, IF-07.4; auditoria `USER_STATUS_CHANGED`. `GROUP`: IF-12.2; se `SUSPENDED` e vínculos alterados: IF-01.6; auditoria `USER_STATUS_CHANGED`.
**Transação:** Base e outbox atómicas em transação única.

## 3. Requisitos funcionais

### RF-10 — Suspender utilizador / RF-13 — Reativar utilizador

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | `PLATFORM_ADMIN` ou `TENANT_ADMIN` via EP-09 (`status` = `SUSPENDED` ou `ACTIVE`) |
| Pré-condições | BD e Redis disponíveis; token válido com permissão |
| RN | RN-120 a RN-129 |

**Fluxo principal:** 1. Recebe PATCH EP-09. 2. Valida transporte, auth e formato. 3. Autoriza `PLATFORM_ADMIN` ou C9. 4. Se `PLATFORM_ADMIN`: valida existência (404); executa `IF-02.1`; se `SUSPENDED`, executa `IF-01.4`, `IF-04.2` e enfileira `EV-03` (`IF-07.4`). 5. Se `TENANT_ADMIN`: executa `IF-12.2`; se `SUSPENDED` e vínculos alterados, executa `IF-01.6`. 6. Regista auditoria `USER_STATUS_CHANGED` e retorna 200.

**Fluxos de exceção:** EX-10.1 (formato/`LOCKED` → 400 `ERR-VAL-001`); EX-10.2 (token → 401 `ERR-AUTH-002`); EX-10.3 (denylist → 403 `ERR-AUTH-004`/`ERR-CTX-002`); EX-10.4 (C9 reprovada → 403 `ERR-CTX-007`); EX-10.5 (alvo ausente → 404 `ERR-CTX-008`); EX-10.6 (falha BD/Redis → 503 `ERR-SYS-002`).

**Concorrência e idempotência:** Pedidos com mesmo estado retornam 200 (CA-13.7). Repetição de `GLOBAL` + `SUSPENDED` reexecuta revogação, denylist e `EV-03` (RN-127). Falha de BD/outbox desfaz transação sem efeitos parciais.

**Pós-condições:** `GLOBAL`: status atualizado, sessões revogadas e `EV-03` se `SUSPENDED`. `GROUP`: vínculos não-dono atualizados e sessões revogadas se `SUSPENDED`.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-120 | Formato | **SE** EP-09 recebido **ENTÃO** `user_id` UUID `D`, `status` ∈ {`ACTIVE`, `SUSPENDED`} e `reason` 5..255 chars **SENÃO** 400 | `LEN = 5..255` | `ERR-VAL-001` | RF-10, RF-13 | PRS-37 |
| RN-121 | Autorização | **SE** EP-09 avaliado **ENTÃO** autoriza **SE** `PLATFORM_ADMIN` OU C9 (`TENANT_ADMIN`, `usuario:suspender`, alvo ≠ `sub`, IF-09.7 = `MEMBER`, alvo `platform_role = NONE`) **SENÃO** 403 | – | `ERR-CTX-007` | RF-10, RF-13 | D-22, PRS-81 |
| RN-122 | Existência | **SE** chamador `PLATFORM_ADMIN` **ENTÃO** `user_id` **DEVE** existir em `user_account` **SENÃO** 404 | – | `ERR-CTX-008` | RF-10, RF-13 | 40 §2 |
| RN-123 | Integridade | **SE** `PLATFORM_ADMIN` pedir `SUSPENDED` **ENTÃO** **DEVE** atualizar conta via `IF-02.1`, revogar famílias via `IF-01.4`, bloquear Redis via `IF-04.2` e enfileirar `EV-03` (`IF-07.4`) **SENÃO** falha | `TTL = 900 s` | – | RF-10 | D-07, PRS-39 |
| RN-124 | Integridade | **SE** `PLATFORM_ADMIN` pedir `ACTIVE` **ENTÃO** **DEVE** atualizar conta via `IF-02.1`, zerar falhas e limpar `locked_until`, NÃO DEVE alterar Redis nem emitir `EV-03` **SENÃO** falha | – | – | RF-13 | PRS-39, PRS-48 |
| RN-125 | Integridade | **SE** `TENANT_ADMIN` pedir `SUSPENDED` **ENTÃO** **DEVE** atualizar vínculos não-dono via `IF-12.2` e revogar famílias dos vínculos alterados via `IF-01.6`, NÃO DEVE alterar `user_account`, Redis nem `EV-03` **SENÃO** falha | – | – | RF-10 | D-07, PRS-86 |
| RN-126 | Integridade | **SE** `TENANT_ADMIN` pedir `ACTIVE` **ENTÃO** **DEVE** atualizar vínculos não-dono via `IF-12.2` para `ACTIVE`, NÃO DEVE revogar famílias nem emitir `EV-03` **SENÃO** falha | – | – | RF-13 | D-07, PRS-81 |
| RN-127 | Integridade | **SE** estado igual ao atual **ENTÃO** **DEVE** aceitar (200): se `GLOBAL` + `SUSPENDED`, **DEVE** reexecutar `IF-01.4`, `IF-04.2` e `EV-03`; se `ACTIVE`, NÃO DEVE revogar famílias nem emitir `EV-03` **SENÃO** falha | – | – | RF-10, RF-13 | UT-3-10 |
| RN-128 | Temporal | **SE** 200 for gerado **ENTÃO** **DEVE** usar instante `TimeProvider` truncado ao segundo e registar 1 auditoria `USER_STATUS_CHANGED` **SENÃO** falha | – | – | RF-10, RF-13 | UT-3-10, PRS-82 |
| RN-129 | Integridade | **SE** escrita na base ou outbox falhar **ENTÃO** **DEVE** desfazer a transação inteira sem efeitos parciais **SENÃO** commit | – | `ERR-SYS-001`/`002` | RF-10, RF-13 | 10 §3.4 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-10 / RF-13 — Estado do utilizador

  Contexto:
    Dado grupo "0192e2c0-00g1-7000-8000-000000000001" e alvo "0192e2c0-0001-7000-8000-000000000001" (platform_role NONE)

  @CA-13.1 @RF-10 @RN-121 @RN-123 @RN-128 @MUST @critico
  Cenário: Suspensão global por PLATFORM_ADMIN (borda 5 chars)
    Dado alvo ACTIVE em user_account e família ativa
    Quando PATCH status do alvo por PLATFORM_ADMIN com status "SUSPENDED" e reason "12345"
    Então status 200, scope "GLOBAL", group_id null, updated_at "2026-04-19T12:00:00Z"
    E alvo SUSPENDED, família revogada, denylist ativa, EV-03 enfileirado e 1 auditoria

  @CA-13.2 @RF-13 @RN-121 @RN-124 @MUST
  Cenário: Reativação global por PLATFORM_ADMIN desbloqueia LOCKED
    Dado alvo LOCKED com 5 falhas, locked_until 1776600900 e denylist ativa
    Quando PATCH status do alvo por PLATFORM_ADMIN com status "ACTIVE" e reason "Reativa"
    Então status 200, status "ACTIVE", scope "GLOBAL", falhas 0, locked_until null e sem EV-03

  @CA-13.3 @RF-10 @RN-121 @RN-125 @MUST @critico
  Cenário: Suspensão em grupo por TENANT_ADMIN altera não-donos
    Dado alvo membro no grupo com is_owner false e família vinculada
    Quando PATCH status do alvo por TENANT_ADMIN no grupo com status "SUSPENDED" e reason "Suspenso"
    Então status 200, scope "GROUP", vínculos SUSPENDED, família vinculada revogada e sem EV-03

  @CA-13.4 @RF-13 @RN-121 @RN-126 @MUST
  Cenário: Reativação em grupo por TENANT_ADMIN com 0 vínculos alterados
    Dado vínculos do alvo no grupo já ACTIVE
    Quando PATCH status do alvo por TENANT_ADMIN no grupo com status "ACTIVE" e reason "Reativa"
    Então status 200, status "ACTIVE", scope "GROUP", updated_at "2026-04-19T12:00:00Z" e 1 auditoria

  @CA-13.5 @RF-10 @RN-121 @MUST
  Esquema do Cenário: Rejeição se Condição C9 for violada
    Quando PATCH status de alvo <tipo> por TENANT_ADMIN no grupo
    Então status 403 e código "ERR-CTX-007"
    Exemplos:
      | tipo |
      | "proprio_usuario" |
      | "proprietario" |
      | "sem_vinculo" |

  @CA-13.6 @RF-10 @RN-122 @MUST
  Cenário: Alvo inexistente devolve 404 para PLATFORM_ADMIN e 403 para TENANT_ADMIN
    Quando PATCH status inexistente por PLATFORM_ADMIN
    Então status 404 e código "ERR-CTX-008"
    Quando PATCH status inexistente por TENANT_ADMIN
    Então status 403 e código "ERR-CTX-007"

  @CA-13.7 @RF-10 @RN-127 @MUST @critico
  Cenário: Repetição de status SUSPENDED em GLOBAL reexecuta efeitos
    Dado alvo já SUSPENDED em user_account
    Quando PATCH status do alvo por PLATFORM_ADMIN com status "SUSPENDED" e reason "Repetir"
    Então status 200, IF-01.4 reexecutada, denylist renovada, novo EV-03 e nova auditoria

  @CA-13.8 @RF-10 @RN-120 @MUST
  Esquema do Cenário: Validação de corpo malformado
    Quando PATCH status do alvo com corpo <corpo>
    Então status 400 e código "ERR-VAL-001"
    Exemplos:
      | corpo |
      | "{\"status\":\"LOCKED\",\"reason\":\"Bloq\"}" |
      | "{\"status\":\"SUSPENDED\",\"reason\":\"1234\"}" |
      | "{\"status\":\"X\",\"reason\":\"Erro\"}" |
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-10 | MUST | RN-120 a RN-123, RN-125, RN-127 a RN-129 | EP-09 | EX-10.1 a EX-10.6 | CA-13.1, CA-13.3, CA-13.5 a CA-13.8 | F-00 |
| RF-13 | MUST | RN-120 a RN-122, RN-124, RN-126 a RN-129 | EP-09 | EX-10.1 a EX-10.6 | CA-13.2, CA-13.4 | F-00 |

**Totais:** RF = 2 · RN = 10 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado |
|---|---|---|---|
| PRS-200 | `updated_at` retornado em `GROUP` utiliza instante do TimeProvider mesmo com 0 vínculos alterados | Coerência temporal (PRS-82) | PO, 2026-10-19 |
| PRS-201 | Em suspensão `GROUP`, apenas famílias com contexto nos tenants alterados por IF-12.2 são revogadas | Limitar revogação ao escopo afetado (PRS-86) | PO, 2026-10-19 |
| PRS-202 | Repetição de `SUSPENDED` em `GLOBAL` reemite `EV-03` e renova TTL em Redis | Garantir bloqueio de sessões remanescentes | PO, 2026-10-19 |

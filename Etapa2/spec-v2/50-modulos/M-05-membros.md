# M-05 · Membros

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-05) · **Estado:** Rascunho · **Atualizado:** 2026-10-19
> **Pacote:** este doc + `10` + `30` (ENT-02..07, ENUM-01, AUD.M05) + `40` (§1..§3, EP-07, IF-03.1, IF-08.1/2, IF-11.3, IF-12/12.1/12.2, matriz §5) + `70` (§1) + `90` (UT-3-05, PRS-38). Total: ~55 KiB (teto ≤ 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Vínculo de membro via EP-07 | Utilizadores (M-01) e tenants (M-04) |
| Papéis em `member_role` | Catálogo `role` (seed M-11) |
| Auditoria via IF-03.1 | Consulta de contextos (M-08) |
| `IMembershipWriter` (IF-12.1, IF-12.2) | Suspensão global de utilizador (M-10) |

**Escreve:** ENT-05 (`tenant_member`), ENT-07 (`member_role`) · **Lê:** ENT-02..06 · **Usa:** M-00 (IF-03.1, IF-08.1/2), M-13 (IF-11.3)

## 2. Endpoints e eventos (detalhe)

### EP-07 `POST /api/v1/tenants/{tenant_id}/members`
| Campo | Valor |
|---|---|
| RF / RN | RF-08 / RN-70 a RN-75 |
| Permissão | JWT; `PLATFORM_ADMIN` OU C7 (`TENANT_ADMIN`, `membro:vincular`, `tenant_id` ∈ `allowed_tenants`) |
| Idempotência | Não. Par duplicado devolve 409 `ERR-CTX-005` |

**Request:** `tenant_id` (path UUID `D`); `user_id` (body UUID `D`); `is_owner` (bool); `can_view_group` (bool); `role_ids` (array 1..10 UUIDs `D` distintos).
```json
{"user_id":"01923456-789a-7bc8-9012-3456789abcd0","is_owner":false,"can_view_group":true,"role_ids":["01923456-789a-7bc8-9012-3456789abc01"]}
```

**Ordem de avaliação:** 1. Rota/método → 404/405. 2. Tamanho ≤ 64 KiB → 413. 3. Content-Type `application/json` → 415. 4. Assinatura/chave → 503 `ERR-SYS-002`. 5. JWT válido e sem jti em denylist → 401 `ERR-AUTH-002`. 6. Sem user em denylist → 403 `ERR-AUTH-004`. 7. Sem tenant em denylist → 403 `ERR-CTX-002`. 8. JSON válido, sem extras/repetidos → 400 `ERR-VAL-001`. 9. Campos obrigatórios e formatos UUID `D`/bool → 400. 10. `role_ids` 1..10 UUIDs distintos (RN-70) → 400. 11. Autorização `PLATFORM_ADMIN` ou C7 (RN-71) → 403 `ERR-CTX-007`. 12. Existência de tenant, utilizador e papéis (RN-72) → 404 `ERR-CTX-008`. 13. Unicidade do par (`tenant_id`, `user_id`) (RN-73) → 409 `ERR-CTX-005`. 14. Escopos de `role_ids` ativos no tenant (RN-74) → 422 `ERR-CTX-006`.

**Respostas:** 201; 400 `ERR-VAL-001`; 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`/`ERR-CTX-002`/`ERR-CTX-007`; 404 `ERR-REQ-001`/`ERR-CTX-008`; 405 `ERR-REQ-002`; 409 `ERR-CTX-005`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 422 `ERR-CTX-006`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Sucesso (201):** `member_id`, `tenant_id`, `user_id`, `is_owner`, `can_view_group`, `status` (`ACTIVE`), `roles` (nomes ordenados), `created_at`.
```json
{"member_id":"01923456-789a-7def-0123-456789abcdef","tenant_id":"01923456-789a-7bc8-9012-3456789abcde","user_id":"01923456-789a-7bc8-9012-3456789abcd0","is_owner":false,"can_view_group":true,"status":"ACTIVE","roles":["OPERADOR_COMPRAS"],"created_at":"2026-10-19T18:00:00Z"}
```

**Transação:** Atómica (rollback total se falha): 1. `AuthorizeTenantWrite(tenant_id)` (IF-08.2). 2. Insere `tenant_member`. 3. Insere `member_role`. 4. Regista auditoria `MEMBER_ADDED` (IF-03.1).

### IF-12 `IMembershipWriter`
- **IF-12.1 `AddCreatorMembership(Guid tenantId, Guid userId)`:** Insere `tenant_member` (`is_owner = false`, `can_view_group = false`, `ACTIVE`) e `member_role` com `GROUP_MANAGER`. Exige `tenantId` em `WriteTenants` (IF-08.2).
- **IF-12.2 `SetGroupMembershipsStatusAsync(Guid userId, Guid groupId, EntityStatus status)`:** Via `ExecuteWithBypassAsync` (IF-11.3). Atualiza status de membros do grupo com `is_owner = false` e status distinto via `ExecuteUpdateAsync` (RN-75). Devolve `tenant_id`s afetados.

## 3. Requisitos funcionais

### RF-08 — Vincular membro a tenant
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador `PLATFORM_ADMIN` ou `TENANT_ADMIN` com `membro:vincular` no tenant |
| Pré-condições | Token válido; tenant e utilizador existentes |
| RN | RN-70 a RN-75 |

**Fluxo principal:** 1. `POST /api/v1/tenants/{tenant_id}/members`. 2. Valida transporte, token, formato e RN-70. 3. Valida autorização (RN-71). 4. Invoca `AuthorizeTenantWrite(tenant_id)`. 5. Valida integridade (RN-72). 6. Valida unicidade (RN-73). 7. Valida escopos ativos (RN-74). 8. Em transação única: grava `tenant_member`, `member_role` e auditoria `MEMBER_ADDED` (IF-03.1). 9. Responde 201 com dados criados.

**Fluxos de exceção:** EX-08.1 (formato inválido → 400); EX-08.2 (token inválido/ausente → 401); EX-08.3 (denylist → 403); EX-08.4 (sem permissão → 403 `ERR-CTX-007`); EX-08.5 (recurso inexistente → 404 `ERR-CTX-008`); EX-08.6 (membro duplicado → 409 `ERR-CTX-005`); EX-08.7 (papel incompatível → 422 `ERR-CTX-006`); EX-08.8 (falha BD → 503 `ERR-SYS-002`, rollback).

**Concorrência e idempotência:** Par duplicado concorrente resulta em exatamente um 201 e um 409 `ERR-CTX-005` (CA-08.3). Falha no processo executa rollback sem resíduos (CA-08.5). Reenvio devolve 409 `ERR-CTX-005`.

**Pós-condições:** Registo em `tenant_member` `ACTIVE`; registos em `member_role`; registo em `auth_audit_log`.

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-70 | Validação | **SE** `role_ids` tiver < 1 ou > 10 elementos OU repetidos **ENTÃO** 400 **SENÃO** segue | 1..10 itens | `ERR-VAL-001` | RF-08 | PRS-38 |
| RN-71 | Autorização | **SE** `platform_role = PLATFORM_ADMIN` OU (`active_scope = TENANT_ADMIN` E `membro:vincular` ∈ `permissions` E `tenant_id` ∈ `allowed_tenants`) **ENTÃO** autoriza **SENÃO** 403 | `membro:vincular` | `ERR-CTX-007` | RF-08 | Matriz §5 |
| RN-72 | Integridade | **SE** `tenant_id`, `user_id` ou algum `role_id` não existir no banco **ENTÃO** 404 **SENÃO** segue | BD relacional | `ERR-CTX-008` | RF-08 | `40` §4.1 |
| RN-73 | Integridade | **SE** já existir `tenant_member` com par (`tenant_id`, `user_id`) em qualquer status **ENTÃO** 409 **SENÃO** segue | Restrição única | `ERR-CTX-005` | RF-08 | UT-3-05 |
| RN-74 | Integridade | **SE** algum papel de `role_ids` possuir `scope` sem correspondência ativa em `tenant_scope` do tenant **ENTÃO** 422 **SENÃO** segue | Escopo ativo | `ERR-CTX-006` | RF-08 | UT-3-05 |
| RN-75 | Integridade | **SE** membro possuir `is_owner = true` **ENTÃO** IF-12.2 NÃO DEVE alterar o seu status **SENÃO** altera via `ExecuteUpdateAsync` | `is_owner = false` | – | RF-08 | D-22, PRS-72 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-08 — Vincular membro a tenant
  Contexto:
    Dado que existe o tenant "01923456-789a-7bc8-9012-3456789abcde" com escopo "BUYER" ativo em "tenant_scope"
    E utilizador "01923456-789a-7bc8-9012-3456789abcd0" ativo em "user_account"
    E papel "01923456-789a-7bc8-9012-3456789abc01" com scope "BUYER" e papel "01923456-789a-7bc8-9012-3456789abc02" com scope "CARRIER"

  @CA-08.1 @RF-08 @RN-71 @RN-74 @MUST
  Cenário: Vinculação de membro com sucesso por TENANT_ADMIN
    Quando envia POST "/api/v1/tenants/01923456-789a-7bc8-9012-3456789abcde/members" autenticado como TENANT_ADMIN com "membro:vincular" e role_ids ["01923456-789a-7bc8-9012-3456789abc01"]
    Então o status é 201, status "ACTIVE", is_owner false, can_view_group true, registo criado em "member_role" e auditoria "MEMBER_ADDED"

  @CA-08.2 @RF-08 @RN-71 @MUST
  Cenário: Vinculação com sucesso por PLATFORM_ADMIN
    Quando envia POST "/api/v1/tenants/01923456-789a-7bc8-9012-3456789abcde/members" autenticado como PLATFORM_ADMIN com is_owner true, can_view_group false e role_ids ["01923456-789a-7bc8-9012-3456789abc01"]
    Então o status é 201, status "ACTIVE", is_owner true e can_view_group false

  @CA-08.3 @RF-08 @RN-73 @MUST @critico @borda
  Cenário: Conflito de unicidade ao vincular membro já existente
    Dado que o utilizador já está vinculado ao tenant
    Quando envia POST "/api/v1/tenants/01923456-789a-7bc8-9012-3456789abcde/members" com o mesmo utilizador
    Então o status é 409, code "ERR-CTX-005" e nenhum registo novo é criado

  @CA-08.4 @RF-08 @RN-74 @MUST @critico @borda
  Cenário: Rejeição ao atribuir papel com escopo inativo no tenant
    Quando envia POST "/api/v1/tenants/01923456-789a-7bc8-9012-3456789abcde/members" com role_ids ["01923456-789a-7bc8-9012-3456789abc02"]
    Então o status é 422, code "ERR-CTX-006" e nenhum registo é inserido

  @CA-08.5 @RF-08 @MUST @critico @borda
  Cenário: Rollback total na vinculação de membro após falha simulada
    Dado que a persistência falha durante inserção em "member_role"
    Quando envia POST "/api/v1/tenants/01923456-789a-7bc8-9012-3456789abcde/members" com dados válidos
    Então o status é 500, code "ERR-SYS-001" e nada persiste no banco

  @CA-08.6 @RF-08 @RN-71 @MUST @critico @borda
  Esquema do Cenário: Negação de autorização para chamadores sem permissão
    Quando envia POST "/api/v1/tenants/01923456-789a-7bc8-9012-3456789abcde/members" com token de <perfil>
    Então o status é 403, code "ERR-CTX-007" e nenhum registo é inserido
    Exemplos:
      | perfil                      |
      | "BUYER"                     |
      | "TENANT_ADMIN_OUTRO_TENANT" |

  @CA-08.7 @RF-08 @RN-72 @MUST @borda
  Esquema do Cenário: Rejeição ao referenciar recurso inexistente
    Quando envia POST com recurso inexistente <tipo_recurso>
    Então o status é 404, code "ERR-CTX-008" e nenhum registo é inserido
    Exemplos:
      | tipo_recurso |
      | "tenant"     |
      | "usuario"    |
      | "papel"      |

  @CA-08.8 @RF-08 @RN-70 @MUST @borda
  Esquema do Cenário: Rejeição de formato de entrada malformada
    Quando envia POST com payload malformado <caso>
    Então o status é 400, code "ERR-VAL-001" e nenhum registo é inserido
    Exemplos:
      | caso                                    |
      | "{\"role_ids\":[]}"                     |
      | "{\"role_ids\":[\"id1\",\"id1\"]}"      |
      | "{\"role_ids\":[\"11_ids_distintos\"]}" |
      | "{\"user_id\":\"nao-uuid\"}"            |
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-08 | MUST | RN-70 a RN-75 | EP-07, IF-12 | EX-08.1 a EX-08.8 | CA-08.1 a CA-08.8 | F-03 |

**Totais:** RF = 1 · RN = 6 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 4) · Σw = 18

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-150 | Validação de `role_ids` DEVE rejeitar pedidos com mais de 10 elementos | Evitar sobrecarga em `member_role` | PO, 2026-10-19 |
| PRS-151 | Operações via IF-12.2 NUNCA alteram membros com `is_owner = true` | Preservar a titularidade do tenant | PO, 2026-10-19 |

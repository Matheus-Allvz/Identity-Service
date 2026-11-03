# M-12 · Estado do tenant

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-12) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** doc + `10` + `30` + `40` (EP-08, EV-02, IFs) + `70` + `90`. Total: 56 KiB.

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| `PATCH /api/v1/tenants/{tenant_id}/status` (EP-08) | Criação de tenants ou dados cadastrais (M-04) |
| RF-09 (suspensão) e RF-12 (reativação) | Bloqueio de utilizadores e membros (M-10) |
| Orquestração transacional de status e sessões | Rotação e hash de refresh tokens (M-02) |
| Enfileiramento de `tenant_bloqueado` (EV-02) na outbox | Publicação física no Kafka (M-14) |
| Operações de `denylist:tenant:{id}` via `IDenylist` | Avaliação de acessos e permissões (M-08) |

**Escreve:** `tenant.status` (IF-10.1), `refresh_token` (IF-01.5), `auth_audit_log` (IF-03.1), `outbox_message` (IF-07.2), `denylist:tenant` (IF-04.3, IF-04.4) · **Lê:** `tenant_member` (ENT-05, C8) · **Usa:** M-04 (IF-10.1), M-02 (IF-01.5), M-00 (IF-03.1, IF-04.3, IF-04.4, IF-08.1), M-14 (IF-07.2)

## 2. Endpoints e eventos (detalhe)

### EP-08 `PATCH /api/v1/tenants/{tenant_id}/status`
| Campo | Valor |
|---|---|
| RF | RF-09 (`SUSPENDED`), RF-12 (`ACTIVE`) |
| Autenticação / Permissão | JWT obrigatório (`Bearer <jwt>`). Rejeições `[A]`. Autorização: `PLATFORM_ADMIN` OU C8 (`active_scope = TENANT_ADMIN`, `tenant_id` ∈ `allowed_tenants`, `tenant_member(tenant_id, sub).is_owner = true`) |
| Idempotência | Sim. Pedidos com status repetido retornam 200 OK |
| RN aplicáveis | RN-140, RN-141, RN-142, RN-143, RN-144, RN-145 |

**Request:** `tenant_id` (path, UUID formato D de 36 caracteres via `Guid.TryParseExact(input, "D")`, não restrito a v7); `status` (body, `"ACTIVE"` \| `"SUSPENDED"`); `reason` (body, 5 a 255 caracteres após `trim()`).
```json
{"status": "SUSPENDED", "reason": "Suspensão por inadimplência"}
```

**Ordem de avaliação:** 0. Rota/PATCH (404/405); 1. Limite corpo e Content-Type (413/415); 2. Chave `ISigningKey` (503); 3. Token válido, expiração, assinatura e sem `denylist:jti` (401 `ERR-AUTH-002`); 4. Sem `denylist:user` (403 `ERR-AUTH-004`); 5. Sem tenant em denylist (403 `ERR-CTX-002`); 6. Path UUID formato D (400 `ERR-VAL-001`); 7. Corpo JSON objeto sem NUL nem extras (400 `ERR-VAL-001`); 8. Campos `status` e `reason` válidos (400 `ERR-VAL-001`); 9. Autorização `PLATFORM_ADMIN` ou C8 (403 `ERR-CTX-007`); 10. Existência do tenant na BD via IF-10.1 (404 `ERR-CTX-008`); 11. Regras de negócio e transação (200 OK).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`, `ERR-CTX-002`, `ERR-CTX-007`; 404 `ERR-CTX-008`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Corpo de sucesso:** `{"tenant_id": "01900000-0000-7000-8000-000000000100", "status": "SUSPENDED", "updated_at": "2026-10-19T18:45:00Z"}`.

**Efeitos e transacionalidade:**
- `SUSPENDED`: (1) `SET denylist:tenant:{id} "1"` (TTL 900 s) no Redis antes do commit (IF-04.3); (2) Na transação BD: atualiza tenant (IF-10.1), revoga sessões (IF-01.5), grava auditoria `TENANT_STATUS_CHANGED` (IF-03.1) e enfileira `tenant_bloqueado` EV-02 (IF-07.2); (3) Commit.
- `ACTIVE`: (1) Na transação BD: atualiza tenant (IF-10.1) e grava auditoria `TENANT_STATUS_CHANGED` (IF-03.1); (2) Commit; (3) Se `Previous = SUSPENDED`, executa `DEL denylist:tenant:{id}` no Redis após commit (IF-04.4).
- Transação atómica: falha na BD desfaz tudo (`ROLLBACK`).

### EV-02 `tenant_bloqueado`
Enfileirado por M-12 via IF-07.2 na outbox exclusivamente quando `status = SUSPENDED`. Payload: `event_id`, `tenant_id`, `group_id`, `cnpj`, `status` (`SUSPENDED`), `reason`, `blocked_by_user_id` (`sub`), `blocked_at` (`UpdatedAt` de IF-10.1).

## 3. Requisitos funcionais

### RF-09 — Suspender tenant
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / `PLATFORM_ADMIN` ou C8 via EP-08 |
| Pré-condições / RN | Token válido; tenant existente / RN-140, RN-141, RN-142, RN-144, RN-145 |

**Fluxo principal:** 1. Envia `PATCH /api/v1/tenants/{tenant_id}/status` (`status = "SUSPENDED"`, `reason`). 2. Valida camadas 0 a 4. 3. Grava `denylist:tenant:{tenant_id} = "1"` no Redis via IF-04.3 (TTL 900 s). 4. Na transação BD: atualiza `tenant.status = SUSPENDED` via IF-10.1, revoga sessões via IF-01.5, grava auditoria via IF-03.1 e enfileira EV-02 via IF-07.2. 5. Comita transação BD e devolve 200 OK.
**Fluxos de exceção:** EX-09.1 (formato inválido → 400 `ERR-VAL-001`); EX-09.2 (não autorizado → 403 `ERR-CTX-007`); EX-09.3 (tenant inexistente → 404 `ERR-CTX-008`); EX-09.4 (falha BD/Redis → 503 `ERR-SYS-002`).
**Concorrência e idempotência:** Pedidos repetidos com `SUSPENDED` retornam 200 OK e repetem revogação, denylist, auditoria e novo EV-02 (CA-09.2). Falha Redis antes da BD aborta (503). Falha no commit BD reverte e chave Redis expira por TTL 900 s.

### RF-12 — Reativar tenant
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / `PLATFORM_ADMIN` (ou C8 se tenant fora do token) via EP-08 |
| Pré-condições / RN | Token válido; tenant existente / RN-140, RN-141, RN-143, RN-144, RN-145 |

**Fluxo principal:** 1. Envia `PATCH /api/v1/tenants/{tenant_id}/status` (`status = "ACTIVE"`, `reason`). 2. Valida camadas 0 a 4. 3. Na transação BD: atualiza `tenant.status = ACTIVE` via IF-10.1 e grava auditoria via IF-03.1. 4. Comita transação BD. 5. Se `Previous = SUSPENDED`, remove `denylist:tenant:{tenant_id}` no Redis via IF-04.4. 6. Devolve 200 OK.
**Fluxos de exceção:** EX-12.1 (formato inválido → 400 `ERR-VAL-001`); EX-12.2 (não autorizado → 403 `ERR-CTX-007`); EX-12.3 (tenant inexistente → 404 `ERR-CTX-008`); EX-12.4 (falha BD → 503 `ERR-SYS-002`); EX-12.5 (falha Redis pós-commit → 200 OK retornado, chave expira por TTL).
**Concorrência e idempotência:** Pedido repetido com `ACTIVE` retorna 200 OK, grava auditoria, sem outbox nem `DEL` no Redis (CA-12.2). Pedidos concorrentes de reativação são idempotentes.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-140 | Validação | **SE** `tenant_id` não for UUID formato D (36 caracteres) **ENTÃO** rejeitar com 400 `ERR-VAL-001` **SENÃO** prosseguir | UUID formato D | `ERR-VAL-001` | RF-09, RF-12 | 40 §2, PRS-302 |
| RN-141 | Validação | **SE** `reason` tiver menos de 5 ou mais de 255 caracteres após `trim()` **ENTÃO** rejeitar com 400 `ERR-VAL-001` **SENÃO** prosseguir | 5 a 255 | `ERR-VAL-001` | RF-09, RF-12 | PRS-37 |
| RN-142 | Integridade | **SE** solicitar `status = SUSPENDED` **ENTÃO** DEVE gravar `denylist:tenant` no Redis antes do commit E na transação BD atualizar tenant, revoga sessões, grava auditoria e enfileira EV-02 **SENÃO** falha e reverte | TTL 900 s | `ERR-SYS-002` | RF-09 | PRS-12, PRS-15 |
| RN-143 | Integridade | **SE** solicitar `status = ACTIVE` **ENTÃO** DEVE atualizar tenant e grava auditoria na transação BD, E se `Previous = SUSPENDED` DEVE executar `DEL denylist:tenant` no Redis após commit **SENÃO** não executa `DEL` | – | – | RF-12 | PRS-15 |
| RN-144 | Autorização | **SE** chamador não possuir `platform_role = PLATFORM_ADMIN` E não cumprir condição C8 **ENTÃO** rejeitar com 403 `ERR-CTX-007` **SENÃO** prosseguir | Condição C8 | `ERR-CTX-007` | RF-09, RF-12 | `40` §5 |
| RN-145 | Integridade | **SE** solicitar status já ativo no tenant **ENTÃO** DEVE responder 200 OK E se `SUSPENDED` repetir revogação, denylist, auditoria e novo EV-02, E se `ACTIVE` gravar só auditoria **SENÃO** aplicar fluxo regular | – | – | RF-09, RF-12 | UT-3-12 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-09 e RF-12 — Alteração do estado do tenant (EP-08)

  Contexto:
    Dado que a base de dados PostgreSQL e o Redis estão operacionais

  @CA-09.1 @RF-09 @RN-140 @RN-141 @RN-142 @RN-144 @MUST @critico @borda
  Cenário: Suspensão de tenant com sucesso por PLATFORM_ADMIN
    Dado tenant "01900000-0000-7000-8000-000000000100" ACTIVE e sessões ativas vinculadas
    E utilizador autenticado com role "PLATFORM_ADMIN"
    Quando enviado PATCH "/api/v1/tenants/01900000-0000-7000-8000-000000000100/status" com {"status": "SUSPENDED", "reason": "Fraude cadastral"}
    Então status é 200 e corpo contém "status" "SUSPENDED"
    E tenant tem status "SUSPENDED" e chave denylist existe no Redis
    E sessões do tenant têm status "REVOKED" e mensagem "tenant_bloqueado" na outbox

  @CA-09.2 @RF-09 @RN-142 @RN-145 @MUST @borda
  Cenário: Suspensão repetida de tenant já suspenso é idempotente e repete efeitos
    Dado tenant "01900000-0000-7000-8000-000000000100" já "SUSPENDED"
    E utilizador autenticado com role "PLATFORM_ADMIN"
    Quando enviado PATCH "/api/v1/tenants/01900000-0000-7000-8000-000000000100/status" com {"status": "SUSPENDED", "reason": "Reiteração cautelar"}
    Então status é 200 e chave denylist permanece no Redis
    E novo evento "tenant_bloqueado" na outbox e auditoria TENANT_STATUS_CHANGED gravada

  @CA-09.3 @RF-09 @RN-141 @RN-144 @MUST @critico @borda
  Esquema do Cenário: Validação de bordas do motivo e autorização na suspensão
    Dado tenant "01900000-0000-7000-8000-000000000100" ACTIVE
    E utilizador possui role <role_plataforma> e escopo <escopo>
    Quando enviado PATCH "/api/v1/tenants/01900000-0000-7000-8000-000000000100/status" com {"status": "SUSPENDED", "reason": <motivo>}
    Então status é <status_http> e erro é <codigo_erro>
    E tenant permanece "ACTIVE"
    Exemplos:
      | role_plataforma  | escopo    | motivo               | status_http | codigo_erro   |
      | "PLATFORM_ADMIN" | "NONE"    | "1234"               | 400         | "ERR-VAL-001" |
      | "PLATFORM_ADMIN" | "NONE"    | "   ab   "           | 400         | "ERR-VAL-001" |
      | "NONE"           | "BUYER"   | "Suspensao indevida" | 403         | "ERR-CTX-007" |
      | "SUPPORT_AGENT"  | "NONE"    | "Tentativa suporte"  | 403         | "ERR-CTX-007" |

  @CA-09.4 @RF-09 @RN-142 @MUST @borda
  Cenário: Falha de dependência do Redis na suspensão aborta operação
    Dado serviço Redis inacessível e utilizador "PLATFORM_ADMIN"
    Quando enviado PATCH "/api/v1/tenants/01900000-0000-7000-8000-000000000100/status" com {"status": "SUSPENDED", "reason": "Bloqueio falha redis"}
    Então status é 503 e código "ERR-SYS-002"
    E status do tenant na base de dados permanece inalterado

  @CA-12.1 @RF-12 @RN-140 @RN-141 @RN-143 @RN-144 @MUST @critico @borda
  Cenário: Reativação de tenant suspenso com remoção da denylist
    Dado tenant "01900000-0000-7000-8000-000000000100" "SUSPENDED" e chave denylist no Redis
    E utilizador autenticado com role "PLATFORM_ADMIN"
    Quando enviado PATCH "/api/v1/tenants/01900000-0000-7000-8000-000000000100/status" com {"status": "ACTIVE", "reason": "Regularização concluída"}
    Então status é 200 e tenant tem status "ACTIVE"
    E chave denylist é removida do Redis, sem mensagem na outbox e com auditoria gravada

  @CA-12.2 @RF-12 @RN-143 @RN-145 @MUST @borda
  Cenário: Reativação repetida de tenant já ativo não remove no Redis nem outbox
    Dado tenant "01900000-0000-7000-8000-000000000100" já "ACTIVE" e utilizador "PLATFORM_ADMIN"
    Quando enviado PATCH "/api/v1/tenants/01900000-0000-7000-8000-000000000100/status" com {"status": "ACTIVE", "reason": "Confirmação ativação"}
    Então status é 200, sem remoção no Redis e sem mensagem na outbox

  @CA-12.3 @RF-12 @RN-140 @RN-144 @MUST @borda
  Esquema do Cenário: Tentativa com tenant inexistente ou UUID inválido
    Dado utilizador autenticado com role "PLATFORM_ADMIN"
    Quando enviado PATCH "/api/v1/tenants/<tenant_param>/status" com {"status": "ACTIVE", "reason": "Tentativa parametro"}
    Então status é <status_http> e código <codigo_erro>
    Exemplos:
      | tenant_param | status_http | codigo_erro |
      | "invalido-uuid" | 400 | "ERR-VAL-001" |
      | "01900000-0000-7000-8000-999999999999" | 404 | "ERR-CTX-008" |
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-09 | MUST | RN-140 a RN-142, RN-144, RN-145 | EP-08, EV-02 | EX-09.1 a EX-09.4 | CA-09.1 a CA-09.4 | F-03 |
| RF-12 | MUST | RN-140, RN-141, RN-143 a RN-145 | EP-08 | EX-12.1 a EX-12.5 | CA-12.1 a CA-12.3 | F-03 |

**Totais:** RF = 2 · RN = 6 · EP = 1 · EV = 1 · CA = 7 (`@critico` = 3) · Σw = 21

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-300 | Chamadas a EP-08 com status idêntico ao atual retornam 200 OK repetindo efeitos definidos para cada estado | Alinha com recomendação do PO/ORQ para repetição de estado, mantendo idempotência | PO, 2026-11-02 (D-23) |
| PRS-301 | Reativação de tenant suspenso é restrita a `PLATFORM_ADMIN` pois `TENANT_ADMIN` tem o tenant na denylist | Consequência direta de RG-08 e da matriz de autorização | PO, 2026-11-02 (D-23) |
| PRS-302 | Parâmetro {tenant_id} em EP-08 validado como UUID formato D (36 caracteres via Guid.TryParseExact) sem restrição a v7, retornando 404 na camada 5 se inexistente | Alinhamento com 40-contratos.md:58 | PO, 2026-11-02 (D-23) |

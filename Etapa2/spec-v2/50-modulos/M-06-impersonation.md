# M-06 · Impersonation

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-06) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** doc + `10` + `30` (ENT-04, ENUM-03/04/07, AUD.M06, §6) + `40` (§1-§3, EP-05, IF-03.1, IF-06.1, IF-08.1, IF-09.1-4/8) + `70` (§1) + `90`. Total: 59 KiB.

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| `POST /api/v1/auth/impersonate` (EP-05) | Autenticação inicial (M-01) |
| RF-06: Iniciar impersonation RFC 8693 | Refresh tokens (M-02) e troca (M-03) |
| Validação de operador e alvo (camadas 4/6) | Chaves criptográficas (M-07) |
| Emissão de JWT com `act` (IF-06.1) e auditoria (IF-03.1) | Regras de acessos (M-08) |

**Escreve:** `auth_audit_log` (IF-03.1) · **Lê:** `user_account` (ENT-04, `BypassTenantFilter = true`) · **Usa:** M-00 (IF-03.1, IF-08.1), M-07 (IF-06.1), M-08 (IF-09.1-4, IF-09.8)

## 2. Endpoints e eventos (detalhe)

### EP-05 `POST /api/v1/auth/impersonate`
| Campo | Valor |
|---|---|
| RF / RN | RF-06 / RN-13, RN-14, RN-80 a RN-87 |
| Permissão | JWT obrigatório (`Bearer <jwt>`). Chamador com `platform_role` ∈ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} sem `act` |
| Idempotência | Não. Cada sucesso gera novo `jti` (UUID v7) e registo de auditoria |

**Request:** `target_user_id` (UUID D), `target_tenant_id` (UUID D), `target_scope` (enum `OperationalScope`), `ticket_id` (regex `^[A-Z]{2,10}-[0-9]{1,10}$`, PRS-40).
```json
{"target_user_id":"01900000-0000-7000-8000-000000000002","target_tenant_id":"01900000-0000-7000-8000-000000000100","target_scope":"BUYER","ticket_id":"CHAMADO-123"}
```

**Ordem de avaliação:** 0. Rota/POST (404/405). 1. Limite corpo ≤ 65 536 bytes e Content-Type (413/415). 2. Chave `ISigningKey` (503). 3. Token e sem `denylist:jti` (401 `ERR-AUTH-002`). 4. Sem `denylist:user` para `sub` do chamador (403 `ERR-AUTH-004`). 5. Sem tenant em denylist no token (403 `ERR-CTX-002`). 6. JSON objeto, campos, UUID D e enum (400 `ERR-VAL-001`). 7. Regex de `ticket_id` (422 `ERR-VAL-004`). 8. Operador com papel de plataforma e sem `act` (RN-13, 403 `ERR-AUTH-010`). 9. Tenant alvo existe via IF-09.8 (404 `ERR-CTX-008`). 10. Alvo existe em `user_account` (404 `ERR-CTX-008`). 11. Alvo com `platform_role = NONE` (RN-80) e alvo ≠ chamador (RN-81) → 403 `ERR-AUTH-010`. 12. Alvo sem `LOCKED` ativo (RN-82), sem `SUSPENDED` e sem `denylist:user` tRef=0 (RN-83) → 403 `ERR-AUTH-004`. 13. Tenant alvo e grupo ativos via IF-09.2 (RN-84) → 403 `ERR-CTX-002`. 14. Vínculo `ACTIVE` do alvo no tenant via IF-09.3 (RN-85) → 403 `ERR-CTX-001`. 15. Escopo ativo e roles não vazias via IF-09.4 (RN-86) → 403 `ERR-CTX-003`. 16. Emissão JWT (RN-14, IF-06.1) e auditoria BD (RN-87, IF-03.1) → 200 OK.

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`/`010`, `ERR-CTX-001..003`; 404 `ERR-CTX-008`, `ERR-REQ-001`; 405 `ERR-REQ-002`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Corpo de sucesso:**
```json
{"access_token":"<jwt>","token_type":"Bearer","expires_in":900,"impersonation_context":{"sub":"01900000-0000-7000-8000-000000000002","group_id":"01900000-0000-7000-8000-000000000050","tenant_id":"01900000-0000-7000-8000-000000000100","allowed_tenants":["01900000-0000-7000-8000-000000000100"],"view_mode":"SINGLE","active_scope":"BUYER","roles":["BUYER_OPERATOR"],"permissions":["demanda:criar","demanda:listar","leilao:participar","pedido:visualizar"],"act":{"sub":"01900000-0000-7000-8000-000000000001","ticket_id":"CHAMADO-123"}}}
```

**Efeitos e transacionalidade:** Emite `access_token` via IF-06.1 (`FamilyId = null`), insere auditoria `IMPERSONATION_STARTED` via IF-03.1 e confirma BD antes do 200. Sem `refresh_token`. Headers: `Cache-Control: no-store`, `Pragma: no-cache`.

## 3. Requisitos funcionais

### RF-06 — Iniciar impersonation RFC 8693
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Operador (`SUPPORT_AGENT` ou `PLATFORM_ADMIN`) via `POST /api/v1/auth/impersonate` |
| Pré-condições | Operador autenticado sem `act`; utilizador alvo e tenant alvo existentes |
| RN | RN-13, RN-14, RN-80 a RN-87 |

**Fluxo principal:** 1. Operador envia requisição. 2. Valida transporte e operador sem `act` (RN-13). 3. Consulta tenant via IF-09.8 e alvo em `user_account` (404 `ERR-CTX-008` se ausentes). 4. Avalia elegibilidade do alvo (RN-80..83), tenant/grupo (RN-84), vínculo (RN-85) e escopo (RN-86). 5. Emite JWT com 900 s por IF-06.1 (RN-14). 6. Insere auditoria por IF-03.1 (RN-87), confirma BD com `SaveChangesAsync` e retorna 200 com `impersonation_context` e headers sem cache.

**Fluxos de exceção:** EX-06.1 (formato → 400); EX-06.2 (`ticket_id` → 422); EX-06.3 (chamador inválido → 403 `ERR-AUTH-010`); EX-06.4 (tenant inexiste → 404 `ERR-CTX-008`); EX-06.5 (alvo inexiste → 404 `ERR-CTX-008`); EX-06.6 (alvo inelegível → 403 `ERR-AUTH-010`); EX-06.7 (alvo indisponível → 403 `ERR-AUTH-004`); EX-06.8 (tenant suspenso → 403 `ERR-CTX-002`); EX-06.9 (vínculo inativo → 403 `ERR-CTX-001`); EX-06.10 (escopo inativo → 403 `ERR-CTX-003`); EX-06.11 (dependência → 503 `ERR-SYS-002`).

**Concorrência e idempotência:** Não idempotente. Chamadas simultâneas geram tokens com `jti` distinto e novas auditorias (CA-06.1). Falha antes do commit aborta entrega do token.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-13 | Autorização | **SE** token contiver `act` OU `platform_role` ∉ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} **ENTÃO** 403 `ERR-AUTH-010` **SENÃO** autoriza | Camada 4 | `ERR-AUTH-010` | RF-06 | ADR-01, ADR-08 |
| RN-14 | Cálculo | **SE** aprovado **ENTÃO** emitir JWT com dados do alvo, `SINGLE`, `family_id = null`, `act = {sub, ticket_id}` e validade 900 s **SENÃO** não emitir | 900 s | – | RF-06 | ADR-08, PRS-41 |
| RN-80 | Autorização | **SE** alvo possuir `platform_role ≠ NONE` **ENTÃO** 403 `ERR-AUTH-010` **SENÃO** prosseguir | – | `ERR-AUTH-010` | RF-06 | ADR-01, PRS-08 |
| RN-81 | Autorização | **SE** `target_user_id` for igual a `chamador.sub` **ENTÃO** 403 `ERR-AUTH-010` **SENÃO** prosseguir | Auto-delegação | `ERR-AUTH-010` | RF-06 | PRS-08 |
| RN-82 | Temporal | **SE** alvo tiver `status = LOCKED` E `locked_until > agora` **ENTÃO** 403 `ERR-AUTH-004` **SENÃO** prosseguir | – | `ERR-AUTH-004` | RF-06 | UT-3-06 |
| RN-83 | Autorização | **SE** alvo tiver `status = SUSPENDED` OU `denylist:user:{id}` ativa **ENTÃO** 403 `ERR-AUTH-004` **SENÃO** prosseguir | tRef = 0 | `ERR-AUTH-004` | RF-06 | RG-05, PRS-46 |
| RN-84 | Autorização | **SE** tenant ou grupo tiver `status = SUSPENDED` OU em denylist **ENTÃO** 403 `ERR-CTX-002` **SENÃO** prosseguir | – | `ERR-CTX-002` | RF-06 | RG-04 |
| RN-85 | Autorização | **SE** alvo não detiver vínculo `status = ACTIVE` em `tenant_member` **ENTÃO** 403 `ERR-CTX-001` **SENÃO** prosseguir | – | `ERR-CTX-001` | RF-06 | RG-03 |
| RN-86 | Autorização | **SE** `target_scope` inativo no tenant OU roles vazio **ENTÃO** 403 `ERR-CTX-003` **SENÃO** prosseguir | – | `ERR-CTX-003` | RF-06 | RG-01, RG-02 |
| RN-87 | Integridade | **SE** emitido **ENTÃO** DEVE gravar `IMPERSONATION_STARTED` em `auth_audit_log` e confirmar antes do 200 **SENÃO** não persistir | IF-03.1 | `ERR-SYS-002` | RF-06 | AUD.M06, UT-3-06 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-06 — Iniciar impersonation RFC 8693

  Contexto:
    Dado que a base de dados PostgreSQL e o Redis estão operacionais

  @CA-06.1 @RF-06 @RN-13 @RN-14 @RN-87 @MUST @borda
  Cenário: Emissão bem-sucedida de token de impersonation com contexto completo e auditoria
    Dado operador "01900000-0000-7000-8000-000000000001" com role "SUPPORT_AGENT" sem act
    E tenant "01900000-0000-7000-8000-000000000100" ativo em "BUYER"
    E alvo "01900000-0000-7000-8000-000000000002" ativo com platform_role "NONE" e papel "BUYER_OPERATOR"
    Quando é enviado POST "/api/v1/auth/impersonate" com {"target_user_id":"01900000-0000-7000-8000-000000000002","target_tenant_id":"01900000-0000-7000-8000-000000000100","target_scope":"BUYER","ticket_id":"CHAMADO-123"}
    Então o status da resposta é 200, "token_type" é "Bearer", "expires_in" é 900
    E o objeto "impersonation_context" contém sub do alvo, view_mode "SINGLE", active_scope "BUYER" e act com ticket_id "CHAMADO-123"
    E a resposta não contém o campo "refresh_token"
    E existe 1 registo na auth_audit_log com event_type "IMPERSONATION_STARTED" e ticket_id "CHAMADO-123"

  @CA-06.2 @RF-06 @RN-13 @RN-80 @RN-81 @MUST @critico @borda
  Esquema do Cenário: Rejeição de impersonation quando chamador ou alvo violam regras de privilégio
    Quando operador com role "<role_op>" e act "<act_op>" envia POST "/api/v1/auth/impersonate" para alvo com role "<role_alvo>" e mesmo_id "<auto_del>"
    Então o status da resposta é 403 e código "ERR-AUTH-010" sem registo em auth_audit_log

    Exemplos:
      | role_op | act_op | role_alvo | auto_del |
      | SUPPORT_AGENT | sim | NONE | nao |
      | NONE | nao | NONE | nao |
      | SUPPORT_AGENT | nao | SUPPORT_AGENT | nao |
      | SUPPORT_AGENT | nao | PLATFORM_ADMIN | nao |
      | SUPPORT_AGENT | nao | NONE | sim |

  @CA-06.3 @RF-06 @RN-82 @RN-83 @MUST @critico @borda
  Esquema do Cenário: Rejeição de impersonation quando utilizador alvo está indisponível ou bloqueado
    Dado utilizador alvo com status "<status_alvo>", locked_until "<bloqueio>" e denylist Redis "<na_denylist>"
    Quando é enviado POST "/api/v1/auth/impersonate" para o utilizador alvo
    Então o status da resposta é 403 e código "ERR-AUTH-004" sem registo em auth_audit_log

    Exemplos:
      | status_alvo | bloqueio | na_denylist |
      | SUSPENDED | null | nao |
      | ACTIVE | null | sim |
      | LOCKED | futuro | nao |

  @CA-06.4 @RF-06 @RN-82 @MUST @borda
  Cenário: Utilizador alvo com bloqueio LOCKED expirado tem impersonation aprovada
    Dado utilizador alvo "01900000-0000-7000-8000-000000000002" com status "LOCKED" e locked_until no passado
    E chave de denylist do utilizador ausente no Redis
    Quando é enviado POST "/api/v1/auth/impersonate" com {"target_user_id":"01900000-0000-7000-8000-000000000002","target_tenant_id":"01900000-0000-7000-8000-000000000100","target_scope":"BUYER","ticket_id":"CHAMADO-123"}
    Então o status da resposta é 200 e sub em impersonation_context é "01900000-0000-7000-8000-000000000002"
    E existe registo na auth_audit_log com event_type "IMPERSONATION_STARTED"

  @CA-06.5 @RF-06 @RN-84 @RN-85 @RN-86 @MUST @critico @borda
  Esquema do Cenário: Rejeição de impersonation quando tenant alvo ou vínculo não cumprem requisitos
    Dado tenant alvo com status "<status_tenant>", denylist Redis "<tenant_denylist>", vínculo "<status_vinculo>" e escopo ativo "<escopo_ativo>"
    Quando é enviado POST "/api/v1/auth/impersonate" com tenant alvo
    Então o status da resposta é 403 e código "<codigo_erro>" sem registo em auth_audit_log

    Exemplos:
      | status_tenant | tenant_denylist | status_vinculo | escopo_ativo | codigo_erro |
      | SUSPENDED | nao | ACTIVE | sim | ERR-CTX-002 |
      | ACTIVE | sim | ACTIVE | sim | ERR-CTX-002 |
      | ACTIVE | nao | SUSPENDED | sim | ERR-CTX-001 |
      | ACTIVE | nao | inativo | sim | ERR-CTX-001 |
      | ACTIVE | nao | ACTIVE | nao | ERR-CTX-003 |

  @CA-06.6 @RF-06 @RN-14 @MUST @borda
  Esquema do Cenário: Validação de borda no formato do chamado técnico ticket_id
    Quando é enviado POST "/api/v1/auth/impersonate" com ticket_id "<ticket_teste>"
    Então o status da resposta é <status_http> e código "<codigo_erro>"

    Exemplos:
      | ticket_teste | status_http | codigo_erro |
      | AB-1 | 200 | – |
      | ABCDEFGHIJ-1234567890 | 200 | – |
      | A-1 | 422 | ERR-VAL-004 |
      | AB- | 422 | ERR-VAL-004 |
      | AB-12345678901 | 422 | ERR-VAL-004 |
      | ab-123 | 422 | ERR-VAL-004 |

  @CA-06.7 @RF-06 @RN-13 @MUST @borda
  Esquema do Cenário: Validação de existência de utilizador e tenant alvo
    Quando é enviado POST "/api/v1/auth/impersonate" com target_user_id "<alvo_id>" e target_tenant_id "<tenant_id>"
    Então o status da resposta é <status_http> e código "<codigo_erro>"

    Exemplos:
      | alvo_id | tenant_id | status_http | codigo_erro |
      | 01900000-0000-7000-8000-999999999999 | 01900000-0000-7000-8000-000000000100 | 404 | ERR-CTX-008 |
      | 01900000-0000-7000-8000-000000000002 | 01900000-0000-7000-8000-999999999999 | 404 | ERR-CTX-008 |
      | id-invalido | 01900000-0000-7000-8000-000000000100 | 400 | ERR-VAL-001 |
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-06 | MUST | RN-13, RN-14, RN-80 a RN-87 | EP-05 | EX-06.1 a EX-06.11 | CA-06.1 a CA-06.7 | F-01 |

**Totais:** RF = 1 · RN = 10 · EP = 1 · EV = 0 · CA = 7 (`@critico` = 3) · Σw = 21

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-160 | EP-05 bem-sucedido gera novo `jti` e nova auditoria | Rastreabilidade estrita de sessões | PO, 2026-11-02 (D-23) |
| PRS-161 | Bloqueio `LOCKED` no alvo com `locked_until ≤ agora` expira | Expiração automática de bloqueio | PO, 2026-11-02 (D-23) |
| PRS-162 | Leitura de `user_account` usa `BypassTenantFilter = true` | Entidade global sem isolamento | PO, 2026-11-02 (D-23) |

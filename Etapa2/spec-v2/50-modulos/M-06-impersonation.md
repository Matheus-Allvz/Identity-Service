# M-06 · Impersonation

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-06) · **Estado:** Rascunho (ciclo 5, UT-3-06) · **Atualizado em:** 2026-10-19
> **Pacote:** este ficheiro + `10` + `30` (ENT-04, ENUM-03/04/07, AUD.M06) + `40` (§1-§3, EP-05, IF-03.1, IF-06.1, IF-08.1, IF-09.1-4/8, tipos) + `70` (§1) + `90` (UT-3-06, PRS-08/40/41). Total: 42.8 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| `POST /api/v1/auth/impersonate` (EP-05) | Autenticação inicial (M-01) |
| RF-06: Iniciar impersonation RFC 8693 | Refresh tokens (M-02) |
| Validação de operador e alvo (camadas 4/6) | Troca de contexto (M-03) |
| Emissão de JWT com `act` via IF-06.1 | Chaves criptográficas (M-07) |
| Auditoria `IMPERSONATION_STARTED` via IF-03.1 | Regras de acessos (M-08) |

**Escreve:** `auth_audit_log` (IF-03.1) · **Lê:** `user_account` (ENT-04 via DbContext com `BypassTenantFilter = true`) · **Usa:** M-00 (IF-03.1, IF-08.1), M-07 (IF-06.1), M-08 (IF-09.1-4, IF-09.8)

## 2. Endpoints e eventos (detalhe)

### EP-05 `POST /api/v1/auth/impersonate`
| Campo | Valor |
|---|---|
| RF | RF-06 |
| Autenticação / Permissão | JWT obrigatório (`Bearer <jwt>`). Rejeições `[A]`. Chamador DEVE ter `platform_role` ∈ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} e token sem `act` |
| Idempotência | Não. Cada sucesso gera novo `jti` (UUID v7) e novo registo de auditoria |
| RN aplicáveis | RN-80 a RN-89 |

**Request:** `target_user_id` (body, UUID formato D); `target_tenant_id` (body, UUID formato D); `target_scope` (body, enum `OperationalScope`); `ticket_id` (body, regex `^[A-Z]{2,10}-[0-9]{1,10}$`, PRS-40).
```json
{"target_user_id":"01900000-0000-7000-8000-000000000002","target_tenant_id":"01900000-0000-7000-8000-000000000100","target_scope":"BUYER","ticket_id":"CHAMADO-123"}
```

**Ordem de avaliação:** 0. Rota/POST (404/405); 1. Limite corpo ≤ 65 536 bytes e Content-Type (413/415); 2. Chave `ISigningKey` (503); 3. Token válido, expiração e sem `denylist:jti` (401 `ERR-AUTH-002`); 4. Sem `denylist:user` para `sub` do chamador (403 `ERR-AUTH-004`); 5. Sem tenant em denylist no token (403 `ERR-CTX-002`); 6. JSON objeto, campos obrigatórios, UUID formato D e enum (400 `ERR-VAL-001`); 7. Regex de `ticket_id` (422 `ERR-VAL-004`); 8. Operador `platform_role` ∈ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} e sem `act` (403 `ERR-AUTH-010`); 9. Tenant alvo existe via IF-09.8 (404 `ERR-CTX-008`); 10. Alvo existe em `user_account` (404 `ERR-CTX-008`); 11. Camada 6 #1: alvo com `platform_role = NONE` e alvo ≠ chamador (403 `ERR-AUTH-010`); 12. Camada 6 #2: alvo sem `SUSPENDED`, sem `denylist:user` (tRef=0) e sem `LOCKED` ativo (403 `ERR-AUTH-004`); 13. Camada 6 #3: tenant alvo e grupo ativos via IF-09.2 (403 `ERR-CTX-002`); 14. Camada 6 #4: vínculo `ACTIVE` do alvo no tenant via IF-09.3 (403 `ERR-CTX-001`); 15. Camada 6 #5: escopo ativo e roles não vazias via IF-09.4 (403 `ERR-CTX-003`); 16. Emissão JWT (IF-06.1) e auditoria gravada na BD (IF-03.1) (200 OK).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`, `ERR-AUTH-010`, `ERR-CTX-001..003`; 404 `ERR-CTX-008`, `ERR-REQ-001`; 405 `ERR-REQ-002`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Corpo de sucesso:** `access_token` (JWT), `token_type` (`"Bearer"`), `expires_in` (`900`), `impersonation_context` (`sub`, `group_id`, `tenant_id`, `allowed_tenants: [tenant_id]`, `view_mode: "SINGLE"`, `active_scope`, `roles`, `permissions`, `act: {sub, ticket_id}`). Sem `refresh_token`.
```json
{"access_token":"eyJhbGciOiJSUzI1NiIs...","token_type":"Bearer","expires_in":900,"impersonation_context":{"sub":"01900000-0000-7000-8000-000000000002","group_id":"01900000-0000-7000-8000-000000000050","tenant_id":"01900000-0000-7000-8000-000000000100","allowed_tenants":["01900000-0000-7000-8000-000000000100"],"view_mode":"SINGLE","active_scope":"BUYER","roles":["BUYER_OPERATOR"],"permissions":["pedidos:criar","pedidos:ler"],"act":{"sub":"01900000-0000-7000-8000-000000000001","ticket_id":"CHAMADO-123"}}}
```

**Efeitos e transacionalidade:** (1) Emite `access_token` via IF-06.1 (`FamilyId = null`); (2) Insere auditoria `IMPERSONATION_STARTED` via IF-03.1; (3) Confirma BD (`SaveChangesAsync`) antes do 200. Falha na BD reverte (503). Headers `Cache-Control: no-store` e `Pragma: no-cache`.

## 3. Requisitos funcionais

### RF-06 — Iniciar impersonation RFC 8693
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Operador (`SUPPORT_AGENT` ou `PLATFORM_ADMIN`) via `POST /api/v1/auth/impersonate` |
| Pré-condições | Operador autenticado sem `act`; utilizador alvo e tenant alvo existentes |
| RN | RN-80, RN-81, RN-82, RN-83, RN-84, RN-85, RN-86, RN-87, RN-88, RN-89 |

**Fluxo principal:** 1. Operador envia requisição com dados do alvo e chamado. 2. Valida camadas 0 a 3. 3. Valida camada 4 (operador com papel de plataforma sem `act`). 4. Consulta tenant alvo via IF-09.8 (se `null` → 404 `ERR-CTX-008`). 5. Consulta alvo em `user_account` com `BypassTenantFilter = true` (se `null` → 404 `ERR-CTX-008`). 6. Avalia camada 6: alvo elegível (RN-81, RN-82); alvo ativo (RN-83, RN-84); tenant e grupo ativos (RN-85); vínculo ativo (RN-86); escopo ativo e roles (RN-87). 7. Emite JWT com 900 s por IF-06.1 (RN-88). 8. Insere auditoria por IF-03.1 (RN-89). 9. Executa `SaveChangesAsync`. 10. Devolve 200 OK com token e `impersonation_context`.
**Fluxos de exceção:** EX-06.1 (formato → 400 `ERR-VAL-001`); EX-06.2 (`ticket_id` → 422 `ERR-VAL-004`); EX-06.3 (chamador inválido → 403 `ERR-AUTH-010`); EX-06.4 (tenant não existe → 404 `ERR-CTX-008`); EX-06.5 (alvo não existe → 404 `ERR-CTX-008`); EX-06.6 (alvo não elegível → 403 `ERR-AUTH-010`); EX-06.7 (alvo indisponível → 403 `ERR-AUTH-004`); EX-06.8 (tenant suspenso → 403 `ERR-CTX-002`); EX-06.9 (vínculo inativo → 403 `ERR-CTX-001`); EX-06.10 (escopo inativo → 403 `ERR-CTX-003`); EX-06.11 (dependência → 503 `ERR-SYS-002`).
**Concorrência e idempotência:** Não idempotente. Chamadas simultâneas geram tokens com `jti` distinto e novas auditorias (CA-06.1). Falha antes do commit aborta entrega do token.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-80 | Autorização | **SE** token contiver `act` OU `platform_role` ∉ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} **ENTÃO** 403 `ERR-AUTH-010` **SENÃO** autorizar camada 5 | Camada 4 | `ERR-AUTH-010` | RF-06 | ADR-01, ADR-08 |
| RN-81 | Autorização | **SE** alvo possuir `platform_role ≠ NONE` **ENTÃO** 403 `ERR-AUTH-010` **SENÃO** prosseguir | Camada 6 #1 | `ERR-AUTH-010` | RF-06 | ADR-01, PRS-08 |
| RN-82 | Autorização | **SE** `target_user_id` for igual a `chamador.sub` **ENTÃO** 403 `ERR-AUTH-010` **SENÃO** prosseguir | Auto-delegação | `ERR-AUTH-010` | RF-06 | PRS-08 |
| RN-83 | Temporal | **SE** alvo tiver `status = LOCKED` E `locked_until > agora` **ENTÃO** 403 `ERR-AUTH-004` **SENÃO** prosseguir | `TimeProvider` | `ERR-AUTH-004` | RF-06 | UT-3-06 |
| RN-84 | Autorização | **SE** alvo tiver `status = SUSPENDED` OU `denylist:user:{id}` ativa **ENTÃO** 403 `ERR-AUTH-004` **SENÃO** prosseguir | `tRef = 0` | `ERR-AUTH-004` | RF-06 | RG-05, PRS-46 |
| RN-85 | Autorização | **SE** tenant ou grupo tiver `status = SUSPENDED` OU em denylist **ENTÃO** 403 `ERR-CTX-002` **SENÃO** prosseguir | `CheckTenantAsync` | `ERR-CTX-002` | RF-06 | RG-04 |
| RN-86 | Autorização | **SE** alvo não detiver vínculo `status = ACTIVE` em `tenant_member` **ENTÃO** 403 `ERR-CTX-001` **SENÃO** prosseguir | `CheckMembershipAsync` | `ERR-CTX-001` | RF-06 | RG-03 |
| RN-87 | Autorização | **SE** `target_scope` inativo no tenant OU roles vazio **ENTÃO** 403 `ERR-CTX-003` **SENÃO** prosseguir | `ResolveRolesAsync` | `ERR-CTX-003` | RF-06 | RG-01, RG-02 |
| RN-88 | Cálculo | **SE** aprovado **ENTÃO** emitir JWT com dados do alvo, `SINGLE`, `family_id = null`, `act = {sub, ticket_id}` e `exp = iat + 900` **SENÃO** não emitir | 900 s | – | RF-06 | ADR-08, PRS-41 |
| RN-89 | Integridade | **SE** emitido **ENTÃO** DEVE gravar `IMPERSONATION_STARTED` em `auth_audit_log` e confirmar antes do 200 **SENÃO** não persistir | IF-03.1 | `ERR-SYS-002` | RF-06 | AUD.M06, UT-3-06 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-06 — Iniciar impersonation RFC 8693

  Contexto:
    Dado que a base de dados PostgreSQL e o Redis estão operacionais

  @CA-06.1 @RF-06 @RN-80 @RN-88 @RN-89 @MUST
  Cenário: Emissão bem-sucedida de token de impersonation com contexto completo e auditoria
    Dado operador "01900000-0000-7000-8000-000000000001" autenticado com role "SUPPORT_AGENT" sem act
    E tenant "01900000-0000-7000-8000-000000000100" ativo no grupo "01900000-0000-7000-8000-000000000050" com escopo "BUYER"
    E utilizador alvo "01900000-0000-7000-8000-000000000002" ativo com platform_role "NONE" e vínculo ACTIVE no tenant com papel "BUYER_OPERATOR"
    Quando é enviado POST "/api/v1/auth/impersonate" com {"target_user_id":"01900000-0000-7000-8000-000000000002","target_tenant_id":"01900000-0000-7000-8000-000000000100","target_scope":"BUYER","ticket_id":"CHAMADO-123"}
    Então o status da resposta é 200
    E o campo "token_type" é "Bearer"
    E o campo "expires_in" é 900
    E o objeto "impersonation_context" contém sub "01900000-0000-7000-8000-000000000002", tenant_id "01900000-0000-7000-8000-000000000100", view_mode "SINGLE", active_scope "BUYER" e act com ticket_id "CHAMADO-123"
    E a resposta não contém o campo "refresh_token"
    E existe 1 registo na auth_audit_log com event_type "IMPERSONATION_STARTED" e ticket_id "CHAMADO-123"

  @CA-06.2 @RF-06 @RN-80 @RN-81 @RN-82 @MUST @critico
  Esquema do Cenário: Rejeição de impersonation quando chamador ou alvo violam regras de privilégio
    Quando operador com role "<role_op>" e act "<act_op>" envia POST "/api/v1/auth/impersonate" para alvo com role "<role_alvo>" e mesmo_id "<auto_del>"
    Então o status da resposta é 403
    E o campo "code" é "ERR-AUTH-010"
    E nenhum registo é inserido na auth_audit_log

    Exemplos:
      | role_op       | act_op | role_alvo     | auto_del |
      | SUPPORT_AGENT | sim    | NONE          | nao      |
      | NONE          | nao    | NONE          | nao      |
      | SUPPORT_AGENT | nao    | SUPPORT_AGENT | nao      |
      | SUPPORT_AGENT | nao    | PLATFORM_ADMIN| nao      |
      | SUPPORT_AGENT | nao    | NONE          | sim      |

  @CA-06.3 @RF-06 @RN-83 @RN-84 @MUST @critico
  Esquema do Cenário: Rejeição de impersonation quando utilizador alvo está indisponível ou bloqueado
    Dado utilizador alvo com status "<status_alvo>", locked_until "<bloqueio>" e denylist Redis "<na_denylist>"
    Quando é enviado POST "/api/v1/auth/impersonate" para o utilizador alvo
    Então o status da resposta é 403
    E o campo "code" é "ERR-AUTH-004"
    E nenhum registo é inserido na auth_audit_log

    Exemplos:
      | status_alvo | bloqueio | na_denylist |
      | SUSPENDED   | null     | nao         |
      | ACTIVE      | null     | sim         |
      | LOCKED      | futuro   | nao         |

  @CA-06.4 @RF-06 @RN-83 @MUST @borda
  Cenário: Utilizador alvo com bloqueio LOCKED expirado tem impersonation aprovada
    Dado utilizador alvo "01900000-0000-7000-8000-000000000002" com status "LOCKED" e locked_until no passado
    E chave de denylist do utilizador ausente no Redis
    Quando é enviado POST "/api/v1/auth/impersonate" com {"target_user_id":"01900000-0000-7000-8000-000000000002","target_tenant_id":"01900000-0000-7000-8000-000000000100","target_scope":"BUYER","ticket_id":"CHAMADO-123"}
    Então o status da resposta é 200
    E o campo "impersonation_context.sub" é "01900000-0000-7000-8000-000000000002"
    E existe registo na auth_audit_log com event_type "IMPERSONATION_STARTED"

  @CA-06.5 @RF-06 @RN-85 @RN-86 @RN-87 @MUST @critico
  Esquema do Cenário: Rejeição de impersonation quando tenant alvo ou vínculo não cumprem requisitos
    Dado tenant alvo com status "<status_tenant>", denylist Redis "<tenant_denylist>", vínculo "<status_vinculo>" e escopo ativo "<escopo_ativo>"
    Quando é enviado POST "/api/v1/auth/impersonate" com tenant alvo
    Então o status da resposta é 403
    E o campo "code" é "<codigo_erro>"
    E nenhum registo é inserido na auth_audit_log

    Exemplos:
      | status_tenant | tenant_denylist | status_vinculo | escopo_ativo | codigo_erro  |
      | SUSPENDED     | nao             | ACTIVE         | sim          | ERR-CTX-002  |
      | ACTIVE        | sim             | ACTIVE         | sim          | ERR-CTX-002  |
      | ACTIVE        | nao             | SUSPENDED      | sim          | ERR-CTX-001  |
      | ACTIVE        | nao             | inativo        | sim          | ERR-CTX-001  |
      | ACTIVE        | nao             | ACTIVE         | nao          | ERR-CTX-003  |

  @CA-06.6 @RF-06 @RN-88 @MUST @borda
  Esquema do Cenário: Validação de borda no formato do chamado técnico ticket_id
    Quando é enviado POST "/api/v1/auth/impersonate" com ticket_id "<ticket_teste>"
    Então o status da resposta é <status_http>
    E o campo "code" é "<codigo_erro>"

    Exemplos:
      | ticket_teste          | status_http | codigo_erro |
      | AB-1                  | 200         | –           |
      | ABCDEFGHIJ-1234567890 | 200         | –           |
      | A-1                   | 422         | ERR-VAL-004 |
      | ABCDEFGHIJK-1         | 422         | ERR-VAL-004 |
      | AB-                   | 422         | ERR-VAL-004 |
      | AB-12345678901        | 422         | ERR-VAL-004 |
      | ab-123                | 422         | ERR-VAL-004 |
      | AB 123                | 422         | ERR-VAL-004 |

  @CA-06.7 @RF-06 @RN-80 @MUST @borda
  Esquema do Cenário: Validação de existência de utilizador e tenant alvo
    Quando é enviado POST "/api/v1/auth/impersonate" com target_user_id "<alvo_id>" e target_tenant_id "<tenant_id>"
    Então o status da resposta é <status_http>
    E o campo "code" é "<codigo_erro>"

    Exemplos:
      | alvo_id                               | tenant_id                             | status_http | codigo_erro |
      | 01900000-0000-7000-8000-999999999999 | 01900000-0000-7000-8000-000000000100 | 404         | ERR-CTX-008 |
      | 01900000-0000-7000-8000-000000000002 | 01900000-0000-7000-8000-999999999999 | 404         | ERR-CTX-008 |
      | id-invalido                           | 01900000-0000-7000-8000-000000000100 | 400         | ERR-VAL-001 |
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-06 | MUST | RN-80 a RN-89 | EP-05 | EX-06.1 a EX-06.11 | CA-06.1 a CA-06.7 | F-01 |

**Totais:** RF = 1 · RN = 10 · EP = 1 · EV = 0 · CA = 7 (`@critico` = 3) · Σw = 10

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-160 | EP-05 bem-sucedido gera novo `jti` e nova auditoria | Rastreabilidade estrita de sessões | PO, 2026-10-19 |
| PRS-161 | Bloqueio `LOCKED` no alvo com `locked_until ≤ agora` expira | Expiração automática de bloqueio | PO, 2026-10-19 |
| PRS-162 | Leitura de `user_account` usa `BypassTenantFilter = true` | Entidade global sem isolamento | PO, 2026-10-19 |

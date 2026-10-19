# M-02 · Sessão e refresh

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-02) · **Estado:** Proposta · **Atualizado:** 2026-10-19
> **Pacote:** este doc + `10` + `30` (ENT-08, enums) + `40` (§1–§3, EP-03, EV-04, IF-01, IF-03.1, IF-04.2, IF-06.1, IF-07.5, IF-08.1, IF-09, tipos) + `70` + `90` (UT-3-02, PRS-33, PRS-34, PRS-45). Total: ~47 KiB (teto ≤ 60 KiB).

## 1. Âmbito do módulo

| Dentro | Fora |
|---|---|
| Rotação de refresh token (EP-03), deteção de reuso e sessão | Autenticação por credenciais (M-01) e contexto (M-03) |
| Interface `ISessionStore` (`IF-01`) e tabela `refresh_token` (ENT-08) | Emissão JWT (M-07), publicação Kafka (M-14), acessos (M-08) |

**Escreve:** `refresh_token` (ENT-08) · **Lê:** `refresh_token` (ENT-08) · **Usa:** M-00 (IF-03, IF-04, IF-08), M-07 (IF-06), M-08 (IF-09), M-14 (IF-07)

## 2. Endpoints e eventos (detalhe)

### EP-03 `POST /api/v1/auth/refresh`

| Campo | Valor |
|---|---|
| RF | RF-04 |
| Permissão | Pública (posse do token; `Authorization` ignorado) |
| Idempotência | Não (consome token ativo e cria filho) |
| RN | RN-30 a RN-37, RN-39 |

**Request:** `refresh_token` (body base64url 43 chars: `^[A-Za-z0-9_-]{43}$`).
```json
{"refresh_token":"dGhpcy1pcy1hLXNhbXBsZS1yZWZyZXNoLXRva2VuLTQz"}
```

**Ordem de avaliação:** 1. Rota/método → 404/405. 2. Corpo ≤ 64 KiB / Content-Type → 413/415. 3. Formato 43 chars → 400 `ERR-VAL-001`. 4. Hash SHA-256 na base (RN-31) → 401 `ERR-AUTH-005`. 5. `status ≠ CONSUMED` (RN-32) → 401 `ERR-AUTH-006` (revoga família, Redis, EV-04). 6. Revalidação `IF-09` (RN-33) → 403 (revoga família com commit). 7. `status = ACTIVE` e `expires_at > now` (RN-34) → 401 `ERR-AUTH-005`. 8. UPDATE condicionado e insert (RN-35, RN-37) → 200 OK (colisão aplica passo 5).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-005`/`ERR-AUTH-006`; 403 `ERR-AUTH-004`/`ERR-CTX-001` a `004`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 503 `ERR-SYS-002`.

**Sucesso (200):** `access_token` (JWT via `IF-06`), `token_type` (`"Bearer"`), `expires_in` (900), `refresh_token` (43 chars), `refresh_expires_in` (604800).
```json
{"access_token":"jwt.token","token_type":"Bearer","expires_in":900,"refresh_token":"dGhpcy1pcy1hLXNhbXBsZS1yZWZyZXNoLXRva2VuLTQz","refresh_expires_in":604800}
```
**Efeitos:** 1. UPDATE para `CONSUMED`, INSERT filho `ACTIVE`; 2. Auditoria `TOKEN_REFRESHED` (`IF-03.1`).
**Transação:** Rotação e auditoria na mesma transação.

### IF-01 `ISessionStore`
- **IF-01.1 `CreateFamilyAsync(userId)`:** Insere `refresh_token` `ACTIVE`, nova família, `view_mode = NONE`, `expires_at = now + 604800 s`. Devolve token.
- **IF-01.2 `UpdateActiveContextAsync(familyId, context)`:** Atualiza contexto da linha `ACTIVE`. Devolve `false` se inexistente.
- **IF-01.3 `RevokeFamilyAsync(familyId)`:** Linhas `ACTIVE`/`CONSUMED` para `REVOKED` (`revoked_at = now`). Idempotente.
- **IF-01.4 `RevokeByUserAsync(userId)`:** Revoga famílias do utilizador.
- **IF-01.5 `RevokeByTenantAsync(tenantId)`:** Revoga famílias vinculadas ao tenant (`active` ou `allowed`).
- **IF-01.6 `RevokeByUserAndTenantsAsync(userId, tenantIds)`:** Revoga famílias do utilizador nos tenants indicados.

## 3. Requisitos funcionais

### RF-04 — Rotação de refresh token e gestão de sessão

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Cliente HTTP via EP-03 ou chamadores de `IF-01` |
| Pré-condições | BD disponível; `ISigningKey` ativa |
| RN | RN-30 a RN-39 |

**Fluxo principal:** 1. POST EP-03 com `refresh_token`. 2. Valida formato e calcula SHA-256. 3. SE `CONSUMED`, reuso (RN-32, 401 `ERR-AUTH-006`). 4. Revalida via `IF-09` (RN-33). 5. UPDATE para `CONSUMED` e INSERT do filho `ACTIVE` (RN-35). 6. Emite JWT (`IF-06.1`), audita `TOKEN_REFRESHED` e retorna 200.

**Fluxos de exceção:** EX-04.1 (hash ausente → 401 `ERR-AUTH-005`); EX-04.2 (token `CONSUMED` → 401 `ERR-AUTH-006`, revoga família, Redis, `EV-04`); EX-04.3 (contexto inválido → 403 catálogo, revoga família); EX-04.4 (token revogado/expirado → 401 `ERR-AUTH-005`); EX-04.5 (concorrência 0 linhas/23505 → rollback, aplica EX-04.2).

**Concorrência e idempotência:** Colisão simultânea no mesmo token resulta em exatamente um 200 e um 401 `ERR-AUTH-006` (CA-04.6). Revogações em `IF-01` são idempotentes (CA-04.7). Falha de processo desfaz transação (CA-04.1).

**Pós-condições:** Token apresentado passa a `CONSUMED`; novo filho `ACTIVE` com TTL 604 800 s.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-30 | Formato | **SE** `refresh_token` recebido **ENTÃO** **DEVE** ter 43 chars base64url e hash SHA-256 **SENÃO** rejeita 400 | `LEN = 43` | `ERR-VAL-001` | RF-04 | PRS-34 |
| RN-31 | Integridade | **SE** hash não existir na base **ENTÃO** **DEVE** responder 401 sem alterar dados **SENÃO** prossegue | – | `ERR-AUTH-005` | RF-04 | UT-3-02 |
| RN-32 | Integridade | **SE** token for `CONSUMED` **ENTÃO** em nova transação **DEVE** revogar família (`IF-01.3`), bloquear Redis (`IF-04.2`), auditar `TOKEN_REUSE_DETECTED`, enfileirar `EV-04` e responder 401 **SENÃO** prossegue | `TTL = 900 s` | `ERR-AUTH-006` | RF-04 | ADR-07 |
| RN-33 | Segurança | **SE** token for `ACTIVE` **ENTÃO** **DEVE** revalidar contexto via `IF-09`: SE falhar, **DEVE** revogar família com commit e responder 403 **SENÃO** prossegue | – | 403 catálogo | RF-04 | D-19, PRS-84 |
| RN-34 | Temporal | **SE** token for `REVOKED` ou `expires_at <= now` **ENTÃO** **DEVE** responder 401 sem efeitos **SENÃO** prossegue | – | `ERR-AUTH-005` | RF-04 | UT-3-02 |
| RN-35 | Integridade | **SE** rotação executar **ENTÃO** **DEVE** atualizar token para `CONSUMED` onde `status = 'ACTIVE'` e inserir filho `ACTIVE` com TTL 604 800 s **SENÃO** falha | `TTL = 604800 s` | – | RF-04 | ENT-08, PRS-33 |
| RN-36 | Temporal | **SE** rotação for 200 **ENTÃO** `access_token` **DEVE** ter `expires_in = 900` e refresh token **DEVE** ter `refresh_expires_in = 604800` **SENÃO** falha | `EXP = 900`, `R_EXP = 604800` | – | RF-04 | RNF-03 |
| RN-37 | Integridade | **SE** UPDATE afetar 0 linhas ou lançar 23505 **ENTÃO** **DEVE** desfazer transação e aplicar RN-32 em transação nova **SENÃO** prossegue | – | `ERR-AUTH-006` | RF-04 | ENT-08 |
| RN-38 | Integridade | **SE** revogação via `IF-01` for chamada **ENTÃO** **DEVE** transitar linhas `ACTIVE` e `CONSUMED` para `REVOKED` com idempotência **SENÃO** falha | – | – | RF-04 | ENUM-05 |
| RN-39 | Integridade | **SE** rotação for 200 **ENTÃO** **DEVE** registar auditoria `TOKEN_REFRESHED` com `user_id` e contexto **SENÃO** não audita | – | – | RF-04 | AUD.M02 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-04 — Rotação de refresh token e gestão de sessão

  Contexto:
    Dado utilizador "0192e2c0-0001-7000-8000-000000000001" ativo, família "0192e2c0-00f1-7000-8000-0000000000f1", tenant "0192e2c0-0010-7000-8000-000000000010" e instante 1776600000

  @CA-04.1 @RF-04 @RN-30 @RN-35 @RN-36 @RN-39 @MUST
  Cenário: Rotação com sucesso e recálculo de permissões
    Dado token "RT1" ACTIVE na família do contexto com expires_at 1777204800
    E contexto SINGLE com tenant do contexto, BUYER e IF-09 válida
    Quando POST "/api/v1/auth/refresh" com refresh_token "RT1"
    Então status 200, token_type "Bearer", expires_in 900, refresh_expires_in 604800
    E "RT1" passa a CONSUMED e novo token filho ACTIVE tem expires_at 1777204800

  @CA-04.2 @RF-04 @RN-31 @MUST
  Cenário: Tentativa com token inexistente na fronteira de 43 caracteres
    Quando POST "/api/v1/auth/refresh" com token de 43 chars ausente
    Então status 401, código "ERR-AUTH-005" e zero linhas alteradas

  @CA-04.3 @RF-04 @RN-32 @MUST @critico
  Cenário: Reuso de token consumido revoga família, aciona denylist e emite EV-04
    Dado token "RT_OLD" CONSUMED e "RT_ACT" ACTIVE na família do contexto
    Quando POST "/api/v1/auth/refresh" com refresh_token "RT_OLD"
    Então status 401, código "ERR-AUTH-006", família REVOKED, Redis bloqueado e EV-04 enfileirado

  @CA-04.4 @RF-04 @RN-33 @MUST @critico
  Esquema do Cenário: Revalidação de contexto reprovada revoga família e retorna 403
    Dado token "RT_VAL" ACTIVE na família do contexto
    E consulta IF-09 reprovada com motivo <motivo> e erro <codigo>
    Quando POST "/api/v1/auth/refresh" com refresh_token "RT_VAL"
    Então status 403, código "<codigo>" e família REVOKED
    Exemplos:
      | motivo           | codigo       |
      | "USER_SUSPENDED" | ERR-AUTH-004 |
      | "TENANT_SUSP"    | ERR-CTX-002  |
      | "NO_MEMBERSHIP"  | ERR-CTX-001  |

  @CA-04.5 @RF-04 @RN-34 @MUST
  Cenário: Token expirado na fronteira exata devolve 401 sem efeitos
    Dado token ACTIVE com expires_at 1776600000 (fronteira exata)
    Quando POST "/api/v1/auth/refresh" com esse token
    Então status 401, código "ERR-AUTH-005" e família inalterada

  @CA-04.6 @RF-04 @RN-35 @RN-37 @MUST @critico
  Cenário: Concorrência simultânea trata colisão de rotação como reuso
    Dado dois pedidos concorrentes com mesmo token ativo "RT_CONC"
    Quando ambos executam rotação concorrente
    Então um conclui 200 e outro falha no UPDATE, desfaz, retorna 401 "ERR-AUTH-006" e revoga família

  @CA-04.7 @RF-04 @RN-38 @MUST
  Cenário: Idempotência na revogação de família em ISessionStore
    Dado família com 1 token ACTIVE e 2 tokens CONSUMED
    Quando IF-01.3 RevokeFamilyAsync chamado pela primeira vez
    Então retorna 3 linhas afetadas e passam a REVOKED
    Quando IF-01.3 RevokeFamilyAsync chamado novamente
    Então retorna 0 linhas afetadas sem erro

  @CA-04.8 @RF-04 @RN-36 @MUST
  Cenário: Validação de limites temporais RNF-03
    Quando rotação com sucesso em 1776600000
    Então access_token tem exp - iat igual a 900 s
    E refresh_token filho tem expires_at - created_at igual a 604800 s
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-04 | MUST | RN-30 a RN-39 | EP-03 | EX-04.1 a EX-04.5 | CA-04.1 a CA-04.8 | F-00 |

**Totais:** RF = 1 · RN = 10 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado |
|---|---|---|---|
| PRS-120 | Refresh token gerado com 32 bytes em base64url (43 chars) | Entropia 256 bits (PRS-34) | PO, 2026-10-19 |
| PRS-121 | EV-04 prioriza linha ACTIVE da família no reuso | Rastrear contexto operacional | PO, 2026-10-19 |
| PRS-122 | Índice `uq_refresh_token_family_active` previne duplicidade ativa | Proteção SGBD contra corridas | PO, 2026-10-19 |

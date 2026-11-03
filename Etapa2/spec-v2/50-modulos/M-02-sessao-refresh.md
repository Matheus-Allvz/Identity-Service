# M-02 · Sessão e refresh

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-02) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** este doc + `10` + `30` (ENT-08, enums) + `40` (§1-§3, EP-03, EV-04, IF-01, IF-03.1, IF-04.2, IF-06.1, IF-07.5, IF-08.1, IF-09) + `70` + `90` (UT-3-02). Total: ~47 KiB.

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Rotação EP-03, deteção de reuso e gestão de sessão | Autenticação por credenciais (M-01) e troca de contexto (M-03) |
| Interface `ISessionStore` (`IF-01`) e tabela `refresh_token` (ENT-08) | Emissão JWT (M-07), Kafka (M-14), acessos (M-08) |

**Escreve/Lê:** `refresh_token` (ENT-08) · **Usa:** M-00 (IF-03, IF-04, IF-08), M-07 (IF-06), M-08 (IF-09), M-14 (IF-07)

## 2. Endpoints e eventos (detalhe)

### EP-03 `POST /api/v1/auth/refresh`
| Campo | Valor |
|---|---|
| RF / RN / Permissão | RF-04 / RN-11, RN-12, RN-40 a RN-47 / Pública (`Authorization` ignorado) |
| Idempotência | Não (consome token ativo e cria filho) |

**Request:** `{"refresh_token":"dGhpcy1pcy1hLXNhbXBsZS1yZWZyZXNoLXRva2VuLTQz"}` (43 chars base64url).

**Ordem de avaliação:** 1. Rota/método → 404/405. 2. Corpo ≤ 64 KiB / Content-Type → 413/415. 3. Formato 43 chars base64url (RN-11) → 400 `ERR-VAL-001`. 4. Hash SHA-256 na BD (RN-12) → 401 `ERR-AUTH-005`. 5. Se `status = CONSUMED` (RN-40) → 401 `ERR-AUTH-006` (revoga família, Redis, EV-04). 6. Revalidação `IF-09` (RN-41) → 403 catálogo (revoga família). 7. `status ≠ ACTIVE` ou expirado (RN-42) → 401 `ERR-AUTH-005`. 8. UPDATE condicional (`status = CONSUMED`, `replaced_by_id = filho.id`) e INSERT filho `ACTIVE` (RN-43, RN-45) → 200 OK (colisão aplica passo 5).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-005`/`ERR-AUTH-006`; 403 `ERR-AUTH-004`/`ERR-CTX-001` a `004`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 503 `ERR-SYS-002`.

**Sucesso (200):** `access_token`, `token_type` (`"Bearer"`), `expires_in` (900), `refresh_token` (43 chars), `refresh_expires_in` (604800). Headers: `Cache-Control: no-store`, `Pragma: no-cache`. Efeitos: UPDATE pai para `CONSUMED` com `replaced_by_id = filho.id`, INSERT filho `ACTIVE` (TTL 604 800 s), auditoria `TOKEN_REFRESHED` na mesma transação.

### EV-04 `sessao_comprometida`
Publicado na outbox por IF-07.5 ao detetar reuso em EP-03 (RN-40): `event_id` (UUID v7), `user_id` (UUID), `family_id` (UUID), `reused_refresh_token_id` (UUID), `ip_address`, `detected_at` (ISO 8601 UTC).

### IF-01 `ISessionStore`
- **IF-01.1 `CreateFamilyAsync(Guid userId)`:** Insere `refresh_token` `ACTIVE`, nova família, `view_mode = NONE`, TTL 604 800 s.
- **IF-01.2 `UpdateActiveContextAsync(Guid familyId, ViewMode viewMode, Guid groupId, Guid? activeTenantId, IReadOnlyList<Guid> allowedTenants, OperationalScope activeScope)`:** Atualiza linha `ACTIVE` da família. Devolve `false` se inexistente.
- **IF-01.3 `RevokeFamilyAsync(Guid familyId)`:** Linhas `ACTIVE`/`CONSUMED` para `REVOKED` (idempotente).
- **IF-01.4 `RevokeByUserAsync(Guid userId)`:** Revoga famílias do utilizador.
- **IF-01.5 `RevokeByTenantAsync(Guid tenantId)`:** Revoga famílias com o tenant.
- **IF-01.6 `RevokeByUserAndTenantsAsync(Guid userId, IReadOnlyList<Guid> tenantIds)`:** Revoga famílias nos tenants indicados.

## 3. Requisitos funcionais

### RF-04 — Rotação de refresh token e gestão de sessão
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / Cliente HTTP via EP-03 ou chamadores de `IF-01` |
| Pré-condições / RN | BD disponível; `ISigningKey` ativa / RN-11, RN-12, RN-40 a RN-47 |

**Fluxo principal:** 1. POST EP-03 com `refresh_token`. 2. Valida formato 43 chars e calcula SHA-256 (RN-11, RN-12). 3. Se `CONSUMED`, reuso (RN-40, 401 `ERR-AUTH-006`). 4. Revalida via `IF-09` (RN-41). 5. UPDATE pai para `CONSUMED` com `replaced_by_id = filho.id` e INSERT filho `ACTIVE` (RN-43). 6. Emite JWT (`IF-06.1`), audita `TOKEN_REFRESHED` e retorna 200 com headers sem cache.

**Fluxos de exceção:** EX-04.1 (hash ausente → 401 `ERR-AUTH-005`); EX-04.2 (token `CONSUMED` → 401 `ERR-AUTH-006`, revoga família, Redis, `EV-04`); EX-04.3 (contexto inválido → 403 catálogo, revoga família); EX-04.4 (token revogado/expirado → 401 `ERR-AUTH-005`); EX-04.5 (concorrência 0 linhas/23505 → rollback, aplica EX-04.2).

**Concorrência e idempotência:** Colisão no mesmo token resulta em um 200 e um 401 `ERR-AUTH-006` (CA-04.6). Revogações em `IF-01` são idempotentes (CA-04.7). Falha desfaz transação (CA-04.1).

**Pós-condições:** Token pai passa a `CONSUMED` com `replaced_by_id`; novo filho `ACTIVE` com TTL 604 800 s.

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-11 | Formato | **SE** token recebido **ENTÃO** tem 43 chars base64url e hash SHA-256 **SENÃO** 400 | – | `ERR-VAL-001` | RF-04 | PRS-34 |
| RN-12 | Integridade | **SE** hash não existir na BD **ENTÃO** responde 401 sem alterar dados **SENÃO** segue | – | `ERR-AUTH-005` | RF-04 | UT-3-02 |
| RN-40 | Integridade | **SE** token `CONSUMED` **ENTÃO** revoga família, Redis, `TOKEN_REUSE_DETECTED`, `EV-04` e 401 | TTL 900 s | `ERR-AUTH-006` | RF-04 | ADR-07 |
| RN-41 | Segurança | **SE** token `ACTIVE` **ENTÃO** revalida IF-09; falha revoga família e responde 403 **SENÃO** segue | – | 403 catálogo | RF-04 | D-19, PRS-84 |
| RN-42 | Temporal | **SE** token `REVOKED` ou expirado **ENTÃO** responde 401 sem efeitos **SENÃO** segue | – | `ERR-AUTH-005` | RF-04 | UT-3-02 |
| RN-43 | Integridade | **SE** rotação executar **ENTÃO** atualiza token para `CONSUMED` com `replaced_by_id = filho.id` e insere filho `ACTIVE` | TTL 604800 s | – | RF-04 | ENT-08, PRS-33 |
| RN-44 | Temporal | **SE** rotação for 200 **ENTÃO** token tem `expires_in = 900` e refresh `refresh_expires_in = 604800` | – | – | RF-04 | RNF-03 |
| RN-45 | Integridade | **SE** UPDATE afetar 0 linhas ou erro 23505 **ENTÃO** desfaz e aplica RN-40 em nova transação **SENÃO** segue | – | `ERR-AUTH-006` | RF-04 | ENT-08 |
| RN-46 | Integridade | **SE** revogação IF-01 **ENTÃO** transita `ACTIVE` e `CONSUMED` para `REVOKED` (idempotente) | – | – | RF-04 | ENUM-05 |
| RN-47 | Integridade | **SE** rotação 200 **ENTÃO** grava auditoria `TOKEN_REFRESHED` com `user_id` e contexto | – | – | RF-04 | AUD.M02 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-04 — Rotação de refresh token e gestão de sessão

  Contexto:
    Dado utilizador "0192e2c0-0001-7000-8000-000000000001" ativo, família "0192e2c0-00f1-7000-8000-0000000000f1", tenant "0192e2c0-0010-7000-8000-000000000010" e instante 1776600000

  @CA-04.1 @RF-04 @RN-11 @RN-43 @RN-44 @RN-47 @MUST @borda
  Cenário: Rotação com sucesso e recálculo de permissões
    Dado token "RT1" ACTIVE na família com expires_at 1777204800 e contexto SINGLE
    Quando POST "/api/v1/auth/refresh" com refresh_token "RT1"
    Então status 200, "token_type" "Bearer", "expires_in" 900, "refresh_expires_in" 604800 e "RT1" passa a CONSUMED

  @CA-04.2 @RF-04 @RN-12 @MUST @borda
  Cenário: Tentativa com token inexistente na fronteira de 43 caracteres
    Quando POST "/api/v1/auth/refresh" com token inexistente de 43 chars
    Então status 401, código "ERR-AUTH-005" e zero linhas alteradas

  @CA-04.3 @RF-04 @RN-40 @MUST @critico @borda
  Cenário: Reuso de token consumido revoga família, aciona denylist e emite EV-04
    Dado token "RT_OLD" CONSUMED e "RT_ACT" ACTIVE na família
    Quando POST "/api/v1/auth/refresh" com refresh_token "RT_OLD"
    Então status 401, código "ERR-AUTH-006", família REVOKED, Redis bloqueado e EV-04 enfileirado

  @CA-04.4 @RF-04 @RN-41 @MUST @critico @borda
  Esquema do Cenário: Revalidação de contexto reprovada revoga família e retorna 403
    Dado token "RT_VAL" ACTIVE na família
    E consulta IF-09 reprovada com motivo <motivo> e erro <codigo>
    Quando POST "/api/v1/auth/refresh" com refresh_token "RT_VAL"
    Então status 403, código "<codigo>" e família REVOKED
    Exemplos:
      | motivo           | codigo       |
      | "USER_SUSPENDED" | ERR-AUTH-004 |
      | "TENANT_SUSP"    | ERR-CTX-002  |

  @CA-04.5 @RF-04 @RN-42 @MUST @borda
  Cenário: Token expirado na fronteira exata devolve 401 sem efeitos
    Dado token ACTIVE com expires_at 1776600000
    Quando POST "/api/v1/auth/refresh" com esse token
    Então status 401 e código "ERR-AUTH-005"

  @CA-04.6 @RF-04 @RN-43 @RN-45 @MUST @critico @borda
  Cenário: Concorrência simultânea trata colisão de rotação como reuso
    Dado dois pedidos concorrentes com token "RT_CONC"
    Quando executam rotação concorrente
    Então um conclui 200 e outro falha com 401 "ERR-AUTH-006" e revoga família

  @CA-04.7 @RF-04 @RN-46 @MUST @borda
  Cenário: Idempotência na revogação de família em ISessionStore
    Dado família com 1 token ACTIVE e 2 CONSUMED
    Quando IF-01.3 RevokeFamilyAsync chamado duas vezes
    Então na primeira retorna 3 linhas REVOKED e na segunda 0 linhas

  @CA-04.8 @RF-04 @RN-44 @MUST @borda
  Cenário: Validação de limites temporais RNF-03
    Quando rotação com sucesso
    Então access_token exp - iat é 900 s e refresh filho expires_at - created_at é 604800 s
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-04 | MUST | RN-11, RN-12, RN-40 a RN-47 | EP-03, EV-04 | EX-04.1 a EX-04.5 | CA-04.1 a CA-04.8 | F-01 |

**Totais:** RF = 1 · RN = 10 · EP = 1 · EV = 1 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-120 | Refresh token gerado com 32 bytes em base64url (43 chars) | Entropia 256 bits (PRS-34) | PO, 2026-11-02 (D-23) |
| PRS-121 | EV-04 prioriza linha ACTIVE da família no reuso | Rastrear contexto operacional | PO, 2026-11-02 (D-23) |
| PRS-122 | Índice `uq_refresh_token_family_active` previne duplicidade ativa | Proteção SGBD contra corridas | PO, 2026-11-02 (D-23) |

# M-04 · Criar tenant

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-04) · **Estado:** Rascunho (ciclo 5) · **Atualizado em:** 2026-10-19
> **Pacote:** este doc + `10` + `30` (ENT-01..03, ENUM-01..03, AUD.M04) + `40` (§1..§3, EP-06, EV-01, IF-03.1, IF-07.1, IF-08.1/2, IF-10/10.1, IF-12.1, tipos partilhados, matriz §5) + `70` (§1) + `90` (UT-3-04, PRS-36/37). Total: ~57 KiB (teto ≤ 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Criação de tenant (CNPJ) via EP-06 | Autenticação e tokens (M-01) |
| Escopos em `tenant_scope` | Consulta de contextos (M-08) |
| Vínculo criador via IF-12.1 (`GROUP_MANAGER`) | Gestão de outros membros (M-05) |
| Registo EV-01 na outbox (IF-07.1) | Despacho Kafka (M-14) |
| Auditoria via IF-03.1 | Status de tenant (M-12 por IF-10.1) |
| Interface `ITenantStatusWriter` (IF-10.1) | Grupos económicos (fora da v2) |

**Escreve:** ENT-02 (`tenant`), ENT-03 (`tenant_scope`) · **Lê:** ENT-01 (`economic_group`), ENT-02 (`tenant`) · **Usa:** M-00 (IF-03.1, IF-08.1, IF-08.2), M-05 (IF-12.1), M-14 (IF-07.1)

## 2. Endpoints e eventos (detalhe)

### EP-06 `POST /api/v1/tenants`
| Campo | Valor |
|---|---|
| RF | RF-07 |
| Autenticação / Permissão | JWT (`Bearer`); `PLATFORM_ADMIN` OU (`active_scope = TENANT_ADMIN` E `tenant:criar` ∈ `permissions` E `group_id` coincide) |
| Idempotência | Não. CNPJ duplicado devolve 409 `ERR-CTX-005` |
| RN | RN-60, RN-61, RN-62, RN-63, RN-64, RN-65, RN-66 |

**Request**
| Campo | Local | Tipo | Obg | Validação |
|---|---|---|---|---|
| `group_id` | body | string | Sim | UUID formato `D` |
| `cnpj` | body | string | Sim | 14 dígitos numéricos, módulo 11 válido, sem 14 dígitos iguais |
| `legal_name` | body | string | Sim | Texto de 2 a 200 caracteres após `btrim` |
| `branch_type` | body | string | Sim | ENUM-02: `"HQ"` ou `"BRANCH"` |
| `geo_lat` | body | número | Sim | Decimal em [-90.000000, 90.000000] |
| `geo_lng` | body | número | Sim | Decimal em [-180.000000, 180.000000] |
| `scopes` | body | array | Sim | 1 a 4 strings distintas de ENUM-03 (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) |

```json
{"group_id":"01923456-789a-7bc8-9012-3456789abcde","cnpj":"99000001000101","legal_name":"Empresa Teste Logística LTDA","branch_type":"HQ","geo_lat":-23.55052,"geo_lng":-46.633308,"scopes":["BUYER"]}
```

**Ordem de avaliação**
| # | Verificação | Camada (`40` §2) | Se falhar |
|---|---|---|---|
| 1 | Rota e método `POST /api/v1/tenants` | 0 (Encaminhamento) | 404 `ERR-REQ-001` / 405 `ERR-REQ-002` |
| 2 | Tamanho do corpo ≤ 65 536 bytes | 1 (Transporte) | 413 `ERR-REQ-003` |
| 3 | `Content-Type: application/json[; charset=utf-8]` | 1 (Transporte) | 415 `ERR-REQ-004` |
| 4 | Chave em `ISigningKey` | 2 (Autenticação) | 503 `ERR-SYS-002` |
| 5 | Token JWT RS256 válido, não expirado, sem `denylist:jti` (RG-06) | 2 (Autenticação) | 401 `ERR-AUTH-002` |
| 6 | Sem `denylist:user` para `sub` (e `act.sub`) com valor ≥ `iat` (RG-07) | 2 (Autenticação) | 403 `ERR-AUTH-004` |
| 7 | Sem `denylist:tenant` em `allowed_tenants` (RG-08) | 2 (Autenticação) | 403 `ERR-CTX-002` |
| 8 | JSON válido, objeto raiz, sem campos extras ou repetidos | 3 (Formato) | 400 `ERR-VAL-001` |
| 9 | Presença e não nulidade dos 7 campos obrigatórios | 3 (Formato) | 400 `ERR-VAL-001` |
| 10 | Sem NUL (`\u0000`) em textos | 3 (Formato) | 400 `ERR-VAL-001` |
| 11 | `group_id` UUID `D`, `legal_name` 2..200, `branch_type` enum, `scopes` 1..4 distintos | 3 (Formato) | 400 `ERR-VAL-001` |
| 12 | `cnpj` 14 dígitos, módulo 11, sem 14 dígitos iguais (RN-60) | 3 (Formato) | 422 `ERR-VAL-002` |
| 13 | `geo_lat` [-90, 90] e `geo_lng` [-180, 180] (RN-61) | 3 (Formato) | 422 `ERR-VAL-002` |
| 14 | Autorização: `PLATFORM_ADMIN` OU C6 (`TENANT_ADMIN`, `tenant:criar`, `group_id` coincide) (RN-62) | 4 (Autorização) | 403 `ERR-CTX-007` |
| 15 | Existência de `economic_group` (RN-63) | 5 (Existência) | 404 `ERR-CTX-008` |
| 16 | Grupo com `status = ACTIVE` (RN-64) | 6 (Regras) | 403 `ERR-CTX-002` |
| 17 | `cnpj` não registado em `tenant` (RN-65) | 6 (Regras) | 409 `ERR-CTX-005` |

**Respostas:** 201 Created; 400 `ERR-VAL-001`; 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`/`ERR-CTX-002`/`ERR-CTX-007`; 404 `ERR-REQ-001`/`ERR-CTX-008`; 405 `ERR-REQ-002`; 409 `ERR-CTX-005`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 422 `ERR-VAL-002`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Corpo de sucesso** (`Location: /api/v1/tenants/{id}`): `id` (UUID v7), `group_id` (UUID), `cnpj` (string), `legal_name` (string), `branch_type` (`HQ`\|`BRANCH`), `geo_lat` (número), `geo_lng` (número), `status` (`"ACTIVE"`), `scopes` (array `OperationalScope` ordinal ascendente), `created_at` (UTC `yyyy-MM-ddTHH:mm:ssZ`).
```json
{"id":"01923456-789a-7def-0123-456789abcdef","group_id":"01923456-789a-7bc8-9012-3456789abcde","cnpj":"99000001000101","legal_name":"Empresa Teste Logística LTDA","branch_type":"HQ","geo_lat":-23.55052,"geo_lng":-46.633308,"status":"ACTIVE","scopes":["BUYER","TENANT_ADMIN"],"created_at":"2026-10-19T18:00:00Z"}
```

**Efeitos e Transacionalidade:** Transação única no `IdentityDbContext` (falha = rollback): 1. Chama `AuthorizeTenantWrite(tenantId)` (IF-08.2). 2. Insere `tenant` (`ACTIVE`). 3. Insere `tenant_scope` (`is_active = true`). 4. Se `TENANT_ADMIN`, chama IF-12.1 `AddCreatorMembership(tenantId, userId)`. 5. Insere auditoria (IF-03.1: `TENANT_CREATED` e opcionalmente `MEMBER_ADDED`). 6. Enfileira EV-01 via IF-07.1.

### EV-01 `empresa_cadastrada`
Outbox no sucesso do EP-06: `event_id`, `tenant_id`, `group_id`, `cnpj`, `legal_name`, `branch_type`, `geo_lat`, `geo_lng`, `status` (`ACTIVE`), `scopes`, `created_at`.

## 3. Requisitos funcionais

### RF-07 — Criar tenant
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Utilizador `PLATFORM_ADMIN` ou `TENANT_ADMIN` com permissão `tenant:criar` no grupo |
| Pré-condições | Token válido; grupo económico ativo |
| RN | RN-60, RN-61, RN-62, RN-63, RN-64, RN-65, RN-66 |

**Fluxo principal:** 1. `POST /api/v1/tenants`. 2. Valida transporte, token, formato, RN-60 e RN-61. 3. Valida autorização (RN-62). 4. Gera `id` v7 e chama `AuthorizeTenantWrite(tenantId)`. 5. Valida existência (RN-63) e status (RN-64) do grupo. 6. Valida unicidade de `cnpj` (RN-65). 7. Se `TENANT_ADMIN`, aplica RN-66. 8. Em transação única: grava `tenant`, `tenant_scope`, IF-12.1 (se `TENANT_ADMIN`), auditoria IF-03.1 e EV-01 na outbox IF-07.1. 9. Responde 201 com `Location`.

**Fluxos de exceção:** EX-07.1 (formato inválido → 400 `ERR-VAL-001`); EX-07.2 (token ausente/inválido → 401 `ERR-AUTH-002`); EX-07.3 (denylist → 403 `ERR-AUTH-004`/`ERR-CTX-002`); EX-07.4 (CNPJ ou geo inválidos → 422 `ERR-VAL-002`); EX-07.5 (sem autorização → 403 `ERR-CTX-007`); EX-07.6 (grupo inexistente → 404 `ERR-CTX-008`); EX-07.7 (grupo suspenso → 403 `ERR-CTX-002`); EX-07.8 (CNPJ duplicado → 409 `ERR-CTX-005`); EX-07.9 (falha BD → 503 `ERR-SYS-002`, rollback).

**Concorrência, idempotência e retomada**
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Concorrência com mesmo CNPJ | Exatamente um conclui 201; outro falha 409 `ERR-CTX-005` por `uq_tenant_cnpj` | CA-07.3 |
| Processo morto na transação | Rollback total no PostgreSQL; nada persiste | CA-07.5 |
| Reenvio após falha de rede | Se persistido, responde 409 `ERR-CTX-005`; se não, cria 201 | CA-07.3 |

**Pós-condições:** Tenant `ACTIVE` e escopos criados; vínculo do criador (se `TENANT_ADMIN`); EV-01 na outbox; registos de auditoria gravados.

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-60 | Validação | **SE** `cnpj` contiver não numéricos, tamanho ≠ 14, 14 dígitos iguais OU módulo 11 inválido **ENTÃO** 422 **SENÃO** segue | Pesos 5..2 e 6..2 | `ERR-VAL-002` | RF-07 | PRS-37, D-10 |
| RN-61 | Validação | **SE** `geo_lat` fora de [-90, 90] OU `geo_lng` fora de [-180, 180] **ENTÃO** 422 **SENÃO** segue | lat: [-90, 90]; lng: [-180, 180] | `ERR-VAL-002` | RF-07 | PRS-22, D-10 |
| RN-62 | Autorização | **SE** `platform_role = PLATFORM_ADMIN` OU (`active_scope = TENANT_ADMIN` E `tenant:criar` ∈ `permissions` E `group_id` coincide) **ENTÃO** autoriza **SENÃO** 403 | Permissão `tenant:criar` | `ERR-CTX-007` | RF-07 | Matriz §5, PRS-36 |
| RN-63 | Integridade | **SE** `group_id` não existe em `economic_group` **ENTÃO** 404 **SENÃO** segue | `economic_group` | `ERR-CTX-008` | RF-07 | Matriz §5 |
| RN-64 | Integridade | **SE** `economic_group.status = SUSPENDED` **ENTÃO** 403 **SENÃO** segue | `status = SUSPENDED` | `ERR-CTX-002` | RF-07 | UT-3-04, `10` §6 |
| RN-65 | Integridade | **SE** `cnpj` já existe em `tenant` **ENTÃO** 409 **SENÃO** segue | Restrição `uq_tenant_cnpj` | `ERR-CTX-005` | RF-07 | UT-3-04, `10` §6 |
| RN-66 | Integridade | **SE** chamador é `TENANT_ADMIN` e `scopes` não tem `TENANT_ADMIN` **ENTÃO** acrescenta `TENANT_ADMIN` em `scopes` **SENÃO** mantém | Escopo `TENANT_ADMIN` | – | RF-07 | D-04, PRS-67 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-07 — Criar tenant
  Contexto:
    Dado que existe o grupo "GRP-1" com id "01923456-789a-7bc8-9012-3456789abcde" e status "ACTIVE"
    E que existe o grupo "GRP-SUSP" com id "01923456-789a-7bc8-9012-3456789abcdf" e status "SUSPENDED"
    E utilizador "01923456-789a-7bc8-9012-3456789abcd0" ativo em "GRP-1" com "TENANT_ADMIN" e "tenant:criar"

  @CA-07.1 @RF-07 @RN-62 @RN-66 @MUST
  Cenário: Sucesso por TENANT_ADMIN acrescentando escopo administrativo
    Quando envia POST "/api/v1/tenants" autenticado como TENANT_ADMIN de "GRP-1" com group_id "01923456-789a-7bc8-9012-3456789abcde", cnpj "99000001000101", legal_name "Logística Alvorada LTDA", branch_type "HQ", geo_lat -23.55052, geo_lng -46.633308 e scopes ["BUYER"]
    Então o status é 201, Location "/api/v1/tenants/[0-9a-f-]{36}", status "ACTIVE", scopes ["BUYER", "TENANT_ADMIN"], vínculo com papel "GROUP_MANAGER", EV-01 e auditoria criados

  @CA-07.2 @RF-07 @RN-62 @MUST
  Cenário: Sucesso por PLATFORM_ADMIN sem vínculo de membro
    Quando envia POST "/api/v1/tenants" autenticado como PLATFORM_ADMIN com group_id "01923456-789a-7bc8-9012-3456789abcde", cnpj "99000002000186", legal_name "Transportes Expressos Filial", branch_type "BRANCH", geo_lat 90.0, geo_lng 180.0 e scopes ["CARRIER"]
    Então o status é 201, status "ACTIVE", scopes ["CARRIER"], sem membros em "tenant_member" e EV-01 criado

  @CA-07.3 @RF-07 @RN-65 @MUST @critico @borda
  Esquema do Cenário: Conflito de unicidade ao cadastrar CNPJ existente ou reenvio
    Dado que existe tenant com cnpj "99000001000101"
    Quando envia POST "/api/v1/tenants" com cnpj <cnpj_teste>
    Então o status é 409, code "ERR-CTX-005" e nenhum novo registo criado
    Exemplos:
      | cnpj_teste       |
      | "99000001000101" |

  @CA-07.4 @RF-07 @RN-60 @RN-61 @MUST @borda
  Esquema do Cenário: Rejeição por dados inválidos de CNPJ ou geolocalização
    Quando envia POST "/api/v1/tenants" com cnpj <cnpj_inv>, geo_lat <lat> e geo_lng <lng>
    Então o status é 422, code "ERR-VAL-002" e nenhum registo inserido em "tenant"
    Exemplos:
      | cnpj_inv         | lat        | lng         |
      | "11111111111111" | 0.0        | 0.0         |
      | "99000001000199" | 0.0        | 0.0         |
      | "99000001000101" | -90.000001 | 0.0         |
      | "99000001000101" | 90.000001  | 0.0         |
      | "99000001000101" | 0.0        | -180.000001 |
      | "99000001000101" | 0.0        | 180.000001  |

  @CA-07.5 @RF-07 @MUST @critico @borda
  Cenário: Rollback total na criação de tenant após falha simulada
    Dado que a persistência falha em "tenant_scope"
    Quando envia POST "/api/v1/tenants" com dados válidos
    Então o status é 500, code "ERR-SYS-001" e nada persiste no banco

  @CA-07.6 @RF-07 @RN-62 @MUST @critico @borda
  Esquema do Cenário: Negação de autorização para chamadores sem privilégio
    Quando envia POST "/api/v1/tenants" com token de <perfil> e group_id <grupo>
    Então o status é 403, code "ERR-CTX-007" e nenhum registo inserido
    Exemplos:
      | perfil                     | grupo                                  |
      | "BUYER"                    | "01923456-789a-7bc8-9012-3456789abcde" |
      | "TENANT_ADMIN_OUTRO_GRUPO" | "01923456-789a-7bc8-9012-3456789abce0" |

  @CA-07.7 @RF-07 @RN-63 @RN-64 @MUST @critico @borda
  Esquema do Cenário: Rejeição de grupo económico inexistente ou suspenso
    Quando envia POST "/api/v1/tenants" autenticado com group_id <grupo>
    Então o status é <status_http>, code "<codigo_erro>" e nenhum registo inserido
    Exemplos:
      | grupo                                  | status_http | codigo_erro   |
      | "01923456-789a-7bc8-9012-000000000000" | 404         | "ERR-CTX-008" |
      | "01923456-789a-7bc8-9012-3456789abcdf" | 403         | "ERR-CTX-002" |

  @CA-07.8 @RF-07 @MUST @borda
  Esquema do Cenário: Rejeição de formato de entrada malformada
    Quando envia POST "/api/v1/tenants" com o payload malformado <caso>
    Então o status é 400, code "ERR-VAL-001" e nenhum registo inserido
    Exemplos:
      | caso                                 |
      | "{\"legal_name\":\"A\"}"             |
      | "{\"legal_name\":\" A \"}"           |
      | "{\"scopes\":[]}"                    |
      | "{\"scopes\":[\"BUYER\",\"BUYER\"]}" |
      | "{\"branch_type\":\"INVALID\"}"      |
      | "{\"group_id\":\"nao-uuid\"}"        |
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-07 | MUST | RN-60, RN-61, RN-62, RN-63, RN-64, RN-65, RN-66 | EP-06, EV-01 | EX-07.1 a EX-07.9 | CA-07.1 a CA-07.8 | F-03 |

**Totais:** RF = 1 · RN = 7 · EP = 1 · EV = 1 · CA = 8 (`@critico` = 4) · Σw = 18

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-140 | Validação módulo 11 de CNPJ DEVE rejeitar 14 dígitos idênticos antes dos restos | Restos matematicamente válidos mas formalmente inválidos na Receita Federal | PO, 2026-10-19 |
| PRS-141 | Interface `ITenantStatusWriter` (IF-10.1) pertence a M-04 como dono de `tenant` | A tabela `tenant` só pode sofrer atualização de estado por interface do dono | PO, 2026-10-19 |

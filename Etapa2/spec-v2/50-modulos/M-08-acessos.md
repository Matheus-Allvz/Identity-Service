# M-08 · Acessos

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-08) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** este ficheiro + `10` (§1-§2, §3, §3.8, §4, §6-§10) + `30` (§1-§3 ENT-01 a ENT-07, ENUM-01, ENUM-03, ENUM-04, §4, §6) + `40` (§1, IF-04.5, IF-04.6, IF-09, IF-11.3, tipos) + `70` (§1) + `20` (EXT-02) + `90` (UT-3-08, PRS-05). Total: 57 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Implementação da interface `IAccessQueries` (IF-09) com operações IF-09.1 a IF-09.8 | Endpoints HTTP e orquestração de autenticação (M-01 a M-06) |
| Implementação canónica e centralizada das regras RG-01 a RG-05 e RG-10 a RG-13 | Validação de assinatura JWT e regras RG-06 a RG-08 (M-00) |
| Resolução de papéis, permissões (`SINGLE`/`MACRO_GROUP`) e agregação de contextos | Gestão direta de denylist no Redis (M-00) |
| Consulta de `GroupMembershipKind` (IF-09.7) via `ExecuteWithBypassAsync` (IF-11.3) | Escritas de vínculos ou alterações cadastrais (M-04, M-05, M-10) |

**Escreve:** Nenhuma tabela (módulo exclusivamente de consulta e validação lógica) · **Lê:** `economic_group`, `tenant`, `tenant_scope`, `user_account`, `tenant_member`, `role`, `member_role` (ENT-01 a ENT-07) · **Usa:** M-00 (IF-04.5, IF-04.6), M-13 (IF-11.3)

## 2. Endpoints e eventos (detalhe)
O M-08 não expõe endpoints HTTP nem publica eventos Kafka em runtime. É um módulo interno provedor de `IAccessQueries` (IF-09) consumido por M-01, M-02, M-03, M-06 e M-10.

## 3. Requisitos funcionais

### RF-18 — Consulta e validação de acessos, papéis e permissões
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Chamada programática a `IAccessQueries` (IF-09) por módulos consumidores |
| Pré-condições | PostgreSQL acessível; Redis acessível para consultas de denylist |
| RN | RN-100, RN-101, RN-102, RN-103, RN-104, RN-105, RN-106, RN-107 |

**Fluxo principal por operação de IF-09:**
1. `CheckUserAsync(userId, tRef)`: Avalia RG-05. Se `user_account.status = SUSPENDED` → `ERR-AUTH-004`. Se `tRef` informado e existir `denylist:user:{userId}` com valor ≥ `tRef` → `ERR-AUTH-004`. Retorna `AccessResult.Success()`.
2. `CheckTenantAsync(tenantId)`: Avalia RG-04. Se tenant inexistente → `ERR-CTX-001` (PRS-58). Se tenant ou seu grupo `SUSPENDED`, ou chave `denylist:tenant:{tenantId}` ativa no Redis → `ERR-CTX-002`. Retorna `AccessResult.Success()`.
3. `CheckMembershipAsync(userId, tenantId)`: Avalia RG-03. Se não existir `tenant_member` com `status = ACTIVE` para `(tenantId, userId)` → `ERR-CTX-001`. Retorna `AccessResult.Success()`.
4. `ResolveRolesAsync(userId, tenantId, scope)`: Avalia RG-01 e RG-02. Exige vínculo `ACTIVE`; sem vínculo `roles` vazio → 403 `ERR-CTX-003`. Se `tenant_scope(tenantId, scope)` inativo → `ERR-CTX-003`. Se `is_owner = true`, obtém todas as roles de `scope`; senão roles associadas em `member_role`. Se `roles` vazio → `ERR-CTX-003`. Permissões = união das permissões das roles. Retorna `RoleSet` com listas ordenadas ordinalmente (PRS-24).
5. `ResolveMacroGroupAsync(userId, groupId, tenantIds, scope)`: Executa na ordem fixa (UT-3-08): (1) se `tenantIds` fora de 1 a 50 elementos ou com duplicados → `ERR-CTX-004` (RG-11); (2) grupo inexistente → `ERR-CTX-004`, grupo `SUSPENDED` → `ERR-CTX-002` (RG-04); (3) para cada tenant: se inexistente ou `group_id ≠ groupId` → `ERR-CTX-004`, se sem vínculo `ACTIVE` com `can_view_group = true` → `ERR-CTX-004`, se `SUSPENDED` ou em denylist → `ERR-CTX-002`, se violar RG-02 para `scope` → `ERR-CTX-003`; (4) calcula interseção de roles e permissões (RG-12); se `permissions` vazia → `ERR-CTX-003` (RG-13). Retorna `MacroContext` com arrays ordenados.
6. `ListContextsAsync(userId)`: Avalia RG-10. Para cada `tenant_member` `ACTIVE` do utilizador cujo tenant e grupo não violem RG-04, determina escopos ativos e roles associadas. Tenants sem escopo disponível são omitidos. Ordenação ordinal por `group_name`, `legal_name`, `tenant_id` (PRS-28).
7. `GetGroupMembershipAsync(userId, groupId)`: Executa em `ExecuteWithBypassAsync` (IF-11.3). Inspeciona todos os `tenant_member` do utilizador nos tenants do grupo (qualquer status): nenhum → `GroupMembershipKind.NONE`; algum com `is_owner = true` → `GroupMembershipKind.OWNER`; senão `GroupMembershipKind.MEMBER` (D-22).
8. `ResolveTenantAsync(tenantId)`: Consulta `tenant` e `economic_group` sem filtro de isolamento. Retorna `TenantInfo` ou `null` se inexistente.

**Fluxos de exceção:** Falhas de negócio retornam `AccessResult` com `Ok = false` e `ErrorCode` preenchido. Falhas de banco de dados ou Redis propagam exceção para tratamento global do M-00 (503 `ERR-SYS-002`).

**Concorrência e idempotência:**
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Consultas simultâneas de acessos para o mesmo utilizador | Leituras com AsNoTracking puras sem efeitos colaterais de persistência; idempotência total | CA-18.1 |
| Queda de Postgres ou Redis durante consulta de acesso | A exceção propaga para o tratador do M-00 que mapeia para 503 `ERR-SYS-002` | CA-18.4 |
| Execução de `GetGroupMembershipAsync` com bypass | `ExecuteWithBypassAsync` garante que o bypass seja desativado no bloco finally | CA-18.3 |

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-100 | Autorização | **SE** `tenant_member.is_owner = true` **ENTÃO** `roles` = todas de `scope` **SENÃO** roles associadas via `member_role`. `permissions` = união sem repetição | Arrays ordenados (PRS-24) | – | RF-18 | RG-01, `10` §3.8 |
| RN-101 | Autorização | **SE** `tenant_scope` inativo OU `roles` vazio **ENTÃO** rejeitar com 403 `ERR-CTX-003` **SENÃO** aprovar | – | `ERR-CTX-003` | RF-18 | RG-02, `10` §3.8 |
| RN-102 | Autorização | **SE** não existir `tenant_member` com `status = ACTIVE` **ENTÃO** rejeitar com 403 `ERR-CTX-001` **SENÃO** aprovar | Vínculo ACTIVE | `ERR-CTX-001` | RF-18 | RG-03, `10` §3.8 |
| RN-103 | Autorização | **SE** tenant ou grupo `SUSPENDED` OU existir `denylist:tenant:{t}` **ENTÃO** rejeitar com 403 `ERR-CTX-002` **SENÃO** aprovar | Denylist Redis | `ERR-CTX-002` | RF-18 | RG-04, `10` §3.8 |
| RN-104 | Autorização | **SE** `user_account.status = SUSPENDED` OU (`tRef` informado E `denylist:user:{u}` ≥ `tRef`) **ENTÃO** rejeitar com 403 `ERR-AUTH-004` **SENÃO** aprovar | Comparação ≤ (PRS-61) | `ERR-AUTH-004` | RF-18 | RG-05, `10` §3.8 |
| RN-105 | Autorização | **SE** `tenantIds` fora de 1 a 50, com repetidos, de outro grupo OU sem vínculo ACTIVE com `can_view_group = true` **ENTÃO** rejeitar com 403 `ERR-CTX-004` **SENÃO** aprovar | 1 a 50 tenants | `ERR-CTX-004` | RF-18 | RG-11, PRS-05 |
| RN-106 | Autorização | **SE** `permissions` na interseção de `MACRO_GROUP` for vazia **ENTÃO** rejeitar com 403 `ERR-CTX-003` **SENÃO** aprovar | Interseção não vazia | `ERR-CTX-003` | RF-18 | RG-12, RG-13 |
| RN-107 | Integridade | **SE** executar `GetGroupMembershipAsync` **ENTÃO** DEVE executar exclusivamente via `ExecuteWithBypassAsync` (IF-11.3) **SENÃO** não DEVE consultar | Bypass temporário | – | RF-18 | D-22, PRS-72 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-18 — Consulta e validação de acessos, papéis e permissões (IF-09)

  Contexto:
    Dado que a base de dados relacional e o cache Redis estão operacionais

  @CA-18.1 @RF-18 @RN-100 @RN-101 @RN-102 @MUST @critico @borda
  Cenário: Resolução de papéis e permissões para proprietário de tenant (SINGLE)
    Dado que o utilizador "01900000-0000-7000-8000-000000000001" é proprietário "is_owner = true" do tenant "01900000-0000-7000-8000-000000000100"
    E que o tenant possui o escopo "BUYER" ativo
    Quando o método "ResolveRolesAsync" é invocado com escopo "BUYER"
    Então o resultado tem "Ok = true"
    E o campo "Roles" contém o papel "BUYER_OPERATOR"
    E o campo "Permissions" contém as 4 permissões associadas a "BUYER_OPERATOR" ordenadas ordinalmente

  @CA-18.2 @RF-18 @RN-105 @RN-106 @MUST @critico @borda
  Esquema do Cenário: Resolução de contexto MACRO_GROUP com interseção de permissões e borda de conjunto vazio
    Dado que o utilizador é membro ativo com "can_view_group = true" nos tenants "TEN-1" e <tenant_2> do mesmo grupo
    E que no tenant "TEN-1" o utilizador possui "is_owner = true" e escopo "BUYER" ativo
    E que no tenant <tenant_2> o utilizador possui papel <papel_ten2> no escopo "BUYER"
    Quando o método "ResolveMacroGroupAsync" é invocado com lista de tenants ["TEN-1", <tenant_2>] e escopo "BUYER"
    Então o resultado tem "Ok = <sucesso>"
    E o campo "ErrorCode" é <codigo_erro>
    E as permissões do "MacroContext" contêm <permissoes>

    Exemplos:
      | tenant_2 | papel_ten2       | sucesso | codigo_erro   | permissoes                                                                            |
      | "TEN-2"  | "BUYER_OPERATOR" | true    | "Nenhum"      | "[\"demanda:criar\", \"demanda:listar\", \"leilao:participar\", \"pedido:visualizar\"]" |
      | "TEN-3"  | "Nenhum"         | false   | "ERR-CTX-003" | "[]"                                                                                  |

  @CA-18.3 @RF-18 @RN-105 @MUST @borda
  Esquema do Cenário: Validação de bordas na lista de tenants em MACRO_GROUP
    Quando o método "ResolveMacroGroupAsync" é invocado com lista de tenants contendo <quantidade> elementos
    Então o resultado tem "Ok = false"
    E o campo "ErrorCode" é "ERR-CTX-004"

    Exemplos:
      | quantidade |
      | 0          |
      | 51         |

  @CA-18.4 @RF-18 @RN-104 @MUST @critico @borda
  Cenário: Verificação de utilizador suspenso via CheckUserAsync
    Dado que o utilizador "01900000-0000-7000-8000-000000000001" possui status "SUSPENDED" na tabela "user_account"
    Quando o método "CheckUserAsync" é invocado com "tRef = null"
    Então o resultado tem "Ok = false"
    E o campo "ErrorCode" é "ERR-AUTH-004"

  @CA-18.5 @RF-18 @RN-107 @MUST @borda
  Cenário: Consulta de filiação a grupo via GetGroupMembershipAsync com bypass
    Dado que o utilizador é membro sem propriedade em um tenant do grupo "01900000-0000-7000-8000-000000000010"
    Quando o método "GetGroupMembershipAsync" é invocado
    Então a consulta executa via "ExecuteWithBypassAsync"
    E o valor retornado é "GroupMembershipKind.MEMBER"
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-18 | MUST | RN-100 a RN-107 | IF-09 (IF-09.1 a IF-09.8) | Nenhum | CA-18.1 a CA-18.5 | F-00 |

**Totais:** RF = 1 · RN = 8 · EP = 0 · EV = 0 · CA = 5 (`@critico` = 3) · Σw = 15

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-180 | Operações de `IAccessQueries` filtram dados explicitamente sem depender de `AllowedTenants` do contexto | Em fluxos de autenticação o token ainda não possui o tenant ativo definido (PRS-07) | PO, 2026-11-02 (D-23) |
| PRS-181 | Tenant inexistente em `CheckTenantAsync` ou `CheckMembershipAsync` retorna 403 `ERR-CTX-001` | Previne enumeração de recursos por chamadores não autorizados (PRS-58) | PO, 2026-11-02 (D-23) |
| PRS-182 | Donos de tenant exigem obrigatoriamente `can_view_group = true` para participar em visões macro | Garante conformidade com o modelo de consentimento federado multi-CNPJ (PRS-05) | PO, 2026-11-02 (D-23) |

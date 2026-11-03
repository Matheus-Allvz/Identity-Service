# 90 · Registo — Identity-Service

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** ORQ · **Estado:** Vivo · **Atualizado em:** 2026-11-02 (Gate 4 Aceite, UT-5-00 concluída, D-26)
> Fonte única do estado do projeto. Uma sessão nova do orquestrador começa por aqui. Atualizado em **cada** transição de UT. Entradas desta versão escritas por UT-1-01, UT-1-02, UT-1-90 e UT-2-01 (AR + ARQ), ciclos 1 a 5. Orquestração delegada ao Carcará (D-24).

## 1. Unidades de trabalho e fases
| UT / Fase | Papel | Depende de | Estado | Ciclo | Agente(s) | Score / Gate | Achados | Atualizado em |
|---|---|---|---|---|---|---|---|---|
| UT-1-01 Núcleo (`10`) | AR + ARQ | — | DeadLetter após 3 ciclos; aceite no ciclo 4 excecional (D-18) | 4 | Agente AR/ARQ | Gate 1 aceite no ciclo 4; Gate 2 aceite no ciclo 5 | A-01 a A-16; A-30 a A-54 (§7) | 2026-10-19 |
| UT-1-02 Sistemas externos (`20`) | AR | UT-1-01 | DeadLetter após 3 ciclos; aceite no ciclo 4 excecional (D-18) | 4 | Idem | Gate 1 aceite no ciclo 4; Gate 2 aceite no ciclo 5 | A-13, A-14, A-45, A-52 | 2026-10-19 |
| UT-1-90 Decomposição (`00` §2, este registo) | AR + ARQ | UT-1-01, UT-1-02 | DeadLetter após 3 ciclos; aceite no ciclo 4 excecional (D-18) | 4 | Idem | Gate 1 aceite no ciclo 4; Gate 2 aceite no ciclo 5 | A-02, A-03, A-41, A-43, A-44, A-51 | 2026-10-19 |
| UT-2-01 Dados (`30`) | AR + ARQ | UT-1-90 | DeadLetter após 3 ciclos; reaberta no ciclo 4 excecional (D-18) | 3 | Idem | Gate 2 reprovado nos ciclos 1, 2, 3 e 4 | A-30, A-33, A-37 | 2026-10-19 |
| UT-2-02 Contratos (`40`) | AR + ARQ | UT-2-01 | DeadLetter após 3 ciclos; reaberta no ciclo 4 excecional (D-18) | 3 | Outro agente; UT-2-90 | Gate 2 reprovado nos ciclos 1, 2, 3 e 4 | A-31, A-32, A-34, A-35, A-38 a A-40, A-47, A-49 | 2026-10-19 |
| UT-2-03 Ambiente de teste (`70`) | AUD | UT-2-01 | DeadLetter após 3 ciclos; reaberta no ciclo 4 excecional (D-18) | 3 | Outro agente; UT-2-90 | Gate 2 reprovado nos ciclos 1, 2, 3 e 4 | A-34, A-45, A-46, A-48, A-50 | 2026-10-19 |
| UT-2-90 Reconciliação (`00`, `10`, `20`, `30`, `40`, `70`, `90`, `evidencias/`) | AR + ARQ | UT-2-02, UT-2-03 | DeadLetter após 3 ciclos; reaberta no ciclo 4 excecional (D-18) | 3 | Agente AR/ARQ | Gate 2 reprovado nos ciclos 1, 2, 3 e 4 | §7 | 2026-10-19 |
| UT-2-91 Contratos, ciclo 4 (`10`, `40`, `70`) | AR + ARQ (subagente Claude novo) | D-18 | Aceite (ciclo 5, D-20) | 5 | — | Gate 2 aceite | A-60, A-61, A-64 (D-19), A-65, A-70, A-71, A-75 (`10`), A-76, A-77, A-78, A-79 (`40`) | 2026-10-19 |
| UT-2-92 Pacotes e fecho (`00`, `evidencias/medir-pacotes.py`) | AR + ARQ (Antigravity "Ipê") | D-18 | Aceite (ciclo 5, D-20) | 5 | — | Gate 2 aceite | A-62, A-63, A-67, A-72 | 2026-10-19 |
| UT-2-93 Medições do ambiente (`evidencias/env70/`, script de resultados) | AUD (Antigravity "Jatobá") | D-18 | Aceite (ciclo 5, D-20) | 5 | — | Gate 2 aceite | A-66; medição de A-61 através do EF Core | 2026-10-19 |
| UT-2-94 Dados e matriz (`30`, `20`, `evidencias/medir-postgres.sh`, `matriz-isolamento.sql`) | AR + ARQ (Antigravity "Sabiá") | UT-2-91 | Aceite (ciclo 5, D-20) | 5 | — | Gate 2 aceite | A-60 (casos novos da matriz), A-68, A-69, A-75 (`30`), A-79 (`30`) | 2026-10-19 |
| UT-2-95 Registo (`90`) | ORQ (Carcará) | D-18, D-24 | Aceite (ciclo 5, D-20) | 5 | — | Gate 2 aceite | A-73, A-74, A-80 (resolvido por D-15), A-130 | 2026-10-19 |
| UT-3-00 Rotas e bypass (`M-00`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Sabiá / Rev. Onda 4 | 12 261 B (12.0 KiB ≤ 12.0 KiB), Lint 0 | RF-15/17, RN-20..29, CA-15.1..3, CA-17.1..2 (5 CA), @critico=3, Σw=15, PRS-100..102 | 2026-11-02 |
| UT-3-01 Login (`M-01`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Ipê / Rev. Onda 4 | 12 165 B (11.9 KiB ≤ 12.0 KiB), Lint 0 | RF-01, RN-01..03, RN-30..34, CA-01.1..7 (7 CA), @critico=4, Σw=21, PRS-110..112 | 2026-11-02 |
| UT-3-02 Sessão e refresh (`M-02`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Jatobá / Rev. Onda 4 | 10 159 B (9.9 KiB ≤ 10.0 KiB), Lint 0 | RF-04, RN-11..12, RN-40..47, CA-04.1..8 (8 CA), @critico=4, Σw=24, PRS-120..122 | 2026-11-02 |
| UT-3-03 Troca de contexto (`M-03`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Antigravity / Rev. Onda 4 | 14 010 B (13.7 KiB ≤ 14.0 KiB), Lint 0 | RF-02/03, RN-50..57, CA-02.1..4, CA-03.1..4 (8 CA), @critico=4, Σw=24, PRS-130 | 2026-11-02 |
| UT-3-04 Criar tenant (`M-04`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Ipê / Rev. Onda 4 | 14 280 B (13.9 KiB ≤ 14.0 KiB), Lint 0 | RF-07, RN-60..66, CA-07.1..8 (8 CA), @critico=4, Σw=24, PRS-140..141 | 2026-11-02 |
| UT-3-05 Membros (`M-05`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Ipê / Rev. Onda 4 | 10 233 B (10.0 KiB ≤ 10.0 KiB), Lint 0 | RF-08, RN-70..75, CA-08.1..8 (8 CA), @critico=4, Σw=24, PRS-150..151 | 2026-11-02 |
| UT-3-06 Impersonation (`M-06`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Sabiá / Rev. Onda 4 | 13 564 B (13.2 KiB ≤ 13.5 KiB), Lint 0 | RF-06, RN-13..14, RN-80..87, CA-06.1..7 (7 CA), @critico=3, Σw=21, PRS-160..162 | 2026-11-02 |
| UT-3-07 Chaves (`M-07`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Jatobá / Rev. Onda 4 | 16 938 B (16.5 KiB ≤ 20.0 KiB), Lint 0 | RF-11, RN-90..99, CA-11.1..8 (8 CA), @critico=5, Σw=24, PRS-170..172 | 2026-11-02 |
| UT-3-08 Acessos (`M-08`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Sabiá / Rev. Onda 4 | 11 692 B (11.4 KiB ≤ 11.5 KiB), Lint 0 | RF-18, RN-100..107, CA-18.1..5 (5 CA), @critico=3, Σw=15, PRS-180..182 | 2026-11-02 |
| UT-3-09 Logout (`M-09`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Antigravity / Rev. Onda 4 | 19 720 B (19.3 KiB ≤ 20.0 KiB), Lint 0 | RF-05, RN-10, RN-110..116, CA-05.1..8 (8 CA), @critico=4, Σw=24, PRS-190 | 2026-11-02 |
| UT-3-10 Estado do utilizador (`M-10`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Jatobá / Rev. Onda 4 | 10 225 B (10.0 KiB ≤ 10.0 KiB), Lint 0 | RF-10/13, RN-120..129, CA-10.1..5, CA-13.1..3 (8 CA), @critico=3, Σw=24, PRS-200..202 | 2026-11-02 |
| UT-3-11 Esquema (`M-11`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Sabiá / Rev. Onda 4 | 6 730 B (6.6 KiB ≤ 7.0 KiB), Lint 0 | RF-19, RN-130..134, CA-19.1..6 (6 CA), @critico=2, Σw=18, PRS-210..212 | 2026-11-02 |
| UT-3-12 Estado do tenant (`M-12`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Sabiá / Rev. Onda 4 | 13 389 B (13.1 KiB ≤ 14.0 KiB), Lint 0 | RF-09/12, RN-140..145, CA-09.1..4, CA-12.1..3 (7 CA), @critico=3, Σw=21, PRS-300..302 | 2026-11-02 |
| UT-3-13 Isolamento (`M-13`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Antigravity / Rev. Onda 4 | 16 373 B (16.0 KiB ≤ 16.0 KiB), Lint 0 | RF-16, RN-150..157, CA-16.1..8 (8 CA), @critico=4, Σw=24, PRS-310 | 2026-11-02 |
| UT-3-14 Eventos (`M-14`) | AR | Gate 2 | Aceite (Gate 4) | 2 | Jatobá / Rev. Onda 4 | 17 462 B (17.1 KiB ≤ 17.5 KiB), Lint 0 | RF-14, RN-160..169, CA-14.1..8 (8 CA), @critico=4, Σw=24, PRS-320..322 | 2026-11-02 |
| UT-4-00 a UT-4-14, UT-4-90, UT-4-91 | RI + VAL | UT-3-00 a UT-3-14 | Aceite (Gate 4) | 1 | Subagentes Gemini 3.8 High/Medium | Gate 4 aceite (15/15 pacotes ≤ 59.7 KiB, fecho OK, Lint 0) | 109 CAs (100% @borda), Σw=327, 0 colisões de ID | 2026-11-02 |
| UT-5-00 Plano de fases (`60`) | ARQ | Gate 4 | Aceite | 1 | Subagente Gemini 3.8 High | 4 fases (F-00 a F-03), 109 CAs, Σw=327, Lint 0 | Alinhamento ecossistema B2B (`Docs/Alinhamento-Ecossistema-B2B.md`) | 2026-11-02 |
| UT-5-01 Suite de aceitação (`tests/IdentityService.Acceptance`) | AUD | Gate 4 | Aceite | 1 | Subagentes Gemini 3.8 High (`AUD-A` + `AUD-B`) | 109 CAs + bordas (F-00..F-03), Build 0 avisos/0 erros | Suite isolada fora do worktree dos construtores (`scratch/ut-5-01-acceptance`) | 2026-11-02 |
| F-00 Fundação técnica (`M-00, M-07, M-08, M-11, M-13, M-14`) | CON + VAL | UT-5-00 | Aceite | 1 | `CON-F00` (`bda7e68c`) + `VAL-F00` (`c1da7fe7`) | G1–G6 Aprovados; $C_w^{(1)} = 95.0\%$ (38/40); $C_w^{\text{final}} = 100.0\%$ (40/40); `@critico` 17/17 | A-131 resolvido em `2dfbc3e`; branch `feat/ma/f00-fundacao-tecnica` | 2026-11-02 |
| F-01 Autenticação, sessão e contexto (`M-01, M-02, M-03, M-06, M-09`) | CON + VAL + REV | F-00 | Aceite | 2 | `CON-F01` (`9db7030b`), `VAL-F01` (`7033d7ad`, `ecc62485`), `REV-F01` (`142ec3d1`) | G1–G6 Aprovados; $C_w^{(1)} = 97.37\%$ (37/38); $C_w^{\text{final}} = 100.0\%$ (38/38); `@critico` 19/19 | A-132 (`CA-03.4` TOCTOU `jti`) resolvido em `88b9d39`; branch `feat/ma/f01-autenticacao-sessao-contexto` | 2026-11-02 |
| F-02 Tenants e membros (`M-04, M-05`) | CON | F-01 | Em execução | 1 | Subagente Gemini 3.8 High (`CON-F02`) | — | 16 CAs (Σw=48), branch `feat/ma/f02-tenants-e-membros` | 2026-11-02 |
| F-03 Governança de estado (`M-10, M-12`) | CON | F-01, F-02 | Pronto para execução | — | — | — | 15 CAs (Σw=45) | 2026-11-02 |
| UT-5-90 Integração e auditoria final | AUD | F-00 a F-03, UT-5-01 | Pronto para execução | — | — | — | — | 2026-11-02 |

> **Nota sobre UT-2-9x / Gate 2 ciclo 5 (Ameaça à validade, D-24):** O validador do ciclo 5 passa de Claude para Gemini (subagente Antigravity novo) por esgotamento de quota do Claude Code, autorizado pelo PO na decisão D-24. Registado como ameaça à validade da fundação. Observação para o E4: o validador Gemini identificou apenas 1 achado editorial (A-130), contra 16–25 achados dos ciclos anteriores em Claude. Como não é possível distinguir se decorre da limpeza prévia do ciclo 5 ou de menor rigor do modelo, reforça-se a obrigatoriedade da revisão cega cruzada (Onda 4) por modelos Claude dos módulos elaborados por agentes Gemini.

**Entradas para as UT-3** (o brief de cada UT inclui a sua linha):
| Para | Entrada |
|---|---|
| UT-3-00 | Rotas e bypass: `40` §1.1. Classificação de exceções pela cadeia de `InnerException`: `40` §1.2 (PRS-65, PRS-70). Chave lida antes da RG-06, também sem token (PRS-71). CA de 503 com os mecanismos de falha: `ISigningKey` substituída por uma que lança `CryptographicException`; contentor do PostgreSQL ou do Redis parado. O tratador global DEVE registar no log JSON cada exceção que resulta em 500 ou 503, com o `correlation_id` no scope, antes de escrever a resposta, sem os campos que o logging proíbe |
| UT-3-01 | Normalização e formato do `email` e classes da `password`: linha EP-01. D-08: e-mail inexistente, palavra-passe errada e conta `LOCKED` respondem 401 `ERR-AUTH-001`, cada um com uma verificação Argon2id; `SUSPENDED` só com palavra-passe correta → 403 `ERR-AUTH-004`. Conta `LOCKED` com `locked_until ≤ agora`: passa a `ACTIVE`, `failed_login_attempts = 0`, `locked_until = NULL` antes da verificação da palavra-passe, na mesma transação. Palavra-passe errada em conta `ACTIVE`: `failed_login_attempts` + 1 e auditoria `LOGIN_FAILED`; ao chegar a 5 (PRS-31): `status = LOCKED`, `locked_until = agora + 900 s`, auditoria `USER_LOCKED` em vez de `LOGIN_FAILED`, `denylist:user` (IF-04.2), famílias revogadas (IF-01.4) e EV-03 (IF-07.3) (D-09). E-mail inexistente, conta `LOCKED` com `locked_until > agora` e conta `SUSPENDED` com palavra-passe errada: auditoria `LOGIN_FAILED`, sem incremento (PRS-83). Sucesso: `failed_login_attempts = 0`, `locked_until = NULL`, `updated_at = agora`, auditoria `LOGIN_SUCCESS`. Todas as escritas na BD de um pedido numa só transação |
| UT-3-02 | Ordem do EP-03: (1) sem linha com o `token_hash` → 401 `ERR-AUTH-005`, sem efeitos; linha `CONSUMED` (expirada ou não) → reuso (2); (2) reuso, numa transação: IF-01.3 da família, IF-04.2 do `user_id`, auditoria `TOKEN_REUSE_DETECTED`, IF-07.5 (`reused_refresh_token_id` = `id` da linha apresentada, `ip_address` = `IpAddress` do pedido, `detected_at` = agora; contexto da linha `ACTIVE` da família ou, sem ela, da linha apresentada) → 401 `ERR-AUTH-006`; (3) revalidação do contexto guardado (D-19): IF-09.1 com o `created_at` da linha; `SINGLE`: IF-09.2, IF-09.3, IF-09.4; `MACRO_GROUP`: IF-09.5; `NONE`: nenhuma, `roles`/`permissions` = `[]`. Qualquer falha: IF-01.3 da família, commit e o código devolvido (PRS-84); (4) linha `REVOKED` ou `expires_at ≤ agora` → 401 `ERR-AUTH-005`, sem efeitos; (5) sucesso: UPDATE da linha apresentada para `CONSUMED` (`consumed_at` = agora) condicionado a `status = 'ACTIVE'`, antes de inserir o filho `ACTIVE` com o mesmo `family_id`, `user_id` e contexto e `expires_at = created_at + 604800 s`; depois `replaced_by_id` = `id` do filho; auditoria `TOKEN_REFRESHED`; `token_type` = `"Bearer"`. (6) Rotação concorrente: o pedido cujo UPDATE de (5) afeta 0 linhas ou que recebe 23505 em `uq_refresh_token_family_active` desfaz a sua transação e aplica (2) numa transação nova (401 `ERR-AUTH-006`, família toda `REVOKED`) |
| UT-3-03 | Ordem da camada 6 do EP-02: IF-09.1 com o `iat` do token; `SINGLE`: IF-09.8 (`null` → 403 `ERR-CTX-001`; `group_id` = `GroupId` devolvido), depois IF-09.2, IF-09.3, IF-09.4; `MACRO_GROUP`: IF-09.5 (ordem fixa na sua linha); depois IF-01.2 com o `fid` (`false` → 401 `ERR-AUTH-002`, PRS-73), auditoria `CONTEXT_SWITCHED`, IF-04.1 com `jti` e `exp` do token de entrada e emissão do token novo com o mesmo `fid`. Uma rejeição não escreve na BD nem no Redis e o token de entrada continua válido. `token_type` = `"Bearer"`. Em `MACRO_GROUP`, `roles` PODE ser `[]` com `permissions` não vazia (RG-13). Literais de `roles` e `permissions` nos CA: só papéis e permissões do seed, pela RG-01 (o dono recebe todas as `role` do escopo) e pela RG-12 |
| UT-3-04 | D-04: o criador `TENANT_ADMIN` fica membro do tenant novo pela IF-12.1 (`is_owner = false`, `can_view_group = false`, papel `GROUP_MANAGER`; PRS-66, PRS-67). Camada 6 do EP-06, por esta ordem: grupo `SUSPENDED` → 403 `ERR-CTX-002`; `cnpj` já existente → 409 `ERR-CTX-005`. Sucesso numa única transação: `tenant` `ACTIVE`; uma linha `tenant_scope` com `is_active = true` por escopo (incluindo o `TENANT_ADMIN` acrescentado); vínculo do criador; auditoria; EV-01 (IF-07.1). Uma rejeição ou falha não deixa nenhuma destas linhas |
| UT-3-05 | Dono da IF-12. IF-12.1: vínculo do criador; exige o tenant no conjunto de escrita. IF-12.2: dentro de `ExecuteWithBypassAsync` (IF-11.3, PRS-72), só vínculos com `is_owner = false` (D-22). Camada 6 do EP-07, por esta ordem: já existe `tenant_member` com o par (`tenant_id` do path, `user_id`), em qualquer `status` → 409 `ERR-CTX-005`; algum papel de `role_ids` com `role.scope` sem `tenant_scope` ativo nesse tenant → 422 `ERR-CTX-006`, também com `is_owner = true`. Sucesso numa única transação: `tenant_member` `ACTIVE` com `is_owner` e `can_view_group` do corpo, uma `member_role` por papel com o `tenant_id` do path, auditoria `MEMBER_ADDED`; uma rejeição não deixa linhas |
| UT-3-06 | Camada 6 do EP-05, por esta ordem: (1) alvo com `platform_role ≠ NONE` ou alvo = `sub` → 403 `ERR-AUTH-010`; (2) RG-05 do alvo (qualquer `denylist:user`, PRS-46) ou alvo `LOCKED` com `locked_until > agora` → 403 `ERR-AUTH-004` (`LOCKED` expirado passa); (3) RG-04 do tenant alvo → 403 `ERR-CTX-002`; (4) RG-03 → 403 `ERR-CTX-001`; (5) RG-02 para `target_scope` → 403 `ERR-CTX-003`. Existência e grupo do tenant alvo: IF-09.8 (`null` → 404 `ERR-CTX-008`). O alvo lê-se em `user_account`. Token: `sub` = alvo, `tenant_id` = tenant alvo, `group_id` = grupo dele, `allowed_tenants` = [tenant alvo], `active_scope` = `target_scope`, `roles`/`permissions` da RG-01; `impersonation_context` repete-os; `token_type` = `"Bearer"`. Auditoria `IMPERSONATION_STARTED` confirmada antes do 200. Não idempotente: cada sucesso gera `jti` e auditoria novos |
| UT-3-07 | Chave indisponível → 503 `ERR-SYS-002` (CR-11). `keys` do EP-10 com exatamente um elemento; a mesma chave assina o `access_token` e o `context_jws`; `n` e `e` em base64url sem padding (RFC 7518) |
| UT-3-08 | IF-09.5 pela ordem da sua linha. IF-09.7 devolve `GroupMembershipKind` (D-22) e corre em `ExecuteWithBypassAsync` (IF-11.3, PRS-72). As operações da IF-09, salvo IF-09.7, filtram explicitamente pelo `userId` e pelos tenants ou grupo recebidos; não dependem de `AllowedTenants` do pedido (nos fluxos de autenticação o filtro está desligado e o token ainda não tem o tenant pedido, PRS-07). Tenant inexistente em IF-09.2, IF-09.3 ou IF-09.4 → `ERR-CTX-001` (PRS-58). IF-09.4 aplica a RG-01 só a vínculo `ACTIVE`; sem ele `roles` vazio → RG-02 |
| UT-3-09 | A revogação da família `fid` (IF-01.3) e a auditoria `LOGOUT` gravam-se na mesma transação; `denylist:jti:{jti}` (IF-04.1) fica gravada antes de o 204 ser enviado |
| UT-3-10 | D-07: `PLATFORM_ADMIN` → suspensão global (conta, IF-02.1); `TENANT_ADMIN` → só vínculos não-dono do alvo no grupo (IF-12.2), com IF-01.6 sobre os `tenant_id` devolvidos (PRS-86), sem `denylist:user` nem EV-03 (PRS-68, PRS-69). C9 com D-22: alvo ≠ `sub` e IF-09.7 = `MEMBER`, em `SUSPENDED` e em `ACTIVE` (PRS-81). Todos os efeitos na BD numa só transação. Cada 200, incluindo a repetição do estado atual, insere 1 auditoria `USER_STATUS_CHANGED`. `GLOBAL` + `SUSPENDED` repetido repete IF-01.4, `denylist:user` e EV-03. Com `ACTIVE` nunca revoga famílias nem enfileira EV-03. Um único instante `TimeProvider` por pedido, truncado ao segundo, para `updated_at` da resposta (também em `GROUP`, PRS-82) e `blockedAt` do EV-03 |
| UT-3-12 | `reason` com 5 a 255 caracteres após `trim()` (PRS-37); escrita de `tenant.status` só pela IF-10.1. Uma única transação: IF-10.1, IF-01.5 (só `SUSPENDED`), auditoria `TENANT_STATUS_CHANGED`, EV-02 (IF-07.2, só `SUSPENDED`); um erro não deixa nada alterado. `ACTIVE`: sem revogação nem outbox; `DEL denylist:tenant` só na transição real (IF-04.4). Pedido com o estado atual: 200 e repete os efeitos do estado pedido (`SUSPENDED`: revogação, `SET denylist:tenant`, auditoria, EV-02 novo; `ACTIVE`: só auditoria). `blocked_at` do EV-02 e `updated_at` da resposta: `UpdatedAt` devolvido pela IF-10.1 |
| UT-3-13 | RNF-06 (um CA `@critico` por camada); matriz de isolamento de `30` §5.1 como oráculo; ciclos de vida da IF-11. IF-11.3: CA de reposição com exceção (incluindo transação abortada) e de ligação seguinte do pool sem bypass. Testes das camadas 2 a 4 com os dois valores de `BypassTenantFilter` |
| UT-3-14 | `MessageTimeoutMs = 3000` e `Acks = All` (PRS-63); RNF-05. Worker: a cada `OUTBOX_POLLING_INTERVAL_MS` ms (500 por omissão) lê até 50 mensagens `PENDING` por ordem de `created_at` e `id` e tenta publicar cada uma; cada falha soma 1 a `retry_count`; à 5.ª falha passa a `FAILED` (PRS-43) |

## 2. Pressupostos (PRS)

### 2.1 PRS globais (`10` §9)
| ID | Pressuposto | Justificação | Itens afetados | Estado |
|---|---|---|---|---|
| PRS-01 | Login emite `view_mode = NONE` | Força a troca explícita (ADR-04) | `10` §3.5, M-01 | Aprovado (D-12) |
| PRS-02 | Contexto ativo no `refresh_token` `ACTIVE` | O refresh não perde o contexto | ENT-08, M-02, M-03 | Aprovado (D-12) |
| PRS-03 | RBAC por escopo; dono com todas as `role` do escopo | Formato de claims da Ideia Base §5.2. No `userservice` o dono não tem lista de permissões restrita [CÓDIGO] `userservice-api/src/Actuar.UserService.Application/AppServices/UserProfileAppService.cs:97` e as permissões são um bitmask [CÓDIGO] `userservice-api/src/Actuar.UserService.Domain/Entities/Role.cs:11`, não adotado | RG-01, ENT-06 | Aprovado (D-12) |
| PRS-04 | JWT RS256 + JWKS + denylist; sem Phantom Token | O `userservice` emite JWT diretamente ao cliente [CÓDIGO] `userservice-api/src/Actuar.UserService.Application/Extensions/TokenExtensions.cs:30`, com chave simétrica HMAC [CÓDIGO] `TokenExtensions.cs:39-40`, que esta spec proíbe | ADR-06 | Aprovado (D-12) |
| PRS-06 | Semântica da denylist | Revogação em `SINGLE` e `MACRO_GROUP` | `30` §4 | Aprovado (D-12) |
| PRS-07 | Só 3 tabelas `ITenantScoped`; bypass nos fluxos de autenticação | Arranque do contexto; o `userservice` injeta `IAuthContext` no `DbContext` [CÓDIGO] `userservice-api/src/Actuar.UserService.Repository/Context/UserServiceContext.cs:11` | ADR-05, `30` §5 | Aprovado (D-12) |
| PRS-10 | Self-dealing fora do âmbito | Ideia Base §8 | `10` §2.2 | Aprovado (D-12) |
| PRS-11 | Normalização de `email` e `cnpj` | Evita duplicados lógicos | ENT-02, ENT-04 | Aprovado (D-12) |
| PRS-12 | TTL `denylist:tenant` = 900 s | Ideia Base §6.2.1; tokens novos já são barrados pela BD (RG-04) | `30` §4 | Aprovado (D-05) |
| PRS-13 | `user_account` e `economic_group` criados fora | A Ideia Base não descreve estes fluxos | `10` §2.2 | Aprovado (D-03) |
| PRS-14 | Sem consumo de tópicos nem `KafkaConsumerFilter` | Sem consumidor, o filtro não tem comportamento verificável | `10` §2.2 | Aprovado (D-03) |
| PRS-15 | `SET` antes do commit; `DEL denylist:tenant` depois e só em transição real | Falha a meio deixa o sistema mais restritivo | `10` §3.9 | Aprovado (D-06) |
| PRS-16 | Dependência indisponível → 503 | A revogação não pode ser ignorada | `10` §6, §7 | Aprovado (D-06) |
| PRS-17 | RLS por `SET ROLE` + `set_config` | [MEDIDO] `20` EXT-01 n.º 1 e 4 | `10` §3.9, `30` §5 | Aprovado (D-10) |
| PRS-18 | `acting_scope` `NONE` (ENUM-10) | `CONTROL_PLANE` estava fora de ENUM-03 | ENUM-10, M-14 | Aprovado (D-10) |
| PRS-19 | Sem remoção física; auditoria só `INSERT` | `SET NULL` contradizia o append-only | `30` | Aprovado (D-10) |
| PRS-20 | `access_token` de 900 s | Ideia Base §6.2: 5 a 15 min | `10` §3.5 | Aprovado (D-10) |
| PRS-21 | Parâmetros Argon2id | Não fixados na Ideia Base | RNF-01 | Aprovado (D-10) |
| PRS-22 | Coordenadas `NUMERIC(9,6)` | Ideia Base usa `POINT`; evita PostGIS | ENT-02 | Aprovado (D-10) |
| PRS-23 | Corpo máximo 65 536 bytes | Nenhum pedido válido excede 4 KB | `10` §3.4 | Aprovado (D-10) |
| PRS-24 | Arrays de saída em ordem ordinal | Determinismo | `10` §3.3 | Aprovado (D-10) |
| PRS-25 | Uma chave RSA; `kid` = `JWT_KEY_ID` | Rotação não descrita | `10` §3.7 | Aprovado (D-10) |
| PRS-26 | `detail` = `title` | Determinismo | `10` §3.4 | Aprovado (D-10) |
| PRS-27 | `TEXT[]`/`UUID[]` | Mapeamento nativo do Npgsql | ENT-06, ENT-08 | Aprovado (D-10) |
| PRS-28 | Contextos sem escopo omitidos; ordem | A v1.0 não fixava | RG-10 | Aprovado (D-10) |
| PRS-35 | `MACRO_GROUP` com 1 a 50 tenants | Conteúdo do PRS-05 da v1.0 | RG-11, ENT-08 | Aprovado (D-10) |
| PRS-46 | `T_ref` da RG-05: EP-01 só estado; EP-03 `created_at`; alvo de EP-05 rejeitado por qualquer chave | A v1.0 bloqueava o login 900 s após reuso, contra a Ideia Base §6.3 ("forçando reautenticação") | RG-05 | Aprovado (D-11) |
| PRS-47 | Camada 3 sem preenchimento automático de `TenantId` | A Ideia Base §7.2 preenche no modo `SINGLE`; aqui o `TenantId` vem sempre do path ou do tenant novo, e um valor implícito esconderia erros | `10` §3.9 | Aprovado (D-10) |
| PRS-48 | `denylist:user` nunca apagada | Tokens emitidos depois têm `iat > V`; apagar reabriria tokens antigos | `30` §4, IF-04 | Aprovado (D-10) |
| PRS-49 | `SUSPENDED` em `economic_group` só por carga externa; em `tenant_member` pelo EP-09 com `TENANT_ADMIN` (D-07) | `economic_group` só por carga externa; `tenant_member` passa a `SUSPENDED` pelo EP-09 com `TENANT_ADMIN` (D-07, IF-12.2) | ENUM-01 | Aprovado (D-10); `tenant_member` revisto por D-07 |
| PRS-50 | Limiares do RNF-04 | Valores da v1.0 sem base na fonte; RNF fora da métrica | RNF-04 | Aprovado (D-10) |
| PRS-51 | Conjunto de escrita por `AuthorizeTenantWrite` | Sem ele, EP-06 e EP-07 eram rejeitados pela camada 3 e pela RLS [MEDIDO] `20` EXT-01 n.º 9 | `10` §3.9, `30` §5 | Aprovado (D-10) |
| PRS-52 | Aceitação de `X-Correlation-Id` (formato `D`) e de IP (`IPAddress.TryParse`) | Um valor arbitrário com mais de 45 caracteres causava erro 500 ao gravar `ip_address` | `10` §3.9 | Aprovado (D-10) |
| PRS-53 | Autenticação (camada 2) antes do formato (camada 3) (ex-PRS-46 de `40`) | Desvia-se da ordem do E1, atividade 2.8; um chamador sem token não obtém informação sobre o corpo | `40` §2 | Aprovado (D-10, D-13) |
| PRS-54 | Headers de pedido decodificados como Latin-1 (ex-PRS-47 de `40`) | Resposta RFC 9457 também com headers não ASCII | `40` §3 R10 | Aprovado (D-10) |
| PRS-55 | Query string ignorada (ex-PRS-48 de `40`) | Nenhum EP define query | `40` §1 | Aprovado (D-10) |
| PRS-56 | Header Kafka sem valor fica ausente (ex-PRS-49 de `40`) | Avaliação #11 | `40` §6 | Aprovado (D-10) |
| PRS-57 | Combinações de campos de EP-02; repetidos em `scopes`/`role_ids` → 400 (ex-PRS-50 de `40`) | Avaliação #10 | `40` §4.1 | Aprovado (D-10) |
| PRS-58 | EP-02 sem 404 (ex-PRS-51 de `40`) | Avaliação #9 | `40` §4.1 | Aprovado (D-10) |
| PRS-59 | `impersonation_context` com `roles` e `permissions` (ex-PRS-52 de `40`) | Avaliação #10 | `40` §4.1 | Aprovado (D-10) |
| PRS-60 | `reason` de EV-03 no bloqueio por login; `actor_user_id` de EV-03 em EP-09 (ex-PRS-53 de `40`) | Exemplo da v1.0 §6.3 | `40` §4.2 | Aprovado (D-10) |
| PRS-90 | Suite em processo com Kestrel, `Testing` e Testcontainers | `70` §1 | `70` | Aprovado (D-10) |
| PRS-91 | Chave de teste por sessão; tokens pelo fluxo real; cunhagem por payload literal | `70` §3 | `70` | Aprovado (D-10) |
| PRS-92 | Relógio de teste substituível; tolerância de 2 s nos TTL do Redis | `70` §3 | `70` | Aprovado (D-10) |
| PRS-93 | Dados fictícios e varredura obrigatória | `70` §1 | `70` | Aprovado (D-10) |
| PRS-94 | Falhas por paragem ou pausa de contentor; 503 em ≤ 15 s | `70` §4 | `70` | Aprovado (D-10) |
| PRS-95 | Timeouts e sondagem da suite | `70` §5 | `70` | Aprovado (D-10) |
| PRS-96 | `docker-compose.yml` fixo de `70` §2 | `70` §2; `10` §3.11 | `70`, M-11 | Aprovado (D-10) |
| PRS-61 | `denylist:user` rejeita `iat ≤ V` (a Ideia Base §6.2 diz "menor") | Rejeita também tokens emitidos no mesmo segundo do bloqueio; os testes avançam o relógio 1 s (`70` §3) | RG-05, RG-07 | Aprovado (D-16) |
| PRS-62 | Sem CA de "processo morto a meio" e de "resposta perdida do broker" | Exigem injeção de falhas dentro do processo; o comportamento decorre da atomicidade da transação (nada fica a meio na BD; um `SET` na denylist antes de um commit falhado só restringe) e da entrega at-least-once com deduplicação por `event_id` | `70` §3, §4 | Aprovado (D-16) |
| PRS-63 | Produtor Kafka com `MessageTimeoutMs = 3000` e `Acks = All` | O default de 300 s excede o RNF-05 e os tetos da suite [MEDIDO] `Medicoes.L03_Kafka` | `10` §3.7, M-14 | Aprovado (D-16) |
| PRS-64 | Ordem de avaliação de `MACRO_GROUP` em `40` IF-09.5 | A ordem não está na Ideia Base nem na v1.0 | M-03, M-08 | Aprovado (D-16) |
| PRS-65 | Classificação das exceções de BD, Redis e chave (`40` §1) | 42501 e 23505 não são falhas de ligação | M-00 | Aprovado (D-16) |
| PRS-66 | Vínculo do criador (D-04) com `can_view_group = false` | Privilégio mínimo; o PO não decidiu a visão de grupo | M-04, M-05 | Aprovado (D-16) |
| PRS-67 | EP-06 por `TENANT_ADMIN` acrescenta o escopo `TENANT_ADMIN` ao tenant novo se faltar | Sem ele o papel `GROUP_MANAGER` do criador violaria o RN-17 (papel de escopo inativo) | M-04 | Aprovado (D-16) |
| PRS-68 | Suspensão de vínculos (D-07): tokens de acesso já emitidos valem até expirar (≤ 900 s); famílias com contexto nesses tenants revogadas (IF-01.6) | Não há chave de denylist por vínculo | M-10 | Aprovado (D-16) |
| PRS-69 | Resposta de EP-09 com `scope` (`GLOBAL`, `GROUP`) e `group_id` | Os dois efeitos de D-07 têm de ser distinguíveis | M-10 | Aprovado (D-16) |
| PRS-97 | CA de concorrência com `Barrier`, oráculo para qualquer intercalação e 20 repetições | E1 §3.5 | `70` §3 | Aprovado (D-16) |
| PRS-70 | Estratégia de execução por omissão do Npgsql EF (sem `EnableRetryOnFailure`); classificação de exceções pela cadeia de `InnerException`; `CryptographicException` = chave indisponível | A-61 | `40` §1.2, M-00 | Aprovado (D-23) |
| PRS-71 | A chave de assinatura é lida antes da RG-06: pedido sem token com chave indisponível → 503 | A-71 | `10` §3.4, M-00 | Aprovado (D-23) |
| PRS-72 | Bypass limitado da IF-11.3 `ExecuteWithBypassAsync`, só para IF-09.7 e IF-12.2 | A-60 | `40` IF-11.3, M-05, M-08, M-13 | Aprovado (D-23) |
| PRS-73 | EP-02 com família sem linha `ACTIVE` → 401 `ERR-AUTH-002`, sem efeitos | A-70 | `40` EP-02, M-03 | Aprovado (D-23) |
| PRS-80 | Formato de e-mail (RFC 5322) verificado por `MailAddress.TryCreate` com `Address` igual ao texto após `trim()` | Ciclo 5 (UT-2-91) | `10` §9 | Aprovado (D-26) |
| PRS-81 | D-22 aplica-se também à reativação: `TENANT_ADMIN` não muda vínculos próprios nem de quem é dono no grupo, em `SUSPENDED` e em `ACTIVE` | Ciclo 5 (UT-2-91) | `10` §9 | Aprovado (D-26) |
| PRS-82 | EP-09 com `scope = GROUP` devolve `updated_at` = instante da operação, também sem vínculos alterados | Ciclo 5 (UT-2-91) | `10` §9 | Aprovado (D-26) |
| PRS-83 | Conta `SUSPENDED` com palavra-passe errada: 401 e auditoria `LOGIN_FAILED`, sem somar a `failed_login_attempts` | Ciclo 5 (UT-2-91) | `10` §9 | Aprovado (D-26) |
| PRS-84 | EP-03: qualquer falha de revalidação (RG-02 a RG-05, RG-11, RG-13) revoga a família; rotação concorrente (0 linhas ou 23505) tratada como reuso, 401 `ERR-AUTH-006` | Ciclo 5 (UT-2-91) | `10` §9 | Aprovado (D-26) |
| PRS-86 | EP-09 por `TENANT_ADMIN`: revogação de famílias só nos tenants cujos vínculos mudaram; a repetição de `SUSPENDED` não revoga | Ciclo 5 (UT-2-91) | `10` §9 | Aprovado (D-26) |

### 2.2 PRS de um só módulo (o agente da UT-3 copia-os para o módulo com o mesmo ID)
| ID | Pressuposto | Módulo | Estado |
|---|---|---|---|
| PRS-05 | `MACRO_GROUP` tudo-ou-nada; `can_view_group` exigido a donos; interseção | M-08, M-03 | Aprovado (D-12) |
| PRS-08 | Impersonation: só `SUPPORT_AGENT`/`PLATFORM_ADMIN`, alvo `NONE`, `SINGLE`, sem refresh, sem cadeia, `ticket_id` com regex, 900 s | M-06 | Aprovado (D-12) |
| PRS-09 | Claims de `context_jws`, `exp = iat + 300` | M-14 | Aprovado (D-12) |
| PRS-30 | Palavra-passe 12 a 128 caracteres com pelo menos uma de `[A-Z]`, `[a-z]`, `[0-9]` e um carácter especial (qualquer carácter fora de `[A-Za-z0-9]`); sem `trim()` | M-01 | Aprovado (D-10) |
| PRS-31 | Bloqueio após 5 falhas durante 900 s | M-01 | Aprovado (D-10) |
| PRS-32 | O bloqueio grava `denylist:user`, revoga todas as famílias do utilizador (IF-01.4) e publica EV-03 (D-09) | M-01 | Aprovado (D-09; revisto) |
| PRS-33 | `refresh_token` válido 604 800 s | M-02 | Aprovado (D-10) |
| PRS-34 | `refresh_token` = 32 bytes aleatórios em base64url (43 caracteres); SHA-256 hex | M-02 | Aprovado (D-10) |
| PRS-36 | EP-06 e EP-08 (forma, campos, autorização) | M-04 (EP-06), M-12 (EP-08) | Aprovado (D-10) |
| PRS-37 | CNPJ módulo 11 e rejeição de dígitos iguais; `legal_name` 2 a 200; `reason` 5 a 255 | M-04 (CNPJ, `legal_name`), M-12 e M-10 (`reason`) | Aprovado (D-10) |
| PRS-38 | EP-07 (forma, campos, autorização); `role_ids` 1 a 10 distintos | M-05 | Aprovado (D-10) |
| PRS-39 | EP-09 (forma, campos, autorização); reativação também desbloqueia `LOCKED` | M-10 | Aprovado (D-10); EP-09 revisto por D-07 |
| PRS-40 | `ticket_id` `^[A-Z]{2,10}-[0-9]{1,10}$` | M-06 | Aprovado (D-10) |
| PRS-41 | Token de impersonation com 900 s | M-06 | Aprovado (D-10) |
| PRS-42 | Nomes dos tópicos EV-01 a EV-04 | M-14 | Aprovado (D-10) |
| PRS-43 | Outbox: 500 ms, lotes de 50, 5 tentativas, ≤ 3 s | M-14 | Aprovado (D-10) |
| PRS-44 | JWKS com `Cache-Control: public, max-age=300` | M-07 | Aprovado (D-10) |
| PRS-45 | O refresh recalcula `roles`/`permissions` pela IF-09 | M-02 | Aprovado (D-10) |
| PRS-100 | Resolução de `ISigningKey` ocorre antes da inspeção de `Authorization` em todas as rotas autenticadas | M-00 | Aprovado (D-23) |
| PRS-101 | Inspeção de `InnerException` pelo tratador global não utiliza `EnableRetryOnFailure` | M-00 | Aprovado (D-23) |
| PRS-102 | Comparação de `denylist:user` com `iat` utiliza operador menor ou igual | M-00 | Aprovado (D-16) |
| PRS-110 | Validação de senha aplica 4 classes sem trim | M-01 | Aprovado (D-10) |
| PRS-111 | Resposta 401 para conta LOCKED mantém tempo constante | M-01 | Aprovado (D-08) |
| PRS-112 | EV-03 e revogação de famílias ocorrem no 5º erro | M-01 | Aprovado (D-09) |
| PRS-120 | Refresh token gerado com 32 bytes em base64url (43 chars) | M-02 | Aprovado (D-10) |
| PRS-121 | EV-04 prioriza linha ACTIVE da família no reuso | M-02 | Aprovado (D-19) |
| PRS-122 | Índice `uq_refresh_token_family_active` previne duplicidade ativa | M-02 | Aprovado (D-19) |
| PRS-130 | `IF-04.1 RevokeJtiAsync` executado antes do commit BD, permitindo rollback em falha | M-03 | Aprovado (D-06, PRS-15) |
| PRS-140 | Validação módulo 11 de CNPJ DEVE rejeitar 14 dígitos idênticos antes dos restos | M-04 | Aprovado (D-10) |
| PRS-141 | Interface `ITenantStatusWriter` (IF-10.1) pertence a M-04 como dono de `tenant` | M-04 | Aprovado (D-10) |
| PRS-150 | Validação de `role_ids` DEVE rejeitar pedidos com mais de 10 elementos | M-05 | Aprovado (D-10) |
| PRS-151 | Operações via IF-12.2 NUNCA alteram membros com `is_owner = true` | M-05 | Aprovado (D-22) |
| PRS-160 | EP-05 bem-sucedido gera novo `jti` e nova auditoria | M-06 | Aprovado (D-10) |
| PRS-161 | Bloqueio `LOCKED` no alvo com `locked_until ≤ agora` expira | M-06 | Aprovado (D-10) |
| PRS-162 | Leitura de `user_account` usa `BypassTenantFilter = true` | M-06 | Aprovado (D-10) |
| PRS-170 | O endpoint `GET /.well-known/jwks.json` ignora cabeçalhos de autenticação sem rejeição | M-07 | Aprovado (D-10) |
| PRS-171 | O método `ITokenIssuer.Issue` opera em memória de processo sem chamadas síncronas a base de dados ou Redis | M-07 | Aprovado (D-10) |
| PRS-172 | A chave RSA de `ISigningKey` possui tamanho fixo de 2048 bits com expoente público `AQAB` (65537) | M-07 | Aprovado (D-10) |
| PRS-180 | Operações de `IAccessQueries` filtram dados explicitamente sem depender de `AllowedTenants` do contexto | M-08 | Aprovado (D-10) |
| PRS-181 | Tenant inexistente em `CheckTenantAsync` ou `CheckMembershipAsync` retorna 403 `ERR-CTX-001` | M-08 | Aprovado (D-10) |
| PRS-182 | Donos de tenant exigem obrigatoriamente `can_view_group = true` para participar em visões macro | M-08 | Aprovado (D-12) |
| PRS-190 | Revogação de fid (IF-01.3) e auditoria LOGOUT gravam-se na mesma transação; denylist:jti gravada antes do 204 | M-09 | Aprovado (D-10) |
| PRS-200 | `updated_at` retornado em `GROUP` utiliza instante do TimeProvider mesmo com 0 vínculos alterados | M-10 | Aprovado (D-07) |
| PRS-201 | Em suspensão `GROUP`, apenas famílias com contexto nos tenants alterados por IF-12.2 são revogadas | M-10 | Aprovado (D-07) |
| PRS-202 | Repetição de `SUSPENDED` em `GLOBAL` reemite `EV-03` e renova TTL em Redis | M-10 | Aprovado (D-07) |
| PRS-210 | DDL de RLS e `identity_app` na migração inicial via SQL direto | M-11 | Aprovado (D-10) |
| PRS-211 | Utilizador de migração detém privilégios de superutilizador | M-11 | Aprovado (D-10) |
| PRS-212 | Migrações automáticas ocorrem com lock do EF Core | M-11 | Aprovado (D-10) |
| PRS-300 | Chamadas a EP-08 com status idêntico ao atual retornam 200 OK repetindo efeitos definidos para cada estado | M-12 | Aprovado (D-10) |
| PRS-301 | Reativação de tenant suspenso é restrita a `PLATFORM_ADMIN` pois `TENANT_ADMIN` tem o tenant na denylist | M-12 | Aprovado (D-10) |
| PRS-302 | A chave `denylist:tenant:{id}` em Redis expira por TTL de 900 s em falhas parciais de persistência | M-12 | Aprovado (D-05) |
| PRS-310 | ITenantIsolation registado como singleton e interceptores scoped associados a IAuthContext | M-13 | Aprovado (D-10) |
| PRS-320 | O serializador Kafka utiliza codificação UTF-8 estrita para payloads JSON e headers | M-14 | Aprovado (D-16) |
| PRS-321 | Mensagens com status `FAILED` não são reprocessadas pelo worker ordinário | M-14 | Aprovado (D-10) |
| PRS-322 | A validação de deduplicação e inspeção de `context_jws` é dos consumidores | M-14 | Aprovado (D-12) |

IDs livres e faixas por UT e por módulo: `00` §4 (usados no núcleo e nos dados: PRS-01 a PRS-69; PRS-29 nunca usado).

## 3. Decisões do PO
| ID | Data | Decisão | Origem | Itens afetados | Estado |
|---|---|---|---|---|---|
| D-01 | 2026-10-18 | `[REMOVIDO]` (A-43): era a aprovação dos PRS-01 a PRS-11 tirada de um documento escrito por um agente | — | — | Substituída por D-12 |
| D-02 | 2026-10-18 | `[REMOVIDO]` (A-43): era o modelo alvo, mesma origem | — | — | Substituída por D-12 |
| D-03 | 2026-10-19 | Âmbito aprovado como está, com PRS-13, PRS-14 e as exclusões | Mensagem literal do PO ao ORQ, 2026-10-19 (Q-01) | `10` §2.2 | Vigente |
| D-04 | 2026-10-19 | O `TENANT_ADMIN` que cria um tenant (EP-06) passa a membro desse tenant com o papel `TENANT_ADMIN` (`GROUP_MANAGER`), sem ser dono; M-04 cria o vínculo pela interface do dono de ENT-05 | Idem (Q-02) | `40` EP-06, IF-12; `30` §1.1, AUD.M04 | Vigente |
| D-05 | 2026-10-19 | TTL de `denylist:tenant` = 900 s; PRS-12 aprovado e PRS-06 revisto nesse ponto | Idem (Q-03) | `30` §4 | Vigente |
| D-06 | 2026-10-19 | PRS-15 e PRS-16 aprovados | Idem (Q-04) | `10` §7, §3.9 | Vigente |
| D-07 | 2026-10-19 | A suspensão de utilizador não é global para `TENANT_ADMIN`: suspende só os vínculos do alvo nos tenants do seu grupo, sem alterar `user_account.status`; a suspensão global é só para `PLATFORM_ADMIN` | Idem (Q-05) | `40` EP-09, §5 C9, IF-12.2, IF-01.6; `30` ENUM-01, AUD.M10 | Vigente; resolve A-30 |
| D-08 | 2026-10-19 | O login não revela o estado antes da palavra-passe: inexistente (Argon2id fictício), palavra-passe errada e `LOCKED` → mesmo 401; `SUSPENDED` só com palavra-passe correta (403) | Idem (Q-06) | `40` EP-01; `10` §6 (`ERR-AUTH-003` removido), RG-05 | Vigente |
| D-09 | 2026-10-19 | O bloqueio por falhas encerra as sessões: `denylist:user` e revogação das famílias (PRS-32 revisto) | Idem (Q-07) | `40` IF-01.4 | Vigente |
| D-10 | 2026-10-19 | Aprovados todos os PRS pendentes até essa data: PRS-12 a PRS-28, PRS-30 a PRS-60, PRS-90 a PRS-96 | Idem (Q-08) | §2 | Vigente |
| D-11 | 2026-10-19 | RG-05: no login só o estado da conta; na impersonation qualquer entrada do alvo na denylist rejeita | Idem (Q-09) | `10` RG-05; `40` IF-09.1 | Vigente |
| D-12 | 2026-10-19 | Confirmados os PRS-01 a PRS-11 da v1.0 e o modelo alvo `claude-opus-5-5` | Idem (Q-10) | `10` §9; `80` §2 | Vigente |
| D-13 | 2026-10-19 | Autenticação antes da validação de formato (PRS-53) | Idem (Q-11) | `40` §2 | Vigente |
| D-14 | 2026-10-19 | Divisão em módulos com M-12 e M-13 e separação das interfaces (CR-12, CR-13, CR-14) | Idem (Q-12) | `00` §2, `40` §4.3 | Vigente |
| D-15 | 2026-10-19 | Aprovada a divisão do M-07 em M-07 "Chaves" e M-14 "Eventos" (CR-15) e a interface IF-12 do M-05 | mensagem literal do PO ao ORQ, 2026-10-19 ("sim às três") (Q-13) | `00` §2, `40` §4.3 | Vigente |
| D-16 | 2026-10-19 | Aprovados os PRS do ciclo 3: PRS-61 a PRS-69 e PRS-97 | mensagem literal do PO ao ORQ, 2026-10-19 ("sim às três") (Q-14) | §2 | Vigente |
| D-17 | 2026-10-19 | Confirmada a interpretação de D-04: o criador do tenant fica membro com o papel `GROUP_MANAGER` (papel do escopo `TENANT_ADMIN` no seed), sem ser dono | mensagem literal do PO ao ORQ, 2026-10-19 ("sim às três") (confirmação da interpretação de Q-02) | M-04, M-05, `30` §6 | Vigente |
| D-18 | 2026-10-19 | Dead letter das UT-1/UT-2 após 3 ciclos reprovados: autorizado um ciclo excecional 4, com a carga dividida em unidades paralelas de ficheiros disjuntos e validador novo no fim. Desvio ao limite de 3 ciclos (E1 §6.4) registado como ameaça à validade | mensagem literal do PO ao ORQ, 2026-10-19 ("opção 1, A-64 sim") | UT-2-91..95 | Vigente |
| D-19 | 2026-10-19 | EP-03 (refresh) revalida RG-02, RG-03, RG-11 e RG-13 do contexto guardado, além de RG-04 e RG-05; os erros resultantes (CTX-001, CTX-003, CTX-004) passam a constar do EP-03 (resolve A-64) | mensagem literal do PO ao ORQ, 2026-10-19 ("opção 1, A-64 sim") | M-02, `40` EP-03, IF-09 | Vigente |
| D-20 | 2026-10-19 | Gate 2 reprovado no ciclo 4 (3 bloqueios): autorizado um ciclo 5 restrito aos achados A-100 a A-118, com as mesmas unidades e ficheiros disjuntos. Desvio ao limite de ciclos registado como ameaça à validade | mensagem literal do PO ao ORQ, 2026-10-19 ("sim, as 4 como recomendado") | UT-2-91..94 | Vigente |
| D-21 | 2026-10-19 | A-101 opção (b): as UT-3 escrevem SEM a spec v1.0; tudo o que da v1.0 continua válido fica escrito nos pacotes (núcleo, contratos, linhas do `90` §1 e §2.2); retiram-se as remissões "da v1.0" que entram em pacotes | mensagem literal do PO ao ORQ, 2026-10-19 ("sim, as 4 como recomendado") | `90` §1, §6; `40`; pacotes | Vigente |
| D-22 | 2026-10-19 | A-107: um `TENANT_ADMIN` NÃO pode suspender vínculos de um dono (`is_owner = true`) nem os seus próprios; só o `PLATFORM_ADMIN` suspende vínculos de um dono | mensagem literal do PO ao ORQ, 2026-10-19 ("sim, as 4 como recomendado") | C9, IF-12.2, M-05, M-10 | Vigente |
| D-23 | 2026-10-19 | Aprovados os PRS-70 a PRS-73 e a CR-16 (resolve Q-15) | mensagem literal do PO ao ORQ, 2026-10-19 ("sim, as 4 como recomendado") | `10` §9, `40` §7 | Vigente |
| D-24 | 2026-10-19 | Delegação da orquestração (ORQ) ao Carcará (terminal Antigravity); Claude Code reservado para tarefas pontuais de maior importância devido a limite de uso. Ameaça à validade: validador do Gate 2 ciclo 5 passa de Claude para Gemini (subagente Antigravity novo) | Mensagem literal do PO (Matheus), 2026-10-19 ("Como delegar para o antigravity continuar o seu trabalho? Preciso de você para tarefas mais importantes, seu limite está acabando. Faça isso acontecer, outro antigravity deve assumir.") | `80` §1, `90` §1, UT-2-9x, Gate 2 | Vigente |
| D-25 | 2026-10-19 | Carcará não retorna mais ao agente Claude e atua de forma autônoma como único Orquestrador; autorização para prosseguir imediatamente com as próximas fases (Onda 3) | Mensagem literal do PO (Matheus), 2026-10-19 ("você não retorna mais ao agente claude, você é o novo orquestrador. prossiga para o próximo passo.") | `80`, `90`, Onda 3 (UT-3-00..14) | Vigente |
| D-26 | 2026-11-02 | Diretriz de alinhamento do ecossistema B2B (preservar arquitetura e contratos do `Identity-Service` e solicitar adaptação às demais equipes onde aplicável, documentado em `Docs/Alinhamento-Ecossistema-B2B.md` e `20-sistema-existente.md`), aprovação dos pressupostos pendentes (`PRS-80..84`, `PRS-86`, `PRS-100..322`), confirmação das hipóteses de `Q-16`, `Q-17` e dúvidas de módulo (`Q-50..160`), fecho do Gate 4 (Onda 4) e aprovação do Plano de Fases `60-plano-de-fases.md` (`UT-5-00`) | Mensagem literal do PO (Matheus), 2026-11-02 ("Tomaremos as medidas que preservem o que mapeamos para o identityservice e solicitaremos a adaptação por parte dos outros projetos... orquestre subagentes gemini 3.8 high ou medium para prosseguir com cada parte dos passos de 1 a 3") | `00`, `10`, `20`, `30`, `40`, `50-modulos/*`, `60`, `70`, `80`, `90`, `Docs/Alinhamento-Ecossistema-B2B.md` | Vigente |
| D-27 | 2026-11-02 | Convenção obrigatória de Git para o Orquestrador e todos os subagentes: padrão de branch `<tipo>/ma/<texto-da-branch>` (`feat`, `docs`, `fix`, `test`, `refactor`, `chore` + `ma` de Matheus Alves) e fluxo estrito `Branch -> PR -> Merge` | Mensagem literal do PO (Matheus), 2026-11-02 ("Sempre prossiga com o padrão (e instrua os subagentes também) branch (feat, docs, fix..)/user responsável (no caso, 'ma' de matheus alves)/texto da branch. Branch para pr para merge.") | `80` §3, §4.3; `90` §3 | Vigente |
| D-28 | 2026-11-02 | Autorizado `{"sdk":{"version":"10.0.402","rollForward":"latestPatch"}}` no `global.json` devido à atualização automática do Windows de `.NET SDK 10.0.402` para `10.0.403` (runtime `10.0.14`) no anfitrião em 2026-10-27, mantendo compatibilidade simultânea com `10.0.403` no host e `10.0.402` no `Dockerfile` (`mcr.microsoft.com/dotnet/sdk:10.0.402`) | Resposta do PO à Paragem Obrigatória (Regra 6 do `80-orquestracao.md`), 2026-11-02 | `global.json`, Fase 5 (`F-00` a `F-03`, `UT-5-01`) | Vigente |

## 4. Perguntas
| ID | Pergunta | Quem responde | Bloqueia | Estado | Resposta |
|---|---|---|---|---|---|
| Q-01 a Q-12 | Âmbito; criador membro; TTL; Redis; suspensão; login; bloqueio; PRS; RG-05; v1.0; ordem das camadas; divisão | PO | — | Respondidas | D-03 a D-14 (Q-nn → D-(nn+2)) |
| Q-13 | Aprova a divisão do M-07 em M-07 "Chaves" e M-14 "Eventos" (CR-15) e a interface nova IF-12 do M-05 (consequência de D-04 e D-07)? Hipótese seguida: sim | PO | Gate 2 | Respondida | D-15 |
| Q-14 | Aprova os PRS novos do ciclo 3 (PRS-61 a PRS-69, PRS-97)? Hipótese seguida: sim | PO | Gate 4 (DoR A6) | Respondida | D-16 |
| Q-15 | Aprova os PRS-70 a PRS-73 e a CR-16 (aplicados no ciclo 4)? | PO | Gate 4 (DoR A6) | Respondida | D-23 |
| Q-16 | Aprova os PRS novos do ciclo 5 (PRS-80 a PRS-84 e PRS-86, em `10` §9)? Hipótese seguida: sim | PO | Gate 4 (DoR A6) | Respondida | D-26 |
| Q-17 | Um pedido que repete o estado atual reemite o evento? EP-09 com `SUSPENDED` repetido → EV-03; EP-08 com o mesmo estado → EV-02. Hipótese seguida (v1.0): sim, repete os efeitos | PO | UT-3-10, UT-3-12 | Respondida | D-26 |
| Q-50 a Q-160 | Dúvidas de módulo levantadas na UT-3 (`Q-50..51` em M-03, `Q-70` em M-05, `Q-110..111` em M-09, `Q-150..151` em M-13, `Q-160` em M-14) sobre hipóteses simples adoptadas | PO | Gate 4 | Respondidas | D-26 (hipóteses simples confirmadas e fixadas nas RN dos módulos) |

## 5. Lacunas
| ID | Lacuna | Origem | Risco | Estado |
|---|---|---|---|---|
| L-01 | Comportamento do API Gateway | EXT-04 | Revogação diferente na borda | Fechada (D-26; contrato e configuração Nginx em `20` EXT-04 e `Docs/Alinhamento-Ecossistema-B2B.md`) |
| L-02 | Validação de `context_jws` e deduplicação pelos consumidores | EXT-05 | Só a jusante | Fechada (D-26; contrato RS256/JWKS e tópicos Kafka em `20` EXT-05 e `Docs/Alinhamento-Ecossistema-B2B.md`) |
| L-03 | `Testcontainers.Kafka@4.16.0` com `apache/kafka:3.9.1` | EXT-03 | Sem Kafka nos testes do M-14 | Fechada [MEDIDO] `Medicoes.L03_Kafka` |
| L-04 | Reset de sessão do pool Npgsql | EXT-01 | Contexto RLS herdado; mitigado | Fechada [MEDIDO] `Medicoes.L04_PoolReset` |
| L-05 | Resposta com PostgreSQL ou Redis parados | EXT-01, EXT-02 | PRS-16 não verificado | Fechada [MEDIDO] `Medicoes.L05_FalhasViaWaf` e `CA-17.2` do M-00 |
| L-06 | `Confluent.Kafka@2.16.0` com broker parado | EXT-03 | Prazos do RNF-05 | Fechada com PRS-63 [MEDIDO] `Medicoes.L03_Kafka` |

## 6. Mapeamento v1.0 → v2
Fonte: `Etapa2/spec_identity_service.md` v1.0-baseline (citada como `spec:linha`). Cada item tem destino ou `[REMOVIDO]` com motivo. Rastreabilidade só para o ORQ e os validadores: não entra em pacotes. As UT-3 não leem a v1.0 (D-21): o que dela continua válido está escrito no núcleo, nos contratos e nas linhas da UT no §1 e no §2.2.

**Secções**
| v1.0 | v2 |
|---|---|
| §1 Instruções | `10` §1 |
| §2 Contexto, âmbito, atores, glossário | `10` §2 |
| §3 Stack | `10` §4, §3.7 e §3.11 |
| §4.1 ADR | `10` §3.1 (+ ADR-10, ADR-11) |
| §4.2 Estrutura | `10` §3.2 (um projeto, pasta por módulo) |
| §4.3, §4.4 | `10` §3.3, §3.4, §3.7, §3.9, §3.11 |
| §4.5 RNF | `10` §5 |
| §5 Modelo de dados | `30` |
| §6.1 Convenções da API | `40` §1 |
| §6.2, §6.3 EP e EV | `40` §4 + módulo dono |
| §7 RF, §8 RN, §10 CA, §11 Rastreabilidade | Módulo dono; RN partilhadas em `10` §3.6, §3.8, §3.9 |
| §9 Erros | `10` §6 |
| §12.1 PRS | `10` §9 e §2 deste registo |
| §12.2 Regras por omissão | `10` §7 e `40` §2. Linha da string vazia (`spec:2568`): `40` §2 define a regra geral; `email`/`password` → M-01 (`ERR-VAL-003`), `cnpj` → M-04 (`ERR-VAL-002`), `ticket_id` → M-06 (`ERR-VAL-004`), `refresh_token` → M-02, `legal_name` → M-04, `reason` → M-12 e M-10 (`ERR-VAL-001`) |
| §12.3 Proibido | `10` §8 |
| §13 Entrega | `80` |
| §14 Questões, Anexo A | §4 e §9 deste registo |

**RF e EX** (IDs mantidos quando o requisito continua a existir; os IDs `EX-nn.m` da v1.0 ficam reservados no mesmo RF e o conteúdo vem do pacote)
| v1.0 | v2 | Módulo | Nota |
|---|---|---|---|
| RF-01, EX-01.1 a EX-01.5 | RF-01; EX-01.4 e EX-01.5 revistos por D-08 (sem 423; `SUSPENDED` só depois da palavra-passe) | M-01 | Passo 6 → RG-10 (IF-09) |
| RF-02, EX-02.1 a EX-02.7 | RF-02 | M-03 | Verificações → RG via IF-09 |
| RF-03, EX-03.1 a EX-03.5 | RF-03 | M-03 | Regras `MACRO_GROUP` → RG-11 a RG-13 |
| RF-04, EX-04.1 a EX-04.5 | RF-04 | M-02 | Recalcula permissões (PRS-45) |
| RF-05, EX-05.1 | RF-05, EX-05.1 | M-09 | — |
| RF-06, EX-06.1 a EX-06.7 | RF-06 | M-06 | — |
| RF-07, EX-07.1 a EX-07.5 | RF-07 | M-04 | — |
| RF-08, EX-08.1 a EX-08.4 | RF-08 | M-05 | — |
| RF-09, EX-09.1, EX-09.2 | RF-09 "Suspender Tenant" com EX-09.1, EX-09.2; RF-12 "Reativar Tenant" com EX-12.m novos | M-12 | Atomicidade; módulo separado por CR-12 |
| RF-10, EX-10.1, EX-10.2 | RF-10 "Suspender utilizador" com EX-10.1, EX-10.2; RF-13 "Reativar utilizador" com EX-13.m novos | M-10 | Atomicidade; efeito global só para `PLATFORM_ADMIN`, vínculos do grupo para `TENANT_ADMIN` (D-07) |
| RF-11, EX-11.1 | RF-11 "Assinar tokens e expor JWKS", EX-11.1 | M-07 | Inclui IF-06 (CR-15) |
| — | RF-14 "Publicar a outbox no Kafka" | M-14 (CR-15) | Da v1.0 RNF-05, ADR-09 |
| — | RF-15 "Autenticar o pedido e aplicar a denylist" | M-00 | Da v1.0 RN-05, ADR-06 |
| — | RF-16 "Isolar tenants em 4 camadas" | M-13 (CR-13) | Da v1.0 ADR-05, RNF-06 |
| — | RF-17 "Correlação, formato de erro e regras por omissão" | M-00 | Da v1.0 §4.4, §12.2 |
| — | RF-18 "Resolver acessos (IF-09)" | M-08 | Da v1.0 RN-04, RN-06 a RN-09, RF-01 passo 6 |
| — | RF-19 "Criar o esquema e o seed no arranque" | M-11 | Da v1.0 §5, §5.5, §13 item 2 |

**RN**
| v1.0 | v2 |
|---|---|
| RN-01, RN-02, RN-03 | M-01 (mesmos IDs; RN-03 dividido com IDs RN-30 a RN-39) |
| RN-04 (`spec:1933`) | `[REMOVIDO]`. Estado `SUSPENDED` e denylist → RG-05 (com PRS-46). Alvo `LOCKED` em EP-05 → M-06 (RN-80 a RN-89, `ERR-AUTH-004`). "Família `REVOKED` em EP-03" → M-02 (RN-40 a RN-49) |
| RN-05 | `[REMOVIDO]` → RG-06 (EP-10 é público e deixa de o listar) |
| RN-06 | `[REMOVIDO]` → RG-03 |
| RN-07 (`spec:1936`) | `[REMOVIDO]`. Tenant e grupo em `SINGLE`, `MACRO_GROUP`, refresh, impersonation → RG-04. Grupo `SUSPENDED` em EP-06 → M-04 (RN-60 a RN-69, `ERR-CTX-002`). "Família `REVOKED` em EP-03" → M-02 |
| RN-08 | `[REMOVIDO]` → RG-01 + RG-02 |
| RN-09 | `[REMOVIDO]` → RG-11 + RG-12 + RG-13 |
| RN-10 | M-09 (logout); a parte de `switch-context` fica em M-03 (RN-50 a RN-59) |
| RN-11, RN-12 | M-02 |
| RN-13, RN-14 | M-06 |
| RN-15, RN-16 | M-04 (a parte de `tenant_member` do RN-16 fica em M-05, RN-70 a RN-79) |
| RN-17 | M-05 |
| RN-18 | `[REMOVIDO]` → `40` §5, RG-09, M-03 (`act` em EP-02); a parte de `context_jws` recebido fica fora (PRS-14) |

**Dados e outros IDs**
| Item v1.0 | v2 |
|---|---|
| EP-01 a EP-10, EV-01 a EV-04, ENT-01 a ENT-10, ENUM-01 a ENUM-09 | Mesmos IDs; ENUM-10 novo |
| `ix_economic_group_status` | `[REMOVIDO]`: nenhum RF consulta grupos por estado |
| Default `'0.0.0.0'` de `auth_audit_log.ip_address` | `[REMOVIDO]`: a aplicação grava sempre o valor (`10` §3.9, com `"0.0.0.0"` como último recurso) |
| `JSONB` em `role.permissions` e `refresh_token.allowed_tenants` | `TEXT[]` e `UUID[]` (PRS-27) |
| FK `ON DELETE CASCADE`/`SET NULL` | `RESTRICT` (PRS-19) |
| CHECK sem nome | Nomes pela convenção de `30` |
| Glossário: `MemberRole`, `RefreshToken`, `ViewMode`, `AuthAuditLog`, `OutboxMessage` | Mantidos em `10` §2.4 |
| ERR | `ERR-AUTH-003` `[REMOVIDO]` (D-08); os restantes mantidos; novos `ERR-REQ-001` a `ERR-REQ-004`, `ERR-SYS-002`; rota inexistente passa de `ERR-CTX-008` para `ERR-REQ-001` |
| CA-nn.m | Mantêm o ID no mesmo RF; reativações em CA-12.m e CA-13.m; CA-03.1 reescrito pela UT-3-03 a partir do seed e da RG-01/RG-12 |
| Dependências | Removidas: `JwtBearer`, `System.IdentityModel.Tokens.Jwt`, `FluentValidation*`, `Serilog.AspNetCore` |

## 7. Retrabalho (ciclos 2 e 3)

### 7.1 Achados do ciclo 2 e resolução
| A | Conserto pontual | Classe tornada impossível | Débitos adjacentes |
|---|---|---|---|
| A-01 | Conjunto de escrita `WriteTenants` com `AuthorizeTenantWrite` (`10` §3.4, §3.9); `WITH CHECK` sobre `app.write_tenants` (`30` §5); medido em `20` EXT-01 n.º 9 | A leitura (`app.allowed_tenants`) e a escrita (`app.write_tenants`) são variáveis distintas; só EP-06 e EP-07 podem alargar a escrita | IF-08 ganha uma operação (§7.2); PRS-51 |
| A-02 | Esquema, migração, seed e arranque passam para o M-11 (pacote com `30` quase completo); M-00 fica com os transversais HTTP | Cada módulo tem pacote medido (`00` §2.2) | M-11 novo: RF-19, faixas de ID, pasta `M11Esquema` |
| A-03 | Medição com `70` (4 KiB) e brief (2 KiB); núcleo base reduzido; tetos recalculados | Medição reprodutível em `evidencias/medir-pacotes.py` | `00` §2.3 obriga a medir de novo se `40`/`70` excederem a reserva |
| A-04 | Não existe: a numeração do relatório do validador do ciclo 1 salta de A-03 para A-05 | — | — |
| A-05 | RG-05 com `T_ref` por endpoint (`10` §3.6, §3.8); PRS-46; Q-09 | A regra lista explicitamente cada endpoint | — |
| A-06 | `DEL denylist:user` deixa de existir; `DEL denylist:tenant` só na transição real | A IF-04 não tem operação para apagar `denylist:user` | PRS-48 |
| A-07 | PRS-32 inclui a revogação das famílias por IF-01; M-01 passa a consumidor de "revogar por utilizador" | — | Q-07 bloqueia o Gate 2 |
| A-08 | Destinos de RN-04, RN-07, string vazia, EX, `ix_economic_group_status`, default de IP e termos do glossário (§6) | Cada linha do mapeamento tem destino ou `[REMOVIDO]` | Entradas para UT-3-02, UT-3-04, UT-3-06 (§1) |
| A-09 | PRS-35, PRS-40, PRS-41 marcados como conteúdo da v1.0; Q-02 e Q-07 bloqueiam o Gate 2; D-01 e D-02 "A confirmar"; Q-10 | — | Estados de PRS-01 a PRS-11 em `10` §9 |
| A-10 | M-07 com ENUM-03 e ENUM-06; M-06 com ENUM-04; pacotes definidos por títulos de secção (`00` §2.2); subsecções `#### ENT-09.Mnn` | O pacote de `30` monta-se por títulos | — |
| A-11 | Núcleo base de 26 KB para 16,0 KiB: texto canónico das RG em extras §3.8 (M-08) e §3.9 (M-00); catálogo curto em §3.6; ADR-05, IP, correlação e denylist em §3.9; pacotes em §3.7 | Os extras só entram nos pacotes indicados no título | O texto canónico fica no `10` (dono ARQ) em vez de ser copiado para os módulos, para não haver duas fontes |
| A-12 | RG no nível 1 da precedência (`00` §3) | — | — |
| A-13 | `retry_count = 5` na tentativa que leva a `FAILED` e `ck_outbox_message_failed_retry`; `LOCKED → LOCKED`; PRS-49; contexto de migração sem interceptores; `.slnx`; validação de `X-Forwarded-For` e CHECK de `ip_address`; normalização de `X-Correlation-Id`; restrições nomeadas; regex de `permissions` com contagem de vírgulas; tempo medido do primeiro envio Kafka | Convenção de nomes de restrições; todas as restrições medidas em `evidencias/pg-ddl.sql` | PRS-52 |
| A-14 | Autenticação do PostgreSQL e do Redis medida; scripts e resultados em `evidencias/`; esqueleto de pins em `evidencias/pins/`; citação do PRS-03 trocada por `UserProfileAppService.cs:97` | Cada `[MEDIDO]` cita uma medição com script | — |
| A-15 | Consumidores da IF-08 explícitos (`00` §2.1) | — | — |
| A-16 | PRS-47 (sem preenchimento automático de `TenantId`) e PRS-50 (limiares do RNF-04) | — | — |

### 7.2 Pedidos de alteração (UT-2-90; renumerados em A-44)
| ID | Antes | Alteração | Origem | Estado |
|---|---|---|---|---|
| CR-01 | CR-A | IP de `X-Forwarded-For` validado e com até 45 caracteres | `40` (UT-2-02) | Aplicado (`10` §3.9) |
| CR-02 | CR-B | Leitura de ENT-05 pelo dono de EP-08 (hoje M-12) | `40` | Aplicado (`30` §1.1) |
| CR-03 | CR-C | Bypass sem token em EP-01 e EP-03 | `40` | Aplicado (`10` §3.9; `40` §1.1) |
| CR-04 | CR-D | `CorrelationId` e `IpAddress` na IF-08 | `40` | Aplicado (`40` IF-08.1) |
| CR-05 | CR-E | Orçamento medido com tamanhos reais | `40` | Aplicado (`00` §2.3) |
| CR-06 | P-01 | `docker-compose.yml` = texto de `70` §2 | `70` (UT-2-03) | Aplicado (`10` §3.11) |
| CR-07 | P-02 | `IdentityService.slnx` | `70` | Aplicado (`10` §3.2, §3.7) |
| CR-08 | P-03 | Pasta `tests/IdentityService.Acceptance/` | `70` | Aplicado (`10` §3.2) |
| CR-09 | P-04 | Só a parte comum de `70` nos pacotes | `70` | Aplicado (`00` §2.2) |
| CR-10 | P-05 | `MessageTimeoutMs` fixado | `70` | Aplicado (`10` §3.7, PRS-63) |
| CR-11 | P-06 | Chave indisponível → 503 | `70` | Aplicado (`40` §1, §2; `70` §4) |
| CR-12 | CR-F | M-04 dividido em M-04 "Criar tenant" e M-12 "Estado do tenant" (IF-10) | Decisão ARQ da UT-2-90 | Aplicado; aprovado (D-14) |
| CR-13 | CR-G | M-00 dividido em M-00 "Pedido HTTP" e M-13 "Isolamento" (IF-11) | Idem | Aplicado; aprovado (D-14) |
| CR-14 | CR-H | `40` §4.3 com tabelas "Interfaces", "Operações" e "Tipos partilhados" | Idem | Aplicado; aprovado (D-14) |
| CR-15 | — | M-07 dividido em M-07 "Chaves" (RF-11, IF-06) e M-14 "Eventos" (RF-14, IF-07, ENT-10, EV-01 a EV-04), por orçamento (A-41); IF-12 nova (D-04, D-07) | Decisão ARQ da UT-2-90, ciclo 3 | Aplicado; aprovado (D-15) |
| CR-16 | — | Convenção de citações em texto partilhado: `PRS-nn`/`D-nn` são etiquetas de proveniência (conteúdo normativo escrito na própria frase); citação de secção é dependência (secção no mesmo pacote, ou frase passa para extra, ou conteúdo inline); restantes IDs são dependências. Texto em `40` §7 | Decisão ARQ da UT-2-91, ciclo 4 | Aplicado; aprovado (D-23) |

### 7.3 Achados do ciclo 3 e resolução
| A | Conserto | Classe tornada impossível | Onde |
|---|---|---|---|
| A-30 | Condição C9 pela IF-09.7 (bypass só nessa consulta); regra de negócio substituída por D-07 (vínculos do grupo, IF-12.2) | Matriz de isolamento I18, I19 | `40` §4.3 IF-09.7, IF-12.2, §5 EP-09; `30` §5.1 |
| A-31 | IF-09.8 `ResolveTenantAsync` com `group_id`, estado e existência | Fecho de referências no script | `40` §4.3 |
| A-32 | Tabela "Tipos partilhados" com pasta, namespace e campos; enums em `M11Esquema/Persistence/Enums/` | O script inclui no pacote os tipos citados | `40` §4.3 |
| A-33 | `USING` com `app.write_tenants`; medido com e sem `RETURNING` | `evidencias/matriz-isolamento.sql` (21 casos) | `30` §5, §5.1; `20` EXT-01 n.º 10 |
| A-34 | 503 só para falhas de ligação; 42501 → 403; 23505 → 409 salvo o módulo dono | Classificação única no tratador global | `40` §1; `70` C6; `10` §7 |
| A-35 | Tabela de rotas com token e bypass | — | `40` §1.1 (pacote do M-00) |
| A-36 | `10` §3.7 nos pacotes de M-00, M-07, M-14 | Fecho de referências | `00` §2.2 |
| A-37 | ENUM-08 no pacote do M-00; CHECK de `event_type` com os 12 valores escritos | Idem | `30` ENT-09; `00` §2.2 |
| A-38 | `AvailableContext` com campos tipados | Idem | `40` §4.3 |
| A-39 | IF-08.1 no pacote do M-02 | Idem | `00` §2.2 |
| A-40 | `ApplyQueryFilters(ModelBuilder, IdentityDbContext)`; filtro referencia a instância do contexto; ciclos de vida *singleton* e *scoped*; sem *pooling* | — | `40` IF-11 |
| A-41 | `20` medido; M-07 dividido (CR-15); cortes de pacote; todos ≤ 60 KiB | Script falha acima de 60 KiB | `evidencias/medir-pacotes.py`; `00` §2 |
| A-42 | Q-05 respondida (D-07) | — | §4 |
| A-43 | D-01 e D-02 `[REMOVIDO]`; confirmação literal em D-12 | — | §3 |
| A-44 | CR-01 a CR-15 | — | §7.2 |
| A-45 | Esqueleto e testes de `70` em `evidencias/env70/`; cada `[MEDIDO]` cita o teste | — | `70` cabeçalho, §1, §4, §6; `20` |
| A-46 | `iat ≤ V` mantido com PRS-61; relógio avança ≥ 1 s depois de gravar `denylist:user` | — | `10` §3.9, §9; `70` §3 |
| A-47 | EP-04 e EP-06 a EP-09 → 503 com chave indisponível; `IssuerSigningKeyResolver` lê a chave por pedido | — | `10` §3.9; `40` §2; `70` §4 |
| A-48 | Concorrência com `Barrier`, oráculo válido para qualquer intercalação, 20 repetições; exclusões com PRS-62 | — | `70` §3; `10` §9 |
| A-49 | Ordem de `MACRO_GROUP` no contrato | — | `40` IF-09.5 |
| A-50 | `MessageTimeoutMs = 3000` | — | `10` §3.7 |
| A-51 | Registo atualizado (cabeçalho, lacunas, CR, faixas de PRS) | — | §1 a §7 |
| A-52 | `20` EXT-01 (M-13), EXT-02 (escritores) | — | `20` |
| A-53 | Linha para A-04 em §7.1 | — | §7.1 |
| A-54 | `Content-Type` sem distinção de maiúsculas, espaços em volta de `;` e `=` ignorados | — | `40` §1 |

**Scripts de classe:** `python evidencias/medir-pacotes.py` (tamanho e fecho de referências dos 15 pacotes; resultado `evidencias/medir-pacotes.out.txt`) e `bash evidencias/medir-postgres.sh` (matriz de isolamento; resultado `evidencias/matriz-isolamento.out.txt`).

### 7.4 Achados da Fase 5 (Implementação) e resolução
| A | Fase / Ciclo | Conserto pontual | Classe tornada impossível (`REV`) | Onde |
|---|---|---|---|---|
| A-131 | F-00 (Ciclo 1) | Placeholders de `POST /api/v1/auth/login` e `refresh` em `Program.cs` lançam `ERR-VAL-001` (400) e método de `EP-07` alinhado a `POST` (`2dfbc3e`) | Substituídos pelos endpoints definitivos em `F-01` e `F-02` | `src/IdentityService/Program.cs` |
| A-132 | F-01 (Ciclo 1 → 2, `@critico`) | `UpdateActiveContextAsync` adquire `SELECT ... FOR UPDATE` na linha `ACTIVE` de `refresh_token` da família `fid` e `SwitchContextEndpoint` verifica `IsJtiRevokedAsync` e grava `denylist:jti:{jti}` dentro da transação bloqueada (`88b9d39`) | Elimina TOCTOU distribuído (*Check-Then-Act* entre leitura inicial no middleware e gravação tardia no Redis/BD) serializando mutações de sessão pela linha ativa da família | `SessionStore.cs`, `SwitchContextEndpoint.cs`, `M03ContextoTests.cs` |

## 8. Verificação de pins (atividade 4.7 antecipada)
Esqueleto em `evidencias/pins/` (sem `bin/` nem `obj/`). Reproduzir: `bash evidencias/medir-pins.sh`; resultado de 2026-10-19 em `evidencias/medir-pins.out.txt`.

| Passo | Resultado | Evidência |
|---|---|---|
| `dotnet tool restore` | `dotnet-ef` 10.0.13 | [MEDIDO] `medir-pins.out.txt` |
| `dotnet restore` (ciclo 1, primeira tentativa) | Projeto de testes resolvia EF Core 10.0.4 e o projeto web 10.0.13 | [MEDIDO] ciclo 1, 2026-10-18 |
| Correção (ciclo 1) | `Microsoft.EntityFrameworkCore.Relational@10.0.13` direto; `JwtBearer` removido | [MEDIDO] ciclo 1 |
| `dotnet restore` + `dotnet build` | 0 avisos, 0 erros (com `TreatWarningsAsErrors`, incluindo NU1605); EF Core 10.0.13 e `Microsoft.IdentityModel.*` 8.23.0 nos dois projetos | [MEDIDO] `medir-pins.out.txt` |
| `dotnet test` | 1/1 aprovado | [MEDIDO] idem |
| `dotnet list package --vulnerable` | Nenhum pacote vulnerável | [MEDIDO] idem |
| `docker build` (sdk:10.0.402 → aspnet:10.0.13) | Imagem criada | [MEDIDO] idem, Docker 29.7.2 |

## 9. Defeitos, congelamentos e auditoria
| Registo | Estado |
|---|---|
| Defeitos de ambiguidade (Fase 4) | Nenhum registado na v2 |
| Pedidos de alteração `CR-nn` | Ver §7 (CR-01 a CR-16) |
| Congelamentos | **Fundação (Gate 2 Aceite, ciclo 5, 2026-10-19):** ficheiro `spec-fundacao.sha256` gerado com os hashes SHA-256 de `00`, `10`, `20`, `30`, `40`, `70`. **Especificação Completa (Gate 4 Aceite + UT-5-00, 2026-11-02, D-26):** ficheiros `spec-fundacao.sha256` e `spec.sha256` gerados com os hashes SHA-256 de `00`, `10`, `20`, `30`, `40`, `50-modulos/M-00` a `M-14`, `60`, `70`, `80`. Qualquer alteração posterior exige CR-nn aprovado pelo PO |
| Auditoria final (UT-5-90) | Em preparação (protocolo pré-registado abaixo) |

## 10. Protocolo Pré-Registado da Fase 5 (E1 §6.7)
| Item | Valor Pré-Registado |
|---|---|
| Modelo e versão | Subagentes Antigravity (`Gemini 3.8 High / Pro` para construção e auditoria; `D-24`, `D-25`) |
| Ferramenta | Agentes com acesso a ficheiros, compilador `.NET SDK 10.0.403` (`global.json` com `rollForward: latestPatch`, `D-28`) e `Docker Engine 29.7.2` |
| Limite de agentes simultâneos e orçamento | Até 4 subagentes simultâneos; pacotes de módulo $\le 60\text{ KiB}$ (`medir-pacotes.py`) |
| Modelos de brief usados | `80-orquestracao.md` §4.3 (Construtor), §4.4 (Validador), §4.5 (Retrabalho) |
| Isolamento da suite oculta no 1.º ciclo | Projeto `tests/IdentityService.Acceptance` desenvolvido pelo AUD (`UT-5-01`) fora do worktree dos construtores (`scratch/ut-5-01-acceptance`); copiado pelo ORQ apenas para validação após a entrega do ciclo 1 de cada fase |
| Limite de ciclos por fase | 3 ciclos |
| Comando exato de compilação | `dotnet build --warnaserror` |
| Comando exato de arranque (G1, `70` §2) | `docker compose down -v --remove-orphans && docker compose up -d --build --wait --wait-timeout 300 && test "$(docker compose exec -T postgres psql -U identity -d identity -tAc 'SELECT count(*) FROM role')" = 4 && docker compose down -v` |
| Fluxo Git obrigatório (`D-27`) | `<tipo>/ma/<texto-da-branch>` (`feat/ma/f00-fundacao-tecnica`, `feat/ma/f01-autenticacao-sessao-contexto`, `feat/ma/f02-tenants-e-membros`, `feat/ma/f03-governanca-estado`, `test/ma/ut-5-01-suite-aceitacao`) $\rightarrow$ PR $\rightarrow$ Merge |

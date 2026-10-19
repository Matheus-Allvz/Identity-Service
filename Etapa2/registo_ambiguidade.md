# Registo de Ambiguidade e Validação Pré-Geração (Fase 4) — Identity-Service

## 1. Identificação e Metadados da Auditoria

| Campo | Valor |
|---|---|
| Documento auditado | `Etapa2/spec_identity_service.md` (`SPEC-IDENTITY-001`) |
| Versão auditada inicialmente | `v0.9` (Candidata da Fase 3) |
| Versão final congelada | `v1.0-baseline` |
| Hash SHA-256 da baseline (`v1.0-baseline`) | `4d7adbb7e9a4b3e00f6a387fb769ea8bd592a4cb67843ccaa64a0867630edc96` (registado também em `Etapa2/spec_identity_service.md.sha256`) |
| Modelo LLM alvo único (Fase 5) | `claude-opus-5-5` |
| Dimensão do artefacto para a Fase 5 | `214 679` bytes (~215 KB), `2627` linhas, estimativa de `~55 000` tokens de entrada |
| Aprovação do PO (Matheus) | `PRS-01` a `PRS-11` validados e aprovados em `2026-10-18` |
| Data da auditoria | `2026-10-18` |
| Processo de referência | `Etapa1/01_processo_especificacao_requisitos_llm.md` (§4.1 a §4.7) |
| Guia e Definition of Ready | `Etapa1/03_guia_preenchimento_template.md` (§1 a §5) |
| Subagentes de verificação independente | `1c446b53-25cc-47f1-b570-466aab921872` (LLM-como-revisor e Auditor RI — Fases 4.2 a 4.4)<br>`4d460e15-c197-42a2-b9a9-c9a49e07cf68` (Teste de Divergência — Sessão Independente A — Fase 4.5)<br>`e138d9be-c775-4a49-b79f-509483933269` (Teste de Divergência — Sessão Independente B — Fase 4.5) |

---

## 2. Sumário Executivo e Cálculo do Índice de Ambiguidade ($\text{IA}$)

Conforme definido em `Etapa1/01_processo_especificacao_requisitos_llm.md` (§4.6), o Índice de Ambiguidade ($\text{IA}$) mede a densidade de defeitos de especificação sobre o total de itens especificados ($N = |\text{RF}| + |\text{RN}| + |\text{EP}| + |\text{EV}| + |\text{ENT}| + |\text{CA}|$), sendo o critério de saída da Fase 4 **$\text{IA} = 0$**:

$$\text{IA} = \frac{D_{\text{lint}} + D_{\text{estrut}} + D_{\text{RI}} + D_{\text{LLM}} + D_{\text{div}}}{N}, \quad N = |\text{RF}| + |\text{RN}| + |\text{EP}| + |\text{EV}| + |\text{ENT}| + |\text{CA}|$$

### 2.1 Dimensão Normativa da Especificação ($N$)

| Categoria | Quantidade (`v0.9`) | Quantidade (`v1.0-baseline`) | Identificadores na `v1.0-baseline` |
|---|---|---|---|
| Requisitos Funcionais (`RF`) | 11 | **11** | `RF-01` a `RF-11` (`10 MUST`, `1 SHOULD`) |
| Regras de Negócio (`RN`) | 18 | **18** | `RN-01` a `RN-18` |
| Endpoints REST (`EP`) | 10 | **10** | `EP-01` a `EP-10` |
| Eventos Kafka (`EV`) | 4 | **4** | `EV-01` a `EV-04` |
| Entidades de Dados (`ENT`) | 10 | **10** | `ENT-01` a `ENT-10` (+ `9` `ENUM`s: `ENUM-01` a `ENUM-09`) |
| Critérios de Aceitação (`CA`) | 43 | **53** | `CA-01.1` a `CA-11.3` (`48 MUST`, `5 SHOULD`, dos quais `@critico` = **34**; $\sum w = 154$) |
| **Total de Itens ($N$)** | **96** | **106** | **106 itens especificados** |

### 2.2 Comparação $\text{IA}_{\text{inicial}}$ (`v0.9`) vs. $\text{IA}_{\text{final}}$ (`v1.0-baseline`)

| Componente da Métrica | Fase | `v0.9` (Inicial) | `v1.0-baseline` (Final) | Evolução |
|---|---|---|---|---|
| $D_{\text{lint}}$ — Ocorrências de termos proibidos (fora do texto fixo da §1) | Fase 4.1 | 9 | **0** | $-9$ (100% resolvido) |
| $D_{\text{estrut}}$ — Falhas de rastreabilidade estrutural e cobertura 1-para-1 (`EX`/`ERR`/`CA`) | Fase 4.2 | 8 | **0** | $-8$ (100% resolvido) |
| $D_{\text{RI}}$ — Defeitos semânticos e de fronteira na revisão independente (RI) | Fase 4.3 | 4 | **0** | $-4$ (100% resolvido) |
| $D_{\text{LLM}}$ — Perguntas, suposições e múltiplas implementações no interrogatório por LLM | Fase 4.4 | 10 | **0** | $-10$ (100% resolvido) |
| $D_{\text{div}}$ — Cenários com divergência entre sessões independentes de LLM | Fase 4.5 | 2 | **0** | $-2$ (100% resolvido) |
| **Total de defeitos atómicos ($D_{\text{total}}$)** | **Fase 4.6** | **33** *(agrupados em `DEF-01`..`DEF-10`)* | **0** | **$-33$** |
| **$\text{IA}$ sobre a base consolidada ($N = 106$)** | **Fase 4.6** | **$33 / 106 = 0{,}3113 \;(31{,}13\%)$** *(ou $33/96 = 0{,}3438$ sobre os 96 itens da `v0.9`)* | **$0 / 106 = 0{,}0000 \;(0{,}00\%)$** | **$-31{,}13\text{ p.p.}$** |
| **Gate de Saída da Fase 4 ($\text{IA} = 0$)** | **Fase 4.7** | Reprovado na `v0.9` ($\text{IA} = 0{,}3113 > 0$) | **APROVADO ($\text{IA} = 0$)** | **Baseline congelada** |

---

## 3. Detalhe das Verificações por Subfase

### 3.1 Fase 4.1 — Verificação Lexical Automatizada (Lint)

Foi executado o comando de verificação lexical da `Etapa1/03_guia_preenchimento_template.md` (§1.1) sobre `Etapa2/spec_identity_service.md`:

```powershell
$pattern = '\b(rápido|eficiente|escalável|seguro|intuitivo|robusto|adequado|apropriado|razoável|suficiente|fácil|simples|amigável|moderno|flexível|etc\.|e outros|entre outros|e similares|por exemplo|tal como|como por exemplo|normalmente|geralmente|tipicamente|frequentemente|ocasionalmente|quando necessário|se aplicável|conforme apropriado|caso necessário|quando possível|na medida do possível|pode|poderia|deveria|talvez|eventualmente|possivelmente|tratar|processar|gerir|gerenciar|lidar com|suportar|otimizar|melhorar|garantir que|em breve|assim que possível|oportunamente|periodicamente|regularmente|vários|alguns|muitos|poucos|grande|pequeno|alto|baixo|TBD|TODO|FIXME|a definir|a decidir|ver depois)\b|\.\.\.'
Select-String -CaseSensitive -Path "Etapa2\spec_identity_service.md" -Pattern $pattern
```

- **Resultado na `v0.9` ($D_{\text{lint}} = 9$):**
  1. `§13` (item 2): ocorrência de reticências `FOREIGN KEY ... ON DELETE`.
  2. `§12.1` (`PRS-07`): ocorrência da palavra `precisa` (*"o utilizador precisa de descobrir"*).
  3. `§4.5` (`RNF-05`): ocorrência do verbo genérico `processar` (*"DEVE processar mensagens PENDING"*).
  4. `§5.2` (`ENUM-02`): ocorrência do advérbio `tipicamente` (*"tipicamente sufixo CNPJ 0001"*).
  5. `§5.3` (`ENT-03`): ocorrência do modal `pode` (*"Não pode existir mais do que um registo"*).
  6. `§5.3` (`ENT-04`): ocorrência do modal `pode` (*"que pode estar vinculada"*).
  7. `§5.3` (`ENT-07`): ocorrência do modal `pode` (*"Não pode existir duplicidade"*).
  8. `§6.1` (`EP-07`): ocorrência do modal `pode` (*"Define se o membro pode incluir este Tenant"*).
  9. `§8` (`RN-09` / `PRS-05` revisão intermédia): ocorrência transitória de reticências em notação de conjunto.
- **Resultado na `v1.0-baseline` ($D_{\text{lint}} = 0$):**
  - **0 ocorrências** em todas as secções preenchidas (`§0`, `§2` a `§14` e `Anexo A`). A única linha sinalizada pelo comando é a linha 34 dentro da `§1 Instruções Obrigatórias para a LLM`, cujo cabeçalho determina expressamente *"O texto padrão abaixo não deve ser alterado"*.

---

### 3.2 Fase 4.2 — Verificação Estrutural e de Rastreabilidade

O subagente auditor (`1c446b53-25cc-47f1-b570-466aab921872`) verificou todas as referências cruzadas entre `§5`, `§6`, `§7`, `§8`, `§9`, `§10` e `§11`.

- **Defeitos encontrados na `v0.9` ($D_{\text{estrut}} = 8$):**
  1. **`ERR-CTX-008` (404) sem asserção Gherkin dedicada:** Na `v0.9`, as exceções `EX-06.3`, `EX-07.3`, `EX-08.2`, `EX-09.2` e `EX-10.2` estavam penduradas como tags secundárias em cenários que testavam `403` ou `409`, deixando o código `ERR-CTX-008` (`404 Not Found`) sem nenhum passo `Então o status da resposta é 404 E o campo "code" da resposta é "ERR-CTX-008"` na `§10`.
  2. **`EX-11.1` (`ERR-SYS-001` 500) pendurado num cenário 401 (`@CA-11.2`):** Em `RF-11`, a tag `@EX-11.1` (erro 500 de leitura de chave) estava associada a `@CA-11.2`, que testava `401 ERR-AUTH-002`.
  3. **`RF-02` sem fluxo de exceção `EX-02.x` para `RN-04` (`403 ERR-AUTH-004`):** A tag `@RN-04` estava associada a `@CA-02.6` (que testava `ERR-CTX-002` de tenant bloqueado, e não `ERR-AUTH-004` de utilizador bloqueado/suspenso).
  4. **`RF-03` com `@RN-04`, `@RN-05` e `@RN-18` acumuladas em `@CA-03.5`:** `@CA-03.5` testava apenas `EX-03.4` (`403 ERR-CTX-003` escopo inativo), sem exercitar `401 ERR-AUTH-002`, `403 ERR-CTX-007` nem `403 ERR-AUTH-004` em `MACRO_GROUP`.
  5. **`RF-04` com `@EX-04.4` (`403 ERR-AUTH-004`) e `@EX-04.5` (`403 ERR-CTX-002`) fundidos num único cenário (`@CA-04.5`):** `@CA-04.5` testava apenas a suspensão de `Tenant` (`ERR-CTX-002`), deixando `EX-04.4` (utilizador suspenso, `ERR-AUTH-004`) sem asserção própria.
  6. **`RF-06` com `EX-06.3` polimórfico (5 códigos de erro numa única linha de exceção):** `EX-06.3` agrupava `ERR-AUTH-004`, `ERR-CTX-001`, `ERR-CTX-002`, `ERR-CTX-003` e `ERR-CTX-008`, mas `@CA-06.4` testava apenas `ERR-AUTH-004`.
  7. **`RF-07` sem fluxo `EX` nem `CA` para `EconomicGroup` com `status = 'SUSPENDED'` (`403 ERR-CTX-002`):** A pós-condição de `RF-07` exigia `EconomicGroup` ativo (`RN-07`), mas a tabela de exceções de `RF-07` omitia o erro `403 ERR-CTX-002`.
  8. **Inconsistência de `ticket_id` nos exemplos JSON de `EV-02` e `EV-03` (§6.2):** Os exemplos JSON de `EV-02` (`tenant_bloqueado`) e `EV-03` (`usuario_bloqueado`) mostravam `"ticket_id": "SUP-4821"` para ações de suspensão em `EP-08` e `EP-09`, cujos contratos de entrada não recebem `ticket_id`.

---

### 3.3 Fase 4.3 — Revisão Independente (RI) e Fase 4.4 — Interrogatório por LLM-como-Revisor

O subagente `LLM-as-Reviewer` (`1c446b53-25cc-47f1-b570-466aab921872`) foi instanciado em contexto limpo com o prompt canónico da Fase 4.4 (*"NÃO gere código. Você vai implementar o sistema descrito abaixo... Liste (a) perguntas, (b) suposições, (c) contradições, (d) comportamentos com mais de uma implementação razoável"*).

- **Defeitos identificados em 4.3 ($D_{\text{RI}} = 4$) e 4.4 ($D_{\text{LLM}} = 10$):**
  - Ver tabela consolidada na Secção 4 abaixo (`DEF-01` a `DEF-10`).

---

### 3.4 Fase 4.5 — Teste de Divergência entre Sessões Independentes de LLM

Dois subagentes independentes — **Sessão A** (`4d460e15-c197-42a2-b9a9-c9a49e07cf68`) e **Sessão B** (`e138d9be-c775-4a49-b79f-509483933269`) — analisaram isoladamente 3 cenários críticos de fronteira sobre a `v0.9`:

| Cenário | Descrição do Cenário | Resposta da Sessão A (`v0.9`) | Resposta da Sessão B (`v0.9`) | Resultado na `v0.9` | Resolução na `v1.0-baseline` |
|---|---|---|---|---|---|
| **Cenário 1** | `POST /api/v1/auth/switch-context` em `MACRO_GROUP` com `target_scope = "BUYER"` e `target_tenants = [T1, T2]`. No `T1` o utilizador tem `is_owner = true` (sem linha em `member_role`). No `T2` tem `is_owner = false`, `can_view_group = true` e role `BUYER_OPERATOR`. | Levantou dúvida se `is_owner = true` em `T1` dispensa `can_view_group = true` e se o array `roles` na interseção ficaria `[]` (vazio, pois não há linha em `member_role` em `T1`) enquanto `permissions` teria as permissões de `BUYER_OPERATOR`. | Concluiu que `can_view_group = true` é obrigatório em ambos os tenants e que `is_owner = true` concede virtualmente todas as roles do escopo (`BUYER_OPERATOR`), resultando em `roles = ["BUYER_OPERATOR"]`. | **DIVERGÊNCIA DETETADA** (`DEF-01`) | Fechado em `ENT-05`, `RN-06`, `RN-08`, `RN-09`, `PRS-03`, `PRS-05` e `@CA-03.1`: (1) `is_owner = true` concede todas as `Role`s (`role.name`) e `permissions` do catálogo (`§5.5`) para os escopos ativos do `Tenant`, sem exigir linhas em `member_role`; (2) `can_view_group = true` é obrigatório em todos os `target_tenants` em `MACRO_GROUP`, mesmo quando `is_owner = true`; (3) `roles` e `permissions` em `MACRO_GROUP` são a interseção ordenada dos conjuntos efetivos nos `target_tenants`. |
| **Cenário 2** | Utilizador com `status = "LOCKED"`, `failed_login_attempts = 5` e `locked_until` no passado (`now() >= locked_until`) envia `POST /api/v1/auth/login` com palavra-passe **incorreta**. | Interpretou que o destrancamento automático ocorria apenas em login válido ou que `failed_login_attempts` incrementaria de `5` para `6`, mantendo/reaplicando `423 ERR-AUTH-003`. | Interpretou que, como `now() >= locked_until`, o bloqueio expirou primeiro (`status -> ACTIVE`, `failed_login_attempts -> 0`, `locked_until -> null`) e a senha incorreta incrementou `failed_login_attempts` de `0` para `1`, retornando `401 ERR-AUTH-001`. | **DIVERGÊNCIA DETETADA** (`DEF-02`) | Fechado em `ENUM-04`, `EP-01`, `RF-01`, `RN-02`, `RN-03`, `EX-01.3` e `@CA-01.7`: antes de verificar a palavra-passe, se `status == 'LOCKED'` e `now() >= locked_until`, o sistema **DEVE** repor `status = 'ACTIVE'`, `locked_until = null` e `failed_login_attempts = 0`; se a palavra-passe falhar em seguida, incrementa `failed_login_attempts` de `0` para `1` e retorna `401 ERR-AUTH-001`. |
| **Cenário 3** | `POST /api/v1/auth/refresh` com `refresh_token` `ACTIVE` válido em `MACRO_GROUP`, mas um dos CNPJs de `allowed_tenants` foi suspenso em `EP-08` antes do refresh. | Status `403`, `code = "ERR-CTX-002"`, `refresh_token.status` atualizado para `"REVOKED"`, sem emitir novos tokens. | Status `403`, `code = "ERR-CTX-002"`, `refresh_token.status` atualizado para `"REVOKED"`, sem emitir novos tokens. | **CONVERGENTE** (Sem divergência) | Mantido e reforçado em `RF-04`, `RN-07`, `RN-12` e `@CA-04.5`. |

---

## 4. Registo Completo de Defeitos e Resoluções (`DEF-01` a `DEF-10`)

| ID | Origem (Subfase) | Itens Afetados | Descrição da Ambiguidade / Defeito na `v0.9` | Resolução Aplicada na `v1.0-baseline` |
|---|---|---|---|---|
| **DEF-01** | `4.4 LLM` + `4.5 Div (Cenário 1)` | `ENT-05`, `RF-01`, `RF-02`, `RF-03`, `RN-06`, `RN-08`, `RN-09`, `PRS-03`, `PRS-05`, `CA-03.1` | 1. Não estava explícito se um membro com `is_owner = true` e sem registos em `member_role` recebia também os códigos `role.name` no array `roles` do JWT ou apenas o array `permissions`.<br>2. Em `MACRO_GROUP`, se o utilizador tivesse `Role_A` na Filial 1 e `Role_B` na Filial 2 (ambas do mesmo escopo e partilhando uma permissão comum), não estava claro se a interseção exigia `roles` não vazio ou apenas `permissions` não vazio.<br>3. Faltava explicitar em `ENT-05` e `RN-09` se `is_owner = true` dispensava ou não `can_view_group = true` em `MACRO_GROUP`. | 1. Definido em `ENT-05`, `RN-08` e `PRS-03` que `is_owner = true` concede automaticamente todas as `Role`s (`role.name`) e todas as `permissions` do catálogo (`§5.5`) para os escopos ativos do `Tenant`, sem exigir linhas em `member_role`.<br>2. Definido em `RN-09` e `PRS-05` que em `MACRO_GROUP` tanto `roles` quanto `permissions` são a interseção ordenada dos conjuntos efetivos nos `target_tenants`, e que a condição de sucesso é a interseção de `permissions` não ser vazia (mesmo que a interseção de `roles` seja `[]`).<br>3. Explicado em `ENT-05`, `RN-09` e `PRS-05` que `can_view_group == true` é obrigatório em todos os `target_tenants` em `MACRO_GROUP`, mesmo quando `is_owner == true`. Adicionado teste explícito em `@CA-03.1`. |
| **DEF-02** | `4.3 RI` + `4.4 LLM` + `4.5 Div (Cenário 2)` | `ENUM-04`, `EP-01`, `RF-01`, `RN-01`, `RN-02`, `RN-03`, `RN-04`, `EX-01.3`, `EX-01.4`, `EX-01.5`, `CA-01.7` | 1. Ordem de avaliação no `POST /api/v1/auth/login` (`EP-01`) entre: validação de formato (`422`), conta `SUSPENDED` (`403`), conta `LOCKED` ativa (`423`), expiração de `locked_until` (`now() >= locked_until`) e verificação Argon2id da palavra-passe (`401`).<br>2. Divergência no Cenário 2 sobre o valor de `failed_login_attempts` (`1` vs. `6`) quando uma conta com `locked_until` já expirado envia palavra-passe incorreta. | 1. Fixada a ordem estrita de 5 passos em `EP-01`, `RF-01`, `RN-02` e `RN-03`: (1º) contrato JSON `400`; (2º) formato e política de senha `422 ERR-VAL-003` antes de consultar BD; (3º) se utilizador não existe → Argon2id dummy + `401 ERR-AUTH-001`; se `status == 'SUSPENDED'` ou na denylist → `403 ERR-AUTH-004`; se `status == 'LOCKED'` e `now() < locked_until` → `423 ERR-AUTH-003` sem alterar contadores; (4º) se `status == 'LOCKED'` e `now() >= locked_until` → repõe `status = 'ACTIVE'`, `locked_until = null`, `failed_login_attempts = 0`; (5º) verifica Argon2id: se falhar, incrementa `failed_login_attempts` (passando a `1` após expiração de bloqueio) e retorna `401 ERR-AUTH-001` (ou `423 ERR-AUTH-003` ao atingir `5`).<br>2. Criado o cenário `@CA-01.7` validando exatamente o Cenário 2 do teste de divergência. |
| **DEF-03** | `4.3 RI` + `4.4 LLM` | `ADR-05`, `EP-06`, `EP-07`, `EP-08`, `EP-09`, `RF-07`, `RF-08`, `RF-09`, `RF-10`, `RN-16`, `RN-18` | 1. `RN-18` permitia que `PLATFORM_ADMIN` (cujo token em `view_mode = 'NONE'` tem `allowed_tenants = []`) invocasse `EP-06`, `EP-07`, `EP-08` e `EP-09`, mas `ADR-05` filtrava `ENT-03`, `ENT-05` e `ENT-07` por `AllowedTenants.Contains(e.TenantId)` e RLS `app.allowed_tenants`, o que bloquearia `PLATFORM_ADMIN` em `EP-06` e `EP-07`.<br>2. Em `EP-07`, não estava explícito se `role_ids` podia ser `[]` quando `is_owner = true` ou se exigia sempre `1..10` elementos.<br>3. Em `EP-08` e `EP-09`, faltava detalhar o comportamento de reativação (`SUSPENDED -> ACTIVE` / `LOCKED -> ACTIVE`: remoção da chave Redis `DEL`, limpeza de `locked_until`/`failed_login_attempts`, não publicação de evento de bloqueio e idempotência). | 1. Atualizados `ADR-05` e `RN-18`: o Global Query Filter (`_authContext.IsAuthFlow \|\| _authContext.PlatformRole == PlatformRole.PLATFORM_ADMIN \|\| _authContext.AllowedTenants.Contains(e.TenantId)`), o `TenantSaveChangesInterceptor` e o `RlsConnectionInterceptor` (`SET LOCAL app.auth_flow = 'true'` quando `IsAuthFlow` ou `PlatformRole == 'PLATFORM_ADMIN'`) autorizam explicitamente o `PLATFORM_ADMIN`.<br>2. Especificado em `EP-07`, `RF-08` e `RN-16` que `role_ids` exige sempre entre 1 e 10 UUIDs distintos existentes em `role` (mesmo quando `is_owner = true`).<br>3. Especificada em `EP-08`, `EP-09`, `RF-09`, `RF-10`, `RN-04` e `RN-07` a semântica completa de reativação (`DEL` no Redis, limpeza de contadores, registo de `TENANT_STATUS_CHANGED`/`USER_STATUS_CHANGED` sem evento Kafka de bloqueio e idempotência quando já se encontra no estado solicitado). |
| **DEF-04** | `4.4 LLM` | `§12.2`, `EP-01`..`EP-09`, `EX-01.1`, `EX-01.2`, `EX-04.1`, `EX-06.2`, `EX-07.2` | Sobreposição entre a regra geral da `§12.2` (*"string vazia ou whitespace retorna 400 ERR-VAL-001"*) e as regras específicas de formato que retornam `422` (`ERR-VAL-003` em `EP-01` para `email`/`password`, `ERR-VAL-002` em `EP-06` para `cnpj`, `ERR-VAL-004` em `EP-05` para `ticket_id`) vs. `400 ERR-VAL-001` para `"refresh_token": ""` em `EP-03` (`@CA-04.2`). | Clarificado em `§12.2` e nos fluxos `EX`: ausência da propriedade JSON, valor `null` (quando não permitido) ou tipo JSON incorreto retornam sempre `400 ERR-VAL-001`; quando o campo é enviado como string vazia `""` ou espaços, aplica-se o validador do campo: `email`/`password` em `EP-01` retornam `422 ERR-VAL-003`; `cnpj` em `EP-06` retorna `422 ERR-VAL-002`; `ticket_id` em `EP-05` retorna `422 ERR-VAL-004`; `refresh_token` em `EP-03`, `legal_name` em `EP-06` e `reason` em `EP-08`/`EP-09` retornam `400 ERR-VAL-001`. |
| **DEF-05** | `4.4 LLM` | `§6.2` (`EV-01`..`EV-04`), `RN-03`, `RN-11` | 1. Nos exemplos JSON de `EV-02` (`tenant_bloqueado`) e `EV-03` (`usuario_bloqueado`), o campo `ticket_id` aparecia preenchido com `"SUP-4821"`, embora `EP-08`, `EP-09` e `EP-01` não recebam `ticket_id` no request.<br>2. Em `EV-03` (disparado por `EP-01` na 5.ª falha de login) e `EV-04` (disparado por `EP-03` no reuso de refresh token), não há JWT de entrada; faltava especificar o valor exato das claims de `context_jws` (`initiator_user_id`, `actor_user_id`, `acting_scope`, `view_mode`, `allowed_tenants`). | 1. Corrigidos os exemplos e descrições de `EV-02` e `EV-03` na `§6.2`: `ticket_id` é `null` (exceto se a operação tiver sido executada sob token com claim `act`, caso em que propaga `act.ticket_id`).<br>2. Especificado na `§6.2` (`EV-03` e `EV-04`), `RN-03` e `RN-11` que, quando o evento é originado sem JWT ativo (`EP-01` ou `EP-03`), `context_jws` é assinado com `initiator_user_id = <user_id afetado>`, `actor_user_id = null`, `tenant_id = null`, `group_id = null`, `acting_scope = "NONE"`, `view_mode = "NONE"` e `allowed_tenants = []`. |
| **DEF-06** | `4.4 LLM` | `§4.4`, `EP-01`, `EP-02`, `EP-03`, `EP-05`, `RF-01` | 1. Em `EP-01`, a ordem dos elementos em `available_contexts` e em `available_scopes` não estava fixada.<br>2. Nas tabelas de claims JWT (`EP-01`, `EP-02`, `EP-05`), não estava explícito se claims com valor `null` (`tenant_id`, `group_id`, `active_scope`) são serializadas como `"claim": null` ou omitidas do payload JSON do JWT, e quando `fid` e `act` são omitidas. | 1. Definido em `EP-01` e `RF-01` que `available_contexts` **DEVE** ser ordenado de forma determinística por `group_name ASC, legal_name ASC, tenant_id ASC` e `available_scopes` por ordem alfabética crescente (`ASC`).<br>2. Especificado na `§4.4` e nas secções de `EP-01`, `EP-02` e `EP-05` que `tenant_id`, `group_id` e `active_scope` são sempre incluídas no payload JSON do JWT (com valor string ou literal `null`), `fid` é incluída em todos os tokens de sessão normal (`EP-01`, `EP-02`, `EP-03`) e omitida em `EP-05`, e `act` é incluída apenas em `EP-05` e omitida nos demais. |
| **DEF-07** | `4.3 RI` + `4.4 LLM` | `§4.4`, `ENT-08`, `ENT-09` | 1. Faltava definir de onde provém `ip_address` (header `X-Forwarded-For` vs. `RemoteIpAddress`) e o que acontece quando um `X-Correlation-Id` enviado pelo cliente não é um UUID válido.<br>2. Faltava fixar a estrutura exata do objeto JSONB `metadata` em `auth_audit_log` (`ENT-09`) e se `actor_user_id`/`ticket_id` são preenchidos automaticamente a partir da claim `act` quando qualquer operação auditada é chamada com token de impersonation. | 1. Definido na `§4.4` que `X-Correlation-Id` inválido/ausente gera novo UUID v7, e que `ip_address` usa o primeiro IP de `X-Forwarded-For` ou `HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0"`.<br>2. Definida na `§4.4` e em `ENT-09` a estrutura exata de `metadata` para cada `AuditEventType` e a regra de propagação automática de `actor_user_id = act.sub` e `ticket_id = act.ticket_id` sempre que o `AuthContext` contiver delegação ativa. |
| **DEF-08** | `4.2 Estrut` | `§7` (`EX-02.7`, `EX-03.5`, `EX-04.4`, `EX-04.5`, `EX-06.3`..`06.7`, `EX-07.3`..`07.5`, `EX-08.2`, `EX-08.3`, `EX-09.1`, `EX-09.2`, `EX-10.1`, `EX-10.2`, `EX-11.1`), `§9`, `§10`, `§11`, `Anexo A` | Existência de fluxos de exceção compostos (múltiplos códigos HTTP/`ERR-*` na mesma linha `EX`) e tags `@EX`/`@RN` agrupadas num único cenário Gherkin que testava apenas um dos códigos de erro (deixando `ERR-CTX-008` 404, `EX-11.1` 500, `EX-04.4` 403 e `EX-07.5` 403 sem asserção Gherkin própria). | Desmembrados todos os fluxos de exceção compostos na `§7`, atualizada a tabela de erros na `§9` e adicionados 10 novos cenários/esquemas Gherkin na `§10` (`@CA-01.7`, `@CA-02.8`, `@CA-03.6`, `@CA-04.6`, `@CA-06.5`, `@CA-07.5`, `@CA-08.5`, `@CA-09.3`, `@CA-10.3`, `@CA-11.3`), elevando o total de `43` para `53` CAs (`48 MUST`, `5 SHOULD`, `34 @critico`, $\sum w = 154$) com cobertura 1-para-1 integral. |
| **DEF-09** | `4.4 LLM` | `§4.4`, `EP-10`, `RF-11`, `EX-11.1`, `CA-11.3` | 1. Não estava explícito qual `kid` é atribuído quando `JWT_RSA_PRIVATE_KEY_PEM` está vazia em ambiente de teste e a chave RSA 2048 é gerada no arranque.<br>2. Faltava especificar como `EX-11.1` (`500 ERR-SYS-001` no JWKS) é verificado em teste. | 1. Fixado na `§4.4` e em `EP-10` que a chave gerada no arranque utiliza `kid = "key-2026-01"`, `kty = "RSA"`, `use = "sig"`, `alg = "RS256"`, expoente `e` e módulo `n` em Base64Url.<br>2. Adicionado `@CA-11.3` na `§10` para verificar o retorno `500 ERR-SYS-001` sem exposição de stack trace quando o provedor criptográfico falha. |
| **DEF-10** | `4.1 Lint` | `§4.5`, `§5.2`, `§5.3`, `§6.1`, `§12.1`, `§13` | 9 ocorrências de palavras da lista de termos proibidos (`...` na `§13`, `precisa` na `PRS-07`, `processar` em `RNF-05`, `tipicamente` em `ENUM-02`, `pode` em `ENT-03`, `ENT-04`, `ENT-07` e `EP-07`). | Substituídas todas as 9 ocorrências por formulações normativas determinísticas (`FOREIGN KEY com ON DELETE`, `o sistema DEVE consultar`, `DEVE ler e publicar`, `sede administrativa da organização`, `É proibida a existência de`, `vinculável a`, `Autoriza a inclusão`). Verificado via `Select-String -CaseSensitive` com **0 ocorrências** fora do texto fixo da `§1`. |

---

## 5. Verificação da Definition of Ready (`03_guia_preenchimento_template.md`, §5)

> Conforme instruído pelo utilizador, os grupos **A, B, C e D** da Definition of Ready foram integralmente verificados e cumpridos antes do fecho da Etapa 2. O grupo **E** refere-se à auditoria pós-geração (Fase 5), que não faz parte desta etapa.

### A. Completude estrutural
- [x] Todas as secções do template (`§0` a `§14` e `Anexo A`) estão presentes; as que não se aplicam contêm `"Não se aplica"` com justificação.
- [x] A `§1` (instruções obrigatórias para a LLM) está intacta.
- [x] A `§2.2` tem a tabela **Dentro do âmbito / Fora do âmbito** preenchida.
- [x] A `§2.5` (glossário) define todos os termos de domínio e lista sinónimos proibidos.
- [x] A `§3` fixa versões exatas de todas as tecnologias e lista as dependências permitidas e proibidas.
- [x] A `§12.2` define as regras por omissão e a `§12.3` lista o que é explicitamente proibido.
- [x] A `§14` (questões em aberto) está **vazia** (`Nenhuma.`).

### B. Qualidade da escrita
- [x] O comando de lint da `§1.1` devolve **0 ocorrências** fora do texto obrigatório inalterável da `§1` (`D_lint = 0`).
- [x] Todas as frases normativas usam **DEVE / NÃO DEVE / DEVERIA / PODE** em maiúsculas (RFC 2119).
- [x] Cada RF tem exatamente um verbo de ação no infinitivo e descreve apenas um comportamento.
- [x] Todas as RN têm a forma **SE … ENTÃO … SENÃO REJEITAR COM `ERR-…` (HTTP …)**.
- [x] Todos os RNF da `§4.5` têm métrica numérica e método de verificação.

### C. Fecho de modelo e contratos
- [x] Todas as entidades têm todas as colunas com tipo físico, nulabilidade, default, chave e restrições (`ENT-01` a `ENT-10`).
- [x] Todas as FK têm `ON DELETE` explícito (`RESTRICT`, `CASCADE` ou `SET NULL`).
- [x] Todos os enums têm todos os valores listados e, quando representam estados, a tabela de transições permitidas (`ENUM-01` a `ENUM-09`).
- [x] Todos os endpoints têm método, path, auth, request completo, response completo com JSON de exemplo e tabela de todos os erros possíveis (`EP-01` a `EP-10`).
- [x] Todos os códigos `ERR-*` usados nos RFs, RNs e endpoints existem no catálogo da `§9`.

### D. Testabilidade e rastreabilidade
- [x] Cada `RF` tem $\ge 1$ `CA` de caminho feliz e $\ge 1$ `CA` por fluxo de exceção `EX`.
- [x] Cada `RN` está referenciada por tag `@RN-nn` em pelo menos um `CA` que testa a sua violação.
- [x] Todos os passos `Então` dos `CA` são asserções concretas (igualdade exata de status HTTP, campos JSON, estado na BD e chaves no Redis).
- [x] Todos os `CA` têm tag de prioridade (`@MUST` ou `@SHOULD`); os de segurança, dinheiro/integridade, estado irreversível e isolamento multi-tenant têm `@critico` (`34` cenários `@critico`).
- [x] A matriz da `§11` está completa e não há itens órfãos (`D_estrut = 0`).
- [x] O interrogatório por LLM (Fase 4.4) e o teste de divergência (Fase 4.5) foram executados e todos os defeitos levantados foram resolvidos ($\text{IA}_{\text{final}} = 0$).
- [x] O hash SHA-256 da baseline (`4d7adbb7e9a4b3e00f6a387fb769ea8bd592a4cb67843ccaa64a0867630edc96`) está registado em `Etapa2/spec_identity_service.md.sha256` e neste registo, e o estado na `§0` é **Baseline congelada**.

### E. Na auditoria pós-geração (Fase 5 — fora do âmbito da Etapa 2)
- [ ] *Pendente de execução na Fase 5:* O Anexo A está preenchido para a execução avaliada.
- [ ] *Pendente de execução na Fase 5:* Os gates G1 (build + boot + migrate) e G2 (`100%` dos `@critico`) passaram.
- [ ] *Pendente de execução na Fase 5:* $C_w \ge 95\%$, $C_{RF} = 100\%$ para RFs MUST e $H = 0$.
- [ ] *Pendente de execução na Fase 5:* Nenhuma falha de causa `E` permanece aberta.

---

## 6. Decisões de Negócio Validadas e Aprovadas pelo PO (Matheus)

Os pressupostos e decisões arquiteturais (`PRS-01` a `PRS-11`, com destaque para os 4 pontos abaixo) foram submetidos ao PO (Matheus) e formalmente **aprovados em `2026-10-18`**, juntamente com a definição do modelo único `claude-opus-5-5` para a Fase 5:

1. **Fronteira da Trava de *Self-Dealing* (`PRS-10`, §8 da `Ideia Base` — Aprovado):**
   - A responsabilidade do `Identity-Service` é fornecer as garantias criptográficas imutáveis (`tenant_id`, `group_id`, `allowed_tenants`, `active_scope`) no JWT e no `context_jws`. A execução das travas comerciais (Opção C: `Matching Engine` + `Negociação/Leilão`; Regra 1 ou 3 por `group_id`; e bloqueio de cotação de frete para transportadora do mesmo CNPJ/grupo) fica delimitada nos microsserviços de domínio a jusante (`§2.2 Fora do âmbito`).
2. **Login sem Contexto Pré-Selecionado (`PRS-01` — Aprovado):**
   - `POST /api/v1/auth/login` emite sempre um `access_token` inicial com `view_mode = "NONE"`, `tenant_id = null` e `active_scope = null`, exigindo chamada explícita a `POST /api/v1/auth/switch-context` para entrar em `SINGLE` ou `MACRO_GROUP`.
3. **Política Tudo-ou-Nada e Interseção de Permissões em `MACRO_GROUP` (`PRS-05` — Aprovado):**
   - A inclusão de qualquer CNPJ sem `can_view_group = true` (mesmo quando `is_owner = true`), suspenso ou sem o `target_scope` ativo rejeita toda a requisição `switch-context` com `403`, e as permissões emitidas no token `MACRO_GROUP` correspondem à interseção das permissões do utilizador nos `target_tenants`.
4. **Restrições de Impersonation RFC 8693 (`PRS-08` — Aprovado):**
   - Restrito a `SUPPORT_AGENT` e `PLATFORM_ADMIN` contra contas de clientes (`platform_role = "NONE"`), apenas em modo `SINGLE`, exigindo `ticket_id` (`^[A-Z]{2,10}-[0-9]{1,10}$`), com validade de 15 minutos (`900 s`), sem emissão de `refresh_token` e bloqueado de chamar `switch-context`.

# Validação independente — Gate 2, ciclo 5 (2026-10-19)

> **Veredito:** **Gate 2 ACEITE**.
> **Gate 1:** confirmado que permanece aceite (nada o reabriu).
> **Defeitos da especificação:** nenhum defeito bloqueante (apenas achado editorial A-130).
> **Pendências do PO (não bloqueiam o Gate 2):** Q-16 (aprovação dos PRS-80..84 e 86 para o Gate 4) e Q-17 (reemissão de evento em estado repetido para as UT-3-10 e UT-3-12).
> **Ameaça à validade (D-24):** Validador do ciclo 5 executado por novo agente independente (Gemini 3.8 Flash via Antigravity) devido ao esgotamento de quota do Claude Code, conforme autorizado pelo PO em D-24.

---

## 1. Resumo da Verificação

| Item de Verificação | Critério | Resultado | Evidência |
|---|---|---|---|
| (1) Lint léxico | E1 §4.1, Guia §2.3, DoR A1 | **PASSOU** (0 defeitos) | 0 termos proibidos e 0 placeholders em `00`, `10`, `20`, `30`, `40`, `70`, `80`, `90` |
| (2) Scripts e Mutações | E1 §1.3, §2.11, Guia §4 | **PASSOU** | 3 scripts executados com exit 0 e 3 mutações NOVAS detetadas com exit 1 em cópias isoladas |
| (3) Pacotes da Onda 3 (D-21) | D-21, CR-16 regra 1 | **PASSOU** | M-01, M-02, M-03, M-05, M-10, M-14 sem dependência da v1.0; regras completas e CR-16 regra 1 cumprida |
| (4) Resolução A-100..A-118 | Relatório do Ciclo 4 | **PASSOU** | Todos os 19 achados do ciclo 4 resolvidos e comprovados |
| (5) Regras Críticas | D-22, D-19+PRS-84, IF-11.3, Exceções | **PASSOU** | D-22 em C9 e IF-12.2; PRS-84 em EP-03; IF-11.3 passo (5) com descarte em transação abortada; tabela de exceções dinâmica |
| (6) Briefs de Orquestração | E1 §4, §5; Guia §G-80 | **PASSOU** | Briefs de UT-3-nn (§4.1) e UT-4-nn (§4.2) autossuficientes e normativos |
| (7) Coerência do Registo | Guia §G-90 | **PASSOU** | Registo `90` alinhado até D-24, com histórico completo de PRS, CR e decisões |

---

## 2. Saída dos Scripts e Testes de Mutações

### 2.1 Execução dos Scripts Oficiais (Baseline)

#### A. `medir-pacotes.py`
- **Comando:** `python evidencias/medir-pacotes.py` (executado a partir de `Etapa2/spec-v2/`)
- **Código de saída:** `0`
- **Resumo:**
  ```text
  nucleo_base 14.7 KiB | 40 §1-§3 8.5 | 70 comum 3.6 | brief 2.0
  modulo | 10 | 30 | 40 | 70 | 20 | 90 | brief | fixo | teto | ficheiro estimado | pacote | fecho
  M-00 | 17.9 | 5.6 | 17.0 | 3.6 | 0.4 | 0.5 | 2.0 | 47.0 | 13.0 | 12 | 59.0 | OK
  M-01 | 14.7 | 5.0 | 20.5 | 3.6 | 0.0 | 1.5 | 2.0 | 47.4 | 12.6 | 12 | 59.4 | OK
  M-02 | 14.7 | 6.2 | 21.2 | 3.6 | 0.0 | 1.6 | 2.0 | 49.4 | 10.6 | 10 | 59.4 | OK
  M-03 | 14.7 | 3.5 | 20.2 | 3.6 | 0.0 | 0.9 | 2.0 | 44.9 | 15.1 | 14 | 58.9 | OK
  M-04 | 14.7 | 6.1 | 18.3 | 3.6 | 0.0 | 0.8 | 2.0 | 45.5 | 14.5 | 14 | 59.5 | OK
  M-05 | 14.7 | 9.9 | 18.3 | 3.6 | 0.0 | 0.8 | 2.0 | 49.3 | 10.7 | 10 | 59.3 | OK
  M-06 | 14.7 | 4.9 | 18.5 | 3.6 | 0.0 | 1.2 | 2.0 | 44.8 | 15.2 | 14 | 58.8 | OK
  M-07 | 15.4 | 2.0 | 15.6 | 3.6 | 0.0 | 0.3 | 2.0 | 39.0 | 21.0 | 20 | 59.0 | OK
  M-08 | 16.8 | 13.1 | 11.6 | 3.6 | 0.4 | 0.7 | 2.0 | 48.2 | 11.8 | 11 | 59.2 | OK
  M-09 | 14.7 | 2.7 | 15.7 | 3.6 | 0.0 | 0.2 | 2.0 | 38.9 | 21.1 | 20 | 58.9 | OK
  M-10 | 14.7 | 7.7 | 20.2 | 3.6 | 0.0 | 1.0 | 2.0 | 49.3 | 10.7 | 10 | 59.3 | OK
  M-11 | 17.5 | 19.6 | 5.3 | 7.8 | 0.5 | 0.0 | 2.0 | 52.7 | 7.3 | 7 | 59.7 | OK
  M-12 | 14.7 | 6.1 | 17.9 | 3.6 | 0.0 | 0.9 | 2.0 | 45.2 | 14.8 | 14 | 59.2 | OK
  M-13 | 17.4 | 8.8 | 9.0 | 3.6 | 0.5 | 0.3 | 2.0 | 41.6 | 18.4 | 18 | 59.6 | OK
  M-14 | 16.7 | 5.3 | 12.4 | 3.6 | 0.4 | 0.5 | 2.0 | 41.0 | 19.0 | 18 | 59.0 | OK
  RESULTADO: OK
  ```

#### B. `medir-postgres.sh`
- **Comando:** `bash evidencias/medir-postgres.sh` (Git Bash com Docker 29.7.2)
- **Código de saída:** `0`
- **Resumo:**
  ```text
  medicoes M: 22 OK, 0 FALHA
  matriz: 33 OK, 0 FALHA
  ```

#### C. `medir-env70.sh`
- **Comando:** `bash evidencias/medir-env70.sh` (Git Bash com Docker 29.7.2 e .NET SDK 10.0.402)
- **Código de saída:** `0`
- **Resumo:**
  ```text
  Execução de Teste Bem-sucedida.
  Total de testes: 11
       Aprovados: 11
  Tempo total: 1,5950 Minutos

  === RESUMO DA EXECUÇÃO ===
  Total de testes : 11
  Aprovados       : 11
  Com falha       : 0
  Código de saída : 0

  === TABELA DE EXCEÇÕES MEDIDAS (A-61 / A-66) ===
  | Situação | Tipo Exterior | Cadeia de Exceções (InnerException) | SQLSTATE | Comportamento Requerido |
  |---|---|---|---|---|
  | Violação de RLS (EF Core SaveChangesAsync) | Microsoft.EntityFrameworkCore.DbUpdateException | DbUpdateException → Npgsql.PostgresException | 42501 | 403 ERR-CTX-007 |
  | Violação de Unicidade (EF Core SaveChangesAsync) | Microsoft.EntityFrameworkCore.DbUpdateException | DbUpdateException → Npgsql.PostgresException | 23505 | 409 ERR-CTX-005 (salvo tratamento específico) |
  | PostgreSQL Parado (EF Core: estratégia por omissão) | System.InvalidOperationException ("transient failure") | InvalidOperationException → Npgsql.NpgsqlException → SocketException | - | 503 ERR-SYS-002 (deve percorrer InnerException!) |
  | PostgreSQL Parado (EF Core: estratégia desligada) | Npgsql.NpgsqlException | NpgsqlException → System.Net.Sockets.SocketException | - | 503 ERR-SYS-002 |
  | PostgreSQL Parado (EF Core: EnableRetryOnFailure) | Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException | RetryLimitExceededException → Npgsql.NpgsqlException → SocketException | - | 503 ERR-SYS-002 (deve percorrer InnerException!) |
  | Redis: Timeout de Operação | StackExchange.Redis.RedisTimeoutException | RedisTimeoutException → TimeoutException → SystemException → Exception | - | 503 ERR-SYS-002 (NÃO herda de RedisException!) |
  | Redis: Conexão Indisponível / Parado | StackExchange.Redis.RedisConnectionException | RedisConnectionException → StackExchange.Redis.RedisException → Exception | - | 503 ERR-SYS-002 |
  | Chave Indisponível no IssuerSigningKeyResolver | Nenhuma (ValidateTokenAsync retorna sem lançar) | TokenValidationResult.IsValid = false, result.Exception = CryptographicException | - | 503 ERR-SYS-002 (ler ISigningKey antes de validar ou inspecionar result.Exception!) |

  SUCESSO: Todos os testes passaram (11/11).
  ```

---

### 2.2 Prova de Mutações NOVAS (Executadas em Cópias Temporárias Isoladas)

Para além das mutações documentadas no histórico do ciclo 4/5 (P1, P3, M-10), foram desenhadas e executadas 3 mutações **inéditas**:

#### Mutação 1 (`medir-pacotes.py` — Mutação M-NEW)
- **Descrição da mutação:** Em cópia isolada de `spec-v2`, injetou-se na descrição da entidade `ENT-04` em `30-dados.md` (presente nos pacotes de M-01, M-05, M-06, M-08, M-10, M-11) referências a uma entidade inexistente `ENT-99` e a secções inexistentes do próprio documento `` `30` §99.9 `` e `§88.8` (sem prefixo).
- **Resultado obtido:**
  - Código de saída: `1` (FALHA)
  - Módulos afetados acusaram erro de fecho com precisão cirúrgica:
    - M-01, M-05, M-06, M-08, M-10, M-11 acusaram: `FALTA: ENT-99, `30` §88.8, `30` §99.9`.
    - Módulos que não incluem `ENT-04` (M-00, M-02, M-03, M-04, M-07, M-09, M-12, M-13, M-14) permaneceram isolados e sem falso positivo.
  - Veredito da mutação: **DETETADA COM SUCESSO (Exit 1)**.

#### Mutação 2 (`medir-postgres.sh` — Mutação P-NEW)
- **Descrição da mutação:** Em cópia isolada de `evidencias/`, removeu-se `app.write_tenants` do `USING` da política RLS `p_member_role_isolation` em `pg-ddl.sql` (linha 142).
- **Resultado obtido:**
  - Código de saída: `1` (FALHA)
  - `matriz: 32 OK, 1 FALHA`
  - Saída de erro: `ERRO: matriz com falha ou contagem inesperada (esperado 33 OK e 0 FALHA; obtido 32 OK e 1 FALHA)`.
  - O teste `I30` (`INSERT INTO member_role ... RETURNING` por `TENANT_ADMIN` em novo tenant) falhou imediatamente porque a cláusula `RETURNING` avalia a condição do `USING` para o tenant que está apenas em `app.write_tenants`.
  - Veredito da mutação: **DETETADA COM SUCESSO (Exit 1)**.

#### Mutação 3 (`medir-env70.sh` — Mutação E-NEW)
- **Descrição da mutação:** Em cópia isolada de `evidencias/`, alterou-se em `env70/tests/IdentityService.Tests/Cunhagem.cs` o emissor esperado de `ValidIssuer = "identity-service"` para `ValidIssuer = "identity-service-MUTADO"`.
- **Resultado obtido:**
  - Código de saída: `1` (FALHA)
  - Execução dos testes: `Total de testes : 11`, `Aprovados : 10`, `Com falha : 1` (`Cunhagem.CunharComPayloadLiteral` falhou por rejeição do JWT).
  - Saída: `FALHA: Testes falharam ou código de saída ≠ 0 (1)`.
  - Veredito da mutação: **DETETADA COM SUCESSO (Exit 1)**.

---

## 3. Avaliação da Decisão D-21 e dos Pacotes da Onda 3 (M-01, M-02, M-03, M-05, M-10, M-14)

Conforme a Decisão D-21 (opção b), os agentes da Fase 3 trabalham em mundo fechado, recebendo exclusivamente o seu `pacote-M-nn.md`, sem acesso à especificação legada v1.0 (`spec_identity_service.md`). Foi realizada a leitura estrita de cada pacote em `pacotes/` e a comparação com a v1.0 e com `30-dados.md` e `40-contratos.md`:

1. **M-01 (`pacote-M-01.md`):**
   - Normalização e validação de `email`: linha EP-01 (`40` §4.1) especifica `trim()`, comprimento entre 5 e 320 caracteres, conformidade RFC 5322 via `MailAddress.TryCreate(email, out var a) && a.Address == email` (PRS-80), e conversão `ToLowerInvariant()` antes da busca (PRS-11).
   - Validação de `password`: sem `trim()`, 12 a 128 caracteres, classes `[A-Z]`, `[a-z]`, `[0-9]` e carácter especial (qualquer carácter fora de `[A-Za-z0-9]`, PRS-30).
   - Bloqueio e tempo uniforme: Argon2id aplicado em todos os ramos de 401 (e-mail inexistente, senha errada e conta `LOCKED`, D-08); bloqueio na 5.ª tentativa (PRS-31); transição para `LOCKED`, expiração em 900 s, gravação de `denylist:user` (IF-04.2), revogação de famílias (IF-01.4) e EV-03 (IF-07.3, D-09).
   - Rastreabilidade de auditoria e resposta em `view_mode = NONE` com `available_contexts` (RG-10) completamente especificadas.

2. **M-02 (`pacote-M-02.md`):**
   - Formato do refresh token: 32 bytes aleatórios em base64url (43 caracteres `^[A-Za-z0-9_-]{43}$`, PRS-34).
   - Deteção de reuso: reapresentação de token `CONSUMED` revoga toda a família (`IF-01.3`), grava `denylist:user` (`IF-04.2`), insere auditoria `TOKEN_REUSE_DETECTED`, emite EV-04 (`IF-07.5`) e responde 401 `ERR-AUTH-006`.
   - Revalidação do contexto guardado (D-19 / PRS-84): qualquer falha na revalidação (RG-02 a RG-05, RG-11, RG-13 via IF-09) revoga a família na BD, efetua commit e devolve o erro correspondente.
   - Concorrência e idempotência: UPDATE condicionado a `status = 'ACTIVE'`; se afetar 0 linhas ou disparar `23505` (`uq_refresh_token_family_active`), desfaz a transação e aplica o fluxo de reuso em nova transação (401 `ERR-AUTH-006`).

3. **M-03 (`pacote-M-03.md`):**
   - Ordem estrita de validação na camada 6 do EP-02: IF-09.1 (`iat`), IF-09.8 (existência de tenant e grupo), IF-09.2 (`CheckTenantAsync`), IF-09.3 (`CheckMembershipAsync`), IF-09.4 (`ResolveRolesAsync`) para `SINGLE`; e IF-09.5 para `MACRO_GROUP`.
   - Preservação do `family_id` com revogação do `jti` anterior (`IF-04.1`), emissão de novo token (`IF-06.1`) e gravação do contexto na família (`IF-01.2`).
   - Casos de borda: tratamento de família sem linha `ACTIVE` (401 `ERR-AUTH-002`, PRS-73); aceitação de `roles = []` com `permissions` não vazias em `MACRO_GROUP` (RG-13).

4. **M-05 (`pacote-M-05.md`):**
   - Inclusão e posse da interface `IF-12` (`IMembershipWriter`): operação `AddCreatorMembership` (IF-12.1) exigindo tenant no conjunto de escrita (`AuthorizeTenantWrite`), e `SetGroupMembershipsStatusAsync` (IF-12.2) executando em `ExecuteWithBypassAsync` sobre vínculos com `is_owner = false` (D-22).
   - Validações do EP-07: paridade tenant × utilizador (409 `ERR-CTX-005`), escopos do papel ativos no tenant (422 `ERR-CTX-006`), auditoria `MEMBER_ADDED`.

5. **M-10 (`pacote-M-10.md`):**
   - Separação entre perfis (D-07): `PLATFORM_ADMIN` opera suspensão global (`user_account.status`, `IF-02.1`, `denylist:user`, EV-03); `TENANT_ADMIN` opera suspensão restrita aos vínculos do grupo (`IF-12.2`, `IF-01.6`), sem alterar a conta e sem emitir `denylist:user` nem EV-03.
   - Condição C9 e D-22: verificação obrigatória de que o alvo não é o próprio chamador e que possui `IF-09.7 = MEMBER` (não é dono de nenhum tenant no grupo).
   - Tratamento de timestamp: um único instante `TimeProvider` por requisição para atualização dos vínculos e resposta do endpoint (PRS-82).

6. **M-14 (`pacote-M-14.md`):**
   - Parâmetros do barramento Kafka medidos: `MessageTimeoutMs = 3000`, `Acks = All` (PRS-63), `OUTBOX_POLLING_INTERVAL_MS = 500 ms`, lotes de até 50 mensagens `PENDING`, incremento de `retry_count` até 5 com transição para `FAILED` (PRS-43).
   - Montagem de headers e `context_jws` com RSA 2048 (`RS256`), expiração em `iat + 300 s` (PRS-09), serialização JSON determinística e mapeamento de todos os eventos EV-01 a EV-04.

7. **Cumprimento da Regra 1 da CR-16:**
   - Em todas as ocorrências de citações `PRS-nn` e `D-nn` nos ficheiros partilhados (`10`, `30`, `40`, `70`), o conteúdo normativo está explicitamente redigido na frase que o cita. Os IDs operam estritamente como etiquetas de proveniência, sem exigir leitura externa pelo construtor.

---

## 4. Tabela de Estado dos Achados Anteriores (A-100 a A-118)

| ID | Severidade Ciclo 4 | Descrição Resumida | Estado no Ciclo 5 | Evidência (Ficheiro:Linha) |
|---|---|---|---|---|
| **A-100** | **Alta ⚠️** | Fecho de referências cego a `00`/`60`/`80`/`90` e `§x` sem prefixo; citação `00 §3` em `10:10` e `10:111` | **RESOLVIDO** | `10-nucleo.md:24` (precedência de 6 linhas inscrita em `10` §1 sem citar `00`); `medir-pacotes.py:157,227-241` (alargamento de `REF_SECS` e `REF_INT`). Mutações comprovadas com Exit 1 |
| **A-101** | **Alta ⚠️** | Pacotes da Onda 3 dependiam da v1.0 ("mesmos EX", "da v1.0", regras omitidas) | **RESOLVIDO** | Decisão D-21 aplicada; `90-registo.md:27-44` (§1) e `90-registo.md:192,215-230` (§6) sem remissões à v1.0; regras de e-mail, senha, bloqueio, reuso e rotação escritas integralmente nos pacotes |
| **A-102** | **Alta ⚠️** | Matriz de isolamento não cobria `INSERT ... RETURNING` em `tenant_member`/`member_role` em tenant só de escrita | **RESOLVIDO** | `matriz-isolamento.sql:143-153` (testes I29 a I32 com `RETURNING`); `pg-ddl.sql:139-143` (RLS com `app.write_tenants` no `USING`); `30-dados.md:377-385` (§5.1 atualizado; 33 casos OK) |
| **A-103** | Média | CR-16 regra 1 violada em `40:95`: normalização de `email` não descrita na frase | **RESOLVIDO** | `40-contratos.md:95` (especificado `trim()`, 5 a 320, RFC 5322 e `ToLowerInvariant()` antes da pesquisa) |
| **A-104** | Média | D-19: indefinição sobre revogação de família nas novas falhas do EP-03 | **RESOLVIDO** | `90-registo.md:32` (UT-3-02), `10-nucleo.md:223` (PRS-84) e `pacote-M-02.md:456` (especificada revogação via IF-01.3 e commit em qualquer falha de revalidação) |
| **A-105** | Média | PRS-70..73 e CR-16 sem Q-nn | **RESOLVIDO** | Resolvido no ciclo 4 via Q-15 → Decisão D-23 |
| **A-106** | Média | Faixas de PRS desordenadas e contadores desatualizados | **RESOLVIDO** | `00-indice.md:164-173` (faixa UT-4 PRS-220..259 e UT-5 PRS-260..299 disjuntas; desvio PRS-70..73 registado com D-23; contadores de usados e livres atualizados) |
| **A-107** | Média | Regra D-22 não aplicada em C9 e IF-12.2 | **RESOLVIDO** | `40-contratos.md:172` (IF-12.2 filtra `is_owner = false`); `40-contratos.md:225` (C9 exige alvo ≠ `sub` e `IF-09.7 = MEMBER`) |
| **A-108** | Baixa | A-72 por resolver: aparente ciclo M-11 ↔ M-13 e omissão de consumidores | **RESOLVIDO** | `00-indice.md:37,42,70,73` (declaração explícita de dependência unidirecional M-11 → M-13 em runtime; M-13 consome apenas IF-08) |
| **A-109** | Baixa | Tabela "Pacote estimado" de `00:26-40` desatualizada face ao script | **RESOLVIDO** | `00-indice.md:26-40` (valores atualizados entre 58.8 e 59.7 KiB, alinhados com `medir-pacotes.py`) |
| **A-110** | Baixa | M-13 com 60.0 KiB, sem margem no teto | **RESOLVIDO** | `00-indice.md:39` e `medir-pacotes.py:116` (teto estimado ajustado para 18 KiB; pacote total em 59.6 KiB com ~400 B de margem) |
| **A-111** | Baixa | `90:340` indicava "CR-nn: Nenhum" | **RESOLVIDO** | Resolvido no ciclo 4; `90-registo.md:357` referencia CR-01 a CR-16 |
| **A-112** | Baixa | Ciclos desatualizados em cabeçalhos | **RESOLVIDO** | `20-sistema-existente.md:3` (ciclo 5); `30-dados.md:3` (ciclo 5); `90-registo.md:3` (ciclo 5) |
| **A-113** | Baixa | PRS-49 contraditório no registo | **RESOLVIDO** | Resolvido no ciclo 4 |
| **A-114** | Baixa | `70:129` generalizava 503 para todos os EP com chave indisponível | **RESOLVIDO** | `70-ambiente-teste.md:129` (distingue EP-01 e EP-03 que respondem 401 para credenciais/refresh inválidos sem acessar a chave, e elimina duplicação de EP-05) |
| **A-115** | Baixa | Citação incorreta `10 §3.7` em vez de `§3.11` em `70:77` | **RESOLVIDO** | `70-ambiente-teste.md:77` (corrigido para `10 §3.11`) |
| **A-116** | Baixa | IF-11.3 passo (5): 25P02 em transação abortada e ordem de reposição face ao commit | **RESOLVIDO** | `40-contratos.md:175` (especificado descarte do 25P02 e relançamento da exceção original; reposição antes do commit do chamador); `matriz-isolamento.sql:115-123` (testes I27a e I27b) |
| **A-117** | Baixa | `medir-postgres.sh` não validava medições M1–M9; `medir-env70.sh` gerava tabela estática | **RESOLVIDO** | `medir-postgres.sh:20-28` (validação de 22 medições M com fail-fast); `medir-env70.sh:64-101` (geração dinâmica a partir de `$MED_FILE` e saída 1 se testes falharem) |
| **A-118** | Baixa | Lacunas conceituais: definição de "especial" no PRS-30, `updated_at` no EP-09 GROUP, rotação concorrente no M-02 | **RESOLVIDO** | `40-contratos.md:95` e `10-nucleo.md:214` (carácter fora de `[A-Za-z0-9]`); `40-contratos.md:103` (instante `TimeProvider` preservado no EP-09 GROUP); `40-contratos.md:97` e `pacote-M-02.md:456` (concorrência tratada como reuso com 401 `ERR-AUTH-006`) |

---

## 5. Novos Achados Identificados (Ciclo 5)

### Achados Menores / Editoriais (Não Bloqueiam o Gate 2)

- **A-130 (Baixa — Editorial):** Na tabela do §1 de `90-registo.md` (linha 9), a coluna "Score / Gate" para a linha histórica `UT-1-01 Núcleo (10)` permaneceu com a redação desatualizada *"Gate 1 reprovado nos ciclos 1, 2, 3 e 4"*, quando o Gate 1 foi formalmente aceite no ciclo 4 (conforme `val-gate2-ciclo4.md` e `HANDOFF-ORQ.md:39`).
  - **Evidência:** `Etapa2/spec-v2/90-registo.md:9`.
  - **Correção requerida:** Atualizar a célula para *"Gate 1 aceite no ciclo 4; Gate 2 pendente"*.

---

## 6. O que está Bem e Consolidado

1. **Rigor e Determinismo Normativo:** Todos os documentos da fundação cumprem estritamente o RFC 2119/8174 e o vocabulário fechado (zero termos vagos e zero placeholders).
2. **Robustez dos Scripts de Teste e Isolamento:** Os 3 scripts de evidências (`medir-pacotes.py`, `medir-postgres.sh`, `medir-env70.sh`) estão blindados contra falsos positivos e reagem deterministicamente com código de saída 1 perante desvios estruturais ou quebras de contrato.
3. **Mundo Fechado e Autossuficiência dos Pacotes:** Os 15 pacotes de contexto cumprem integralmente o orçamento de 60 KiB (variando entre 38.9 KiB e 59.7 KiB), possuem fecho de referências 100 % verificado e permitem aos agentes da Onda 3 implementar os módulos sem necessidade de consultar documentos externos ou histórico prévio.
4. **Isolamento Multi-tenant e RLS:** A matriz de isolamento executada contra PostgreSQL 17.6 real cobre exaustivamente operações de leitura cruzada e escrita (incluindo `INSERT ... RETURNING` com `app.write_tenants`), com 33 casos de teste automatizados e aprovados.
5. **Classificação Real de Exceções:** O comportamento do ASP.NET Core / EF Core e dos drivers foi medido empiricamente e reflete com exatidão a hierarquia de `InnerException` para mapeamento de 503 (`ERR-SYS-002`), 403 (`ERR-CTX-007`) e 409 (`ERR-CTX-005`).

---

## 7. Veredito Final e Instruções para a Orquestração

### Veredito
- **Gate 1:** **CONFIRMADO ACEITE** (inalterado desde o ciclo 4).
- **Gate 2:** **ACEITE**.
- **Bloqueios:** **ZERO**.

### Próximos Passos Obrigatórios para o Orquestrador (ORQ)
1. **Registo:** Corrigir a linha 9 de `90-registo.md` conforme A-130 e atualizar o estado de Gate 2 para "Aceite no ciclo 5".
2. **Congelamento da Fundação:** Executar imediatamente o congelamento com hash SHA-256 dos ficheiros base:
   ```bash
   sha256sum Etapa2/spec-v2/{00-indice,10-nucleo,20-sistema-existente,30-dados,40-contratos,70-ambiente-teste}.md > Etapa2/spec-v2/spec-fundacao.sha256
   ```
   e registar os hashes em `90-registo.md` §9.
3. **Submissão ao PO:** Levar ao PO Matheus as pendências de negócio abertas:
   - **Q-16:** Aprovação dos PRS-80 a PRS-84 e PRS-86 (recomendação: aprovar). Bloqueia DoR do Gate 4.
   - **Q-17:** Confirmação sobre reemissão de eventos EV-02/EV-03 perante repetição de estado em EP-08/EP-09 (recomendação: sim). Bloqueia o início das UT-3-10 e UT-3-12.
4. **Desbloqueio da Onda 3:** Autorizar o lançamento dos agentes paralelos para a Onda 3 (UT-3-00 a UT-3-14), distribuindo exclusivamente os ficheiros `pacotes/pacote-M-nn.md` com os briefs definidos em `80-orquestracao.md` §4.1.

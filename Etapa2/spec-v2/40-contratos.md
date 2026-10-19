# 40 · Contratos partilhados — Identity-Service

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** ARQ (UT-2-02; reconciliado em UT-2-90) · **Estado:** Rascunho (ciclo 5, UT-2-91; decisões do PO até D-23 aplicadas) · **Atualizado em:** 2026-10-19
> Contrato partilhado, congelado no Gate 2. O **detalhe** de cada endpoint e evento fica no módulo dono (`50`); aqui fica a interface que os outros módulos usam. Nomes, enums e códigos vêm de `10` e `30`.
> **Pacotes (`00` §2.2):** §1 a §3 entram sempre (tamanho medido em `00` §2.3). De §4 e §5 entram só as linhas dos itens do módulo e das interfaces de que depende; em §4.3, regra própria de pacote. §6 entra só no pacote de M-14 (`00` §2.2). §7 é só para o ORQ e o PO.

## 1. Convenções globais da API

| Item | Valor |
|---|---|
| Base path | `/api/v1`. Exceção única: EP-10 em `/.well-known/jwks.json` |
| Pedido com corpo | `Content-Type` com tipo `application/json` (sem distinção de maiúsculas), seguido opcionalmente de `;` e do parâmetro `charset=utf-8` (nome e valor sem distinção de maiúsculas; espaços e tabs à volta de `;` e de `=` ignorados, RFC 9110 §8.3.1) e de nenhum outro parâmetro; corpo UTF-8 com no máximo 65 536 bytes (PRS-23) |
| Resposta de sucesso | `application/json; charset=utf-8`, sem envelope. 204 sem corpo |
| Resposta de erro | `10` §3.4 (RFC 9457, 6 campos: `type`, `title`, `status`, `code`, `detail`, `correlation_id`; nenhum outro) |
| Autenticação | `Authorization: Bearer <jwt>`: esquema `Bearer` sem distinção de maiúsculas, um espaço, JWT compacto. Claims em `10` §3.5. EP-01, EP-03 e EP-10 são públicos e ignoram o header `Authorization` |
| Correlação | `X-Correlation-Id` aceite só como UUID no formato `D` (36 caracteres, sem distinção de maiúsculas) e devolvido em minúsculas; outro valor ou ausente → UUID v7 gerado (PRS-52). Presente em todas as respostas, incluindo erros e 204 |
| Cache | `Cache-Control: no-store` nas respostas de EP-01 a EP-05 (com `Pragma: no-cache` exceto EP-04); EP-10: `public, max-age=300` (PRS-44) |
| JSON | `10` §3.3. Raiz do corpo: objeto. Campo desconhecido ou repetido no mesmo objeto → 400 `ERR-VAL-001`. Enums em texto com a grafia exata de `30` §2 (`"buyer"` → 400). Tipos estritos: número em texto, texto em número ou `0`/`1` em booleano → 400 |
| Query string | Nenhum EP define parâmetros de query; são ignorados (PRS-55) |
| Rejeição num módulo | O módulo lança `ApiErrorException(code)` (`M00Transversal/Errors/`); o tratador global de M-00 escreve o corpo com `status` e `title` de `10` §6 e classifica as outras exceções pela cadeia de `InnerException` (PRS-65): 23505 não tratado → 409 `ERR-CTX-005`. Os módulos **NÃO DEVEM** capturar exceções de BD, Redis ou chave; exceção única: o dono de uma restrição única trata o seu 23505 (M-02: `uq_refresh_token_family_active`), procurando a `PostgresException` na cadeia |

### 1.1 Rotas
Tabela normativa do M-00 (autenticação e bypass, `10` §3.4): o middleware identifica o EP pelo método e pelo path. O dono de cada EP regista a rota exatamente como abaixo.

| EP | Método e path | Token | `BypassTenantFilter` |
|---|---|---|---|
| EP-01 | `POST /api/v1/auth/login` | Público (`Authorization` ignorado) | `true` |
| EP-02 | `POST /api/v1/auth/switch-context` | Obrigatório | `true` |
| EP-03 | `POST /api/v1/auth/refresh` | Público (`Authorization` ignorado) | `true` |
| EP-04 | `POST /api/v1/auth/logout` | Obrigatório | `true` |
| EP-05 | `POST /api/v1/auth/impersonate` | Obrigatório | `true` |
| EP-06 | `POST /api/v1/tenants` | Obrigatório | Só com `platform_role = PLATFORM_ADMIN` |
| EP-07 | `POST /api/v1/tenants/{tenant_id}/members` | Obrigatório | Idem |
| EP-08 | `PATCH /api/v1/tenants/{tenant_id}/status` | Obrigatório | Idem |
| EP-09 | `PATCH /api/v1/users/{user_id}/status` | Obrigatório | Idem |
| EP-10 | `GET /.well-known/jwks.json` | Público | `false` (não lê a BD) |

### 1.2 Classificação de exceções (tratador global do M-00)
Cadeia = a exceção e as suas `InnerException` (o EF Core embrulha em `DbUpdateException` ou `InvalidOperationException`). Decide a primeira linha satisfeita por algum elemento da cadeia. Sem `EnableRetryOnFailure` (PRS-70).

| # | Elemento da cadeia | Resposta |
|---|---|---|
| 1 | Exceção exterior `ApiErrorException` | O seu código (`10` §6); fora do catálogo → 500 `ERR-SYS-001` |
| 2 | `PostgresException` com `SqlState` `42501` | 403 `ERR-CTX-007` (RG-09) |
| 3 | `PostgresException` com `SqlState` `23505` | 409 `ERR-CTX-005` |
| 4 | `PostgresException` com `SqlState` iniciado por `08` ou igual a `57P01`, `57P02`, `57P03`; `NpgsqlException` que não é `PostgresException`; `RedisConnectionException`; `RedisTimeoutException`; `CryptographicException` (falha de `ISigningKey`) | 503 `ERR-SYS-002` |
| 5 | Nenhuma linha anterior | 500 `ERR-SYS-001` |

## 2. Validação de entrada em camadas (ordem fixa)

Cada pedido percorre as camadas por esta ordem. A primeira que falha determina a resposta; as seguintes não são avaliadas. Nenhuma escrita (BD, Redis, outbox) ocorre antes da camada 6. A autenticação vem antes do formato para que um chamador sem token válido não obtenha informação sobre o corpo (PRS-53).

| # | Camada | Quem | Verificações, por ordem | Resposta |
|---|---|---|---|---|
| 0 | Encaminhamento | ASP.NET Core | Nenhuma rota com o path; path existe com outro método | 404 `ERR-REQ-001`; 405 `ERR-REQ-002` |
| 1 | Transporte | M-00, antes do `AuthContextMiddleware` | (a) Corpo acima de 65 536 bytes (por `Content-Length` ou na leitura); (b) corpo com 1 byte ou mais e `Content-Type` ausente, diferente de `application/json` ou com parâmetro diferente de `charset=utf-8`. Sem corpo, o `Content-Type` não é verificado. EP-10 não lê o corpo: camada não aplicada | 413 `ERR-REQ-003`; 415 `ERR-REQ-004` |
| 2 | Autenticação | M-00 `AuthContextMiddleware`, só nos EP com token obrigatório (EP-02, EP-04 a EP-09) | Primeiro lê a chave de `ISigningKey` (também sem token): indisponível → 503 `ERR-SYS-002` (PRS-71). Depois RG-06, RG-07, RG-08, por esta ordem (tabela abaixo) | 401 `ERR-AUTH-002`; 403 `ERR-AUTH-004`; 403 `ERR-CTX-002` |
| 3 | Formato | Endpoint do módulo dono | (a) Corpo vazio onde o EP exige corpo, bytes não UTF-8, JSON inválido ou raiz que não é objeto; (b) campo desconhecido ou repetido, obrigatório ausente ou `null`, tipo errado, valor fora do enum, texto com U+0000, UUID fora do formato `D`, parâmetro de path fora do formato UUID, limites estruturais da coluna "Request" de §4.1; (c) conteúdo de campo: `email`, `password`; `cnpj`, `geo_lat`, `geo_lng`; `ticket_id` | (a), (b): 400 `ERR-VAL-001`. (c): 422 `ERR-VAL-003`; 422 `ERR-VAL-002`; 422 `ERR-VAL-004` |
| 4 | Autorização | Endpoint, condição da matriz de autorização | Condição do EP falsa. Usa só as claims e, quando a condição o exige, a relação do recurso com o chamador; um recurso inexistente torna a condição falsa | 403 `ERR-CTX-007` (EP-05: 403 `ERR-AUTH-010`) |
| 5 | Existência | Endpoint | Recurso referenciado no pedido inexistente (só nos EP com 404 em §4.1) | 404 `ERR-CTX-008` |
| 6 | Regras de negócio | Módulo dono (ordem de avaliação do EP) e `IAccessQueries` | RN do módulo e RG de `10` §3.6, pela ordem do módulo dono | 401, 403, 409, 422 listados em §4.1 |

Em qualquer camada: dependência indisponível → 503 `ERR-SYS-002`; exceção não prevista → 500 `ERR-SYS-001`. A cardinalidade e os repetidos de `target_tenants` (EP-02) **não** são formato: são RG-11 na camada 6 (403 `ERR-CTX-004`).

**Rejeições do middleware (camada 2, avaliação #8).** RG-06 → 401 `ERR-AUTH-002`; RG-07 (`sub` e, com `act`, `act.sub`) → 403 `ERR-AUTH-004`; RG-08 → 403 `ERR-CTX-002` (`allowed_tenants` vazio nunca rejeita). Aplicam-se a **todos** os EP autenticados (EP-02, EP-04 a EP-09), qualquer que seja o corpo, o escopo, o papel ou a permissão, e constam de cada um como `[A]` em §4.1. Não escrevem na BD nem no Redis. O middleware lê só o Redis e a chave: `SUSPENDED` na BD é verificado na camada 6 (RG-04, RG-05 por `IAccessQueries`).

**Grupos de resposta usados em §4.1.** `[T]` = 413 `ERR-REQ-003`, 415 `ERR-REQ-004`. `[A]` = 401 `ERR-AUTH-002`, 403 `ERR-AUTH-004`, 403 `ERR-CTX-002`. Todos os EP têm ainda 404 `ERR-REQ-001`, 405 `ERR-REQ-002`, 500 `ERR-SYS-001` e 503 `ERR-SYS-002`.

**Erro de dado vs. erro HTTP.** Não há lotes nem importações. Qualquer violação é um erro HTTP do pedido inteiro. Um elemento inválido num array (`scopes`, `role_ids`, `target_tenants`) rejeita o pedido inteiro, sem efeitos parciais.

## 3. Checklist global de entrada malformada

Aplica-se aos EP com corpo (EP-01 a EP-09) e, no R10, a todos os EP. A resposta indicada pressupõe que as camadas anteriores passam (token válido nos EP autenticados). Nenhum destes casos pode devolver 5xx.

| # | Entrada | Resposta esperada |
|---|---|---|
| R1 | Corpo vazio (0 bytes) | EP-01, EP-02, EP-03, EP-05 a EP-09: 400 `ERR-VAL-001`. EP-04: aceite (204). EP-10: corpo ignorado |
| R2 | JSON malformado, truncado ou com raiz que não é objeto (`[]`, `"x"`, `null`, `1`) | 400 `ERR-VAL-001`. EP-04: qualquer corpo diferente de um objeto sem propriedades → 400 `ERR-VAL-001` |
| R3 | `null` dentro de uma lista (`scopes`, `role_ids`, `target_tenants`) | 400 `ERR-VAL-001` |
| R4 | Carácter NUL (`\u0000`) num texto | 400 `ERR-VAL-001` em qualquer campo de texto, antes das validações 422 da camada 3 (c) |
| R5 | Bytes que não são UTF-8 válido | 400 `ERR-VAL-001` |
| R6 | Texto fora do comprimento | `email` fora de 5 a 320 ou `password` fora de 12 a 128 → 422 `ERR-VAL-003`; `cnpj` sem exatamente 14 caracteres → 422 `ERR-VAL-002`; `ticket_id` fora da regex (máximo 21) → 422 `ERR-VAL-004`; `legal_name` fora de 2 a 200, `reason` fora de 5 a 255 (após `trim()`), `refresh_token` sem 43 caracteres → 400 `ERR-VAL-001`; corpo acima de 65 536 bytes → 413 `ERR-REQ-003`. Arrays: `scopes` fora de 1 a 4 ou com repetidos, `role_ids` fora de 1 a 10 ou com repetidos → 400 `ERR-VAL-001`; `target_tenants` fora de 1 a 50 ou com repetidos → 403 `ERR-CTX-004` (RG-11) |
| R7 | `Content-Type` ausente, errado ou com parâmetros vazios | Com corpo: ausente, outro tipo (`text/plain`, `application/xml`, `multipart/form-data`, `application/merge-patch+json`), `charset` diferente de `utf-8`, `charset=` vazio, `;` sem parâmetro ou outro parâmetro → 415 `ERR-REQ-004`. Sem corpo: não verificado |
| R8 | Paginação com valores negativos, zero ou acima do inteiro máximo | Não se aplica: sem paginação. A query string é ignorada (PRS-55): a resposta é a do mesmo pedido sem query |
| R9 | Multipart truncado | Não se aplica: sem upload. `multipart/*` → 415 `ERR-REQ-004` |
| R10 | Header com caracteres não ASCII | `KestrelServerOptions.RequestHeaderEncodingSelector` devolve `Encoding.Latin1` para todos os headers, para que a resposta saia do pipeline (PRS-54). `Authorization` → 401 `ERR-AUTH-002` nos EP autenticados, ignorado nos públicos; `X-Correlation-Id` → ignorado, UUID v7 gerado; `Content-Type` → 415 `ERR-REQ-004` (com corpo); `X-Forwarded-For` → IP de origem: 1.º valor aceite só se for um endereço IP válido com até 45 caracteres; senão o endereço da ligação (PRS-52); outros headers → ignorados |

## 4. Interfaces

### 4.1 Endpoints
Abreviaturas de erro: `VAL-001` = `ERR-VAL-001`, e assim para todas as áreas. `[T]`, `[A]` e os erros comuns a todos os EP: validação em camadas. Autorização exata: matriz de autorização.

| ID | Método e path | Dono (RF) | Autenticação / autorização | Request (resumo) | Sucesso (resumo) | Erros possíveis, além dos comuns |
|---|---|---|---|---|---|---|
| EP-01 | `POST /api/v1/auth/login` | M-01 (RF-01) | Pública | `email` texto: `trim()`; depois 5 a 320 caracteres e `MailAddress.TryCreate(email, out var a) && a.Address == email` (formato RFC 5322, PRS-80); depois `ToLowerInvariant()`, antes da pesquisa (PRS-11). `password` texto sem `trim()`: 12 a 128 caracteres com pelo menos uma de `[A-Z]`, `[a-z]`, `[0-9]` e um carácter especial = qualquer carácter fora de `[A-Za-z0-9]` (PRS-30) | 200: `access_token` (`view_mode = NONE`, PRS-01), `token_type` = `"Bearer"`, `expires_in` = 900, `refresh_token` (43 caracteres, PRS-34), `refresh_expires_in` = 604800, `user` {`id`, `email`, `name`, `platform_role`}, `available_contexts` [{`group_id`, `group_name`, `tenant_id`, `cnpj`, `legal_name`, `branch_type`, `is_owner`, `can_view_group`, `available_scopes`, `roles`}] (RG-10) | `[T]`; 400 VAL-001; 422 VAL-003; 401 AUTH-001 (e-mail inexistente; palavra-passe errada; conta `LOCKED`, qualquer que seja a palavra-passe, D-08). Os três casos executam uma verificação Argon2id com os parâmetros do RNF-01, para o tempo de resposta não os distinguir: e-mail inexistente contra um hash fictício; conta `LOCKED` contra o seu `password_hash`; 403 AUTH-004 (só `status = SUSPENDED`, só depois de a palavra-passe estar correta; a denylist não conta no login: RG-05, D-08, D-11). Sem 423 |
| EP-02 | `POST /api/v1/auth/switch-context` | M-03 (RF-02, RF-03) | JWT; token sem `act` | `view_mode` (`SINGLE` ou `MACRO_GROUP`; `NONE` → 400); `target_scope` (`OperationalScope`). `SINGLE`: `target_tenant_id` UUID obrigatório; `target_group_id` e `target_tenants` `null` ou omitidos. `MACRO_GROUP`: `target_group_id` UUID e `target_tenants` array de UUID obrigatórios; `target_tenant_id` `null` ou omitido. Combinação violada → 400 (PRS-57) | 200: `access_token`, `token_type`, `expires_in` = 900, `context` {`sub`, `group_id`, `tenant_id`, `allowed_tenants`, `view_mode`, `active_scope`, `roles`, `permissions`} (claims do novo token). O `jti` de entrada fica revogado; o contexto fica na família (IF-01). Sem `refresh_token` novo | `[T]`; `[A]`; 400 VAL-001; 403 CTX-007 (`act`); 401 AUTH-002 (família sem linha `ACTIVE`: IF-01.2 = `false`; transação desfeita, sem efeitos, PRS-73); 403 AUTH-004 (RG-05); 403 CTX-001 (RG-03); 403 CTX-002 (RG-04); 403 CTX-003 (RG-02, RG-13); 403 CTX-004 (RG-11). Sem 404: tenant inexistente falha a RG-03 (`SINGLE`) ou a RG-11 (`MACRO_GROUP`); grupo inexistente falha a RG-11 (PRS-58) |
| EP-03 | `POST /api/v1/auth/refresh` | M-02 (RF-04) | Pública (posse do `refresh_token`) | `refresh_token` texto `^[A-Za-z0-9_-]{43}$` | 200: `access_token` (contexto da família; `roles`/`permissions` recalculados, PRS-45), `token_type`, `expires_in` = 900, `refresh_token` novo, `refresh_expires_in` = 604800 | `[T]`; 400 VAL-001; 401 AUTH-005; 401 AUTH-006; 403 AUTH-004 (RG-05); 403 CTX-001 (RG-03); 403 CTX-002 (RG-04); 403 CTX-003 (RG-02, RG-13); 403 CTX-004 (RG-11). Revalida o contexto guardado (D-19): `SINGLE` por IF-09.2 a IF-09.4; `MACRO_GROUP` por IF-09.5 |
| EP-04 | `POST /api/v1/auth/logout` | M-09 (RF-05) | JWT; qualquer token válido, incluindo com `act` | Sem corpo ou objeto sem propriedades (`{}`) | 204 sem corpo. `jti` revogado; família `fid` revogada se o token tiver `fid` | `[T]` (só com corpo); `[A]`; 400 VAL-001 |
| EP-05 | `POST /api/v1/auth/impersonate` | M-06 (RF-06) | JWT; `platform_role` ∈ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} e token sem `act` | `target_user_id` UUID; `target_tenant_id` UUID; `target_scope` (`OperationalScope`); `ticket_id` texto `^[A-Z]{2,10}-[0-9]{1,10}$` (PRS-40) | 200: `access_token` (`SINGLE`, `act` = {`sub` do chamador, `ticket_id`}, sem `fid`, PRS-41), `token_type`, `expires_in` = 900, `impersonation_context` {`sub`, `group_id`, `tenant_id`, `allowed_tenants`, `view_mode`, `active_scope`, `roles`, `permissions`, `act`} (PRS-59). Sem `refresh_token` | `[T]`; `[A]`; 400 VAL-001; 422 VAL-004; 403 AUTH-010 (camada 4: chamador; camada 6: alvo com `platform_role ≠ NONE` ou alvo = `sub`); 404 CTX-008 (utilizador ou tenant alvo); 403 AUTH-004 (alvo `SUSPENDED`, `LOCKED` com `locked_until > agora`, ou com qualquer `denylist:user`: RG-05, PRS-46); 403 CTX-001; 403 CTX-002; 403 CTX-003 |
| EP-06 | `POST /api/v1/tenants` | M-04 (RF-07) | JWT; §5 | `group_id` UUID; `cnpj` texto (14 dígitos, módulo 11, PRS-37); `legal_name` texto (2 a 200 após `trim()`); `branch_type` (`HQ` ou `BRANCH`); `geo_lat` número [-90, 90]; `geo_lng` número [-180, 180]; `scopes` array de 1 a 4 valores distintos de ENUM-03 | 201 (depois da camada 4, o EP declara o `id` novo com a IF-08.2 `AuthorizeTenantWrite`), `Location: /api/v1/tenants/{id}`: `id`, `group_id`, `cnpj`, `legal_name`, `branch_type`, `geo_lat`, `geo_lng`, `status` = `ACTIVE`, `scopes`, `created_at`. EV-01 na outbox. Chamador `TENANT_ADMIN` (não `PLATFORM_ADMIN`): fica membro do tenant novo com `is_owner = false`, `can_view_group = false` (PRS-66) e o papel `GROUP_MANAGER`, pela IF-12.1; se `scopes` não tiver `TENANT_ADMIN`, este escopo é acrescentado e devolvido em `scopes` (D-04, PRS-67) | `[T]`; `[A]`; 400 VAL-001; 422 VAL-002; 403 CTX-007; 404 CTX-008 (`group_id`); 403 CTX-002 (grupo `SUSPENDED`); 409 CTX-005 (`cnpj`) |
| EP-07 | `POST /api/v1/tenants/{tenant_id}/members` | M-05 (RF-08) | JWT; §5 | Path `tenant_id` UUID. `user_id` UUID; `is_owner` booleano; `can_view_group` booleano; `role_ids` array de 1 a 10 UUID distintos (PRS-38) | 201 (depois da camada 4, o EP declara o `tenant_id` do path com IF-08 `AuthorizeTenantWrite`): `member_id`, `tenant_id`, `user_id`, `is_owner`, `can_view_group`, `status` = `ACTIVE`, `roles` (nomes), `created_at` | `[T]`; `[A]`; 400 VAL-001; 403 CTX-007 (inclui RG-09); 404 CTX-008 (tenant, utilizador ou papel); 409 CTX-005; 422 CTX-006 |
| EP-08 | `PATCH /api/v1/tenants/{tenant_id}/status` | M-12 (RF-09, RF-12) | JWT; §5 | Path `tenant_id` UUID. `status` (`ACTIVE` ou `SUSPENDED`); `reason` texto (5 a 255 após `trim()`, PRS-37) | 200: `tenant_id`, `status`, `updated_at`. `SUSPENDED`: famílias revogadas (IF-01), `denylist:tenant`, EV-02. `ACTIVE`: `DEL denylist:tenant` só na transição `SUSPENDED → ACTIVE` (PRS-15) | `[T]`; `[A]`; 400 VAL-001; 403 CTX-007; 404 CTX-008 |
| EP-09 | `PATCH /api/v1/users/{user_id}/status` | M-10 (RF-10, RF-13) | JWT; §5 | Path `user_id` UUID. `status` (`ACTIVE` ou `SUSPENDED`; `LOCKED` → 400); `reason` texto (5 a 255 após `trim()`) | 200: `user_id`, `status`, `scope`, `group_id`, `updated_at` (PRS-69). `PLATFORM_ADMIN` → `scope = GLOBAL`, `group_id = null`: `user_account.status` (IF-02); `SUSPENDED`: todas as famílias revogadas (IF-01.4), `denylist:user`, EV-03; `ACTIVE`: desbloqueia `LOCKED` (PRS-39); `denylist:user` não é apagada (PRS-48). `TENANT_ADMIN` → `scope = GROUP`, `group_id` do token, `updated_at` = instante da operação (`TimeProvider`, o mesmo gravado nos vínculos alterados; também sem alterações, PRS-82): só os vínculos `tenant_member` do alvo nos tenants desse grupo (IF-12.2); `SUSPENDED`: famílias do alvo com contexto nesses tenants revogadas (IF-01.6); sem `denylist:user` nem EV-03; `user_account` inalterado (D-07) | `[T]`; `[A]`; 400 VAL-001; 403 CTX-007; 404 CTX-008 |
| EP-10 | `GET /.well-known/jwks.json` | M-07 (RF-11) | Pública | Nenhum (corpo e query ignorados) | 200: `keys` [{`kty` = `RSA`, `use` = `sig`, `alg` = `RS256`, `kid` = `JWT_KEY_ID`, `n`, `e`}] (`n`, `e` em base64url) | Só os comuns (503 SYS-002: chave indisponível) |

### 4.2 Eventos
Publicados por M-14 a partir da outbox (RF-14). Headers, entrega e idempotência: definidos pelo M-14. Mensagem Kafka: chave = `partition_key`; valor = payload JSON UTF-8 (`10` §3.3).

| ID | Tópico | Dono (gatilho) | Chave de partição | Payload |
|---|---|---|---|---|
| EV-01 | `empresa_cadastrada` | M-14 (M-04, RF-07: EP-06 com 201) | `tenant_id` | `event_id`, `tenant_id`, `group_id`, `cnpj`, `legal_name`, `branch_type`, `geo_lat`, `geo_lng`, `status` = `ACTIVE`, `scopes`, `created_at` |
| EV-02 | `tenant_bloqueado` | M-14 (M-12, RF-09) | `tenant_id` | `event_id`, `tenant_id`, `group_id`, `cnpj`, `status` = `SUSPENDED`, `reason`, `blocked_by_user_id` (`sub` do chamador), `blocked_at` |
| EV-03 | `usuario_bloqueado` | M-14 (M-01, RF-01: bloqueio `LOCKED`; M-10, RF-10) | `user_id` | `event_id`, `user_id`, `status` (`LOCKED` ou `SUSPENDED`), `locked_until` (texto em `LOCKED`; `null` em `SUSPENDED`), `reason` (`SUSPENDED`: `reason` do pedido; `LOCKED`: `"Excesso de tentativas consecutivas de login com palavra-passe incorreta"`, PRS-60), `blocked_at` |
| EV-04 | `sessao_comprometida` | M-14 (M-02, RF-04: reuso) | `user_id` | `event_id`, `user_id`, `family_id`, `reused_refresh_token_id`, `ip_address`, `detected_at` |

### 4.3 Interfaces internas
Tipos públicos na pasta indicada; namespace = `IdentityService.Modules.<pasta com . no lugar de />` (por exemplo `IdentityService.Modules.M02Refresh.Interface`). Métodos `Async` recebem ainda `CancellationToken`. **Transação:** as IF de BD usam o `IdentityDbContext` com âmbito do pedido e a transação aberta pelo chamador; inserções ficam no `ChangeTracker` até ao `SaveChangesAsync` do chamador; atualizações em massa executam de imediato dentro dessa transação. Nenhuma IF faz commit. Dependência indisponível: a exceção propaga até ao tratador global do M-00. Argumento que viola o contrato: `ArgumentException` (500).
**Pacotes:** o consumidor recebe a linha da IF na tabela "Interfaces", só as linhas das operações que usa em "Operações" e as linhas de "Tipos partilhados" dos tipos que essas linhas citam; o dono recebe todas as linhas da sua IF.

**Interfaces**
| ID | Interface (pasta) | Dono | Regras comuns |
|---|---|---|---|
| IF-01 | `ISessionStore` (`M02Refresh/Interface/`) | M-02 | Revogar = `ACTIVE` e `CONSUMED` → `REVOKED`, `revoked_at` = agora; devolve `int` linhas revogadas, 0 é sucesso (idempotente) |
| IF-02 | `IUserAccountStatusWriter` (`M01Login/Interface/`) | M-01 | Transições: `ACTIVE`, `LOCKED` → `SUSPENDED`; `SUSPENDED`, `LOCKED` → `ACTIVE`; repetir o estado atual é aceite sem efeito no estado |
| IF-03 | `IAuditLog` (`M00Transversal/Audit/`) | M-00 | Colunas por evento na subsecção de `30` dos eventos do módulo; a linha grava-se com o `SaveChangesAsync` do chamador |
| IF-04 | `IDenylist` (`M00Transversal/Redis/`) | M-00 | `SET` antes do commit e `DEL` depois do commit (PRS-15): responsabilidade do chamador. Não existe operação que apague `denylist:user` ou `denylist:jti` (PRS-48) |
| IF-05 | `ISigningKey` (`M00Transversal/Auth/`) | M-00 | Lida em cada uso (nunca copiada para estado estático); sem chave válida no arranque o processo termina com código ≠ 0; uma falha em uso lança exceção (→ 503) |
| IF-06 | `ITokenIssuer` (`M07Chaves/Interface/`) | M-07 | Gera `jti` (UUID v7), `iat` (`TimeProvider`), `exp = iat + 900`, `iss`, `aud`, header `kid`; ordena os arrays (PRS-24) |
| IF-07 | `IEventOutbox` (`M14Eventos/Interface/`) | M-14 | Headers e `context_jws` montados por M-14 a partir do contexto de autenticação do pedido ou de `FamilyContext`; devolve `Guid` `event_id` (= `outbox_message.id`, UUID v7); linha `PENDING` gravada com o `SaveChangesAsync` do chamador |
| IF-08 | `IAuthContext` (`M00Transversal/Auth/`) | M-00 | Valores por pedido (*scoped*) |
| IF-09 | `IAccessQueries` (`M08Acessos/Interface/`) | M-08 | Resultado `AccessResult`/`AccessResult<T>`; `ErrorCode` = código da RG violada; arrays ordenados (PRS-24); o consumidor decide os efeitos de uma falha |
| IF-10 | `ITenantStatusWriter` (`M04Tenants/Interface/`) | M-04 | Transições: `ACTIVE` ↔ `SUSPENDED`; repetir o estado atual é aceite sem efeito no estado |
| IF-12 | `IMembershipWriter` (`M05Membros/Interface/`) | M-05 | Escreve `tenant_member` e `member_role` na transação do chamador |
| IF-11 | `ITenantIsolation` (`M13Isolamento/Interface/`) | M-13 | Registo no DI: `ITenantIsolation` *singleton*; os interceptores *scoped* (leem o `IAuthContext` do pedido). `IdentityDbContext` registado com `AddDbContext` (*scoped*), sem *pooling* |

**Operações**
| Operação | Assinatura | Efeito e resultado | Consumidores |
|---|---|---|---|
| IF-01.1 | `Task<NewSession> CreateFamilyAsync(Guid userId)` | Insere `refresh_token` `ACTIVE`, `view_mode = NONE`, `family_id` novo, `expires_at = created_at + 604800 s`; o texto do token só existe neste retorno | M-01 |
| IF-01.2 | `Task<bool> UpdateActiveContextAsync(Guid familyId, ViewMode viewMode, Guid groupId, Guid? activeTenantId, IReadOnlyList<Guid> allowedTenants, OperationalScope activeScope)` | Atualiza a linha `ACTIVE` da família. `false`: família sem linha `ACTIVE`, nada alterado. `viewMode = NONE` → `ArgumentException` | M-03 |
| IF-01.3 | `Task<int> RevokeFamilyAsync(Guid familyId)` | Revoga a família | M-09 |
| IF-01.4 | `Task<int> RevokeByUserAsync(Guid userId)` | Revoga todas as famílias do utilizador | M-01 (bloqueio por falhas, PRS-32), M-10 |
| IF-01.5 | `Task<int> RevokeByTenantAsync(Guid tenantId)` | Revoga as linhas com `active_tenant_id = t` ou `t` ∈ `allowed_tenants` | M-12 |
| IF-01.6 | `Task<int> RevokeByUserAndTenantsAsync(Guid userId, IReadOnlyList<Guid> tenantIds)` | Revoga as famílias do utilizador cuja linha `ACTIVE` tem `active_tenant_id` ou algum `allowed_tenants` em `tenantIds` | M-10 |
| IF-02.1 | `Task<UserStatus?> SetAdministrativeStatusAsync(Guid userId, UserStatus status)` | `status` ∈ {`ACTIVE`, `SUSPENDED`}; grava `status`, `failed_login_attempts = 0`, `locked_until = NULL`, `updated_at`. Devolve o estado anterior; `null`: utilizador inexistente, nada alterado. `LOCKED` → `ArgumentException` | M-10 |
| IF-03.1 | `void Append(AuditEventType eventType, Guid? userId, Guid? actorUserId, Guid? tenantId, Guid? groupId, string? ticketId, object? metadata)` | M-00 preenche `id` (UUID v7), `ip_address`, `correlation_id`, `created_at`, `metadata = {}` se `null`; se o token tiver `act`, grava `actor_user_id = act.sub` e `ticket_id = act.ticket_id` | M-01 a M-06, M-09, M-10, M-12 |
| IF-04.1 | `Task RevokeJtiAsync(string jti, long exp)` | `SET denylist:jti:{jti} "1"`, TTL `max(1, exp − agora)` | M-03, M-09 |
| IF-04.2 | `Task<long> BlockUserAsync(Guid userId)` | `SET denylist:user:{id}` = agora (segundos Unix), TTL 900; devolve o valor gravado | M-01, M-02, M-10 (só com `PLATFORM_ADMIN`, D-07) |
| IF-04.3 | `Task BlockTenantAsync(Guid tenantId)` | `SET denylist:tenant:{id} "1"`, TTL 900 (PRS-12) | M-12 |
| IF-04.4 | `Task UnblockTenantAsync(Guid tenantId)` | `DEL denylist:tenant:{id}`; só na transição `SUSPENDED → ACTIVE` | M-12 |
| IF-04.5 | `Task<long?> GetUserRevokedAtAsync(Guid userId)` | Valor de `denylist:user`; `null` sem chave | M-08 |
| IF-04.6 | `Task<IReadOnlySet<Guid>> GetBlockedTenantsAsync(IReadOnlyList<Guid> tenantIds)` | Tenants com `denylist:tenant`, num só `MGET` | M-08 |
| IF-05.1 | Propriedades `string KeyId` (= `JWT_KEY_ID`), `RsaSecurityKey SigningKey` (privada, 2048 bits), `string Modulus`, `string Exponent` (base64url) | Material de assinatura, de validação e de EP-10 | M-00 (validação), M-07, M-14 |
| IF-06.1 | `IssuedToken Issue(AccessTokenClaims claims)` | Devolve o token e as claims ordenadas, para o corpo de resposta. Combinação contrária às claims do núcleo (por exemplo `ViewMode.NONE` com `TenantId`) → `ArgumentException` | M-01, M-02, M-03, M-06 |
| IF-07.1 | `Guid EnqueueTenantCreated(TenantCreatedPayload payload)` | EV-01 | M-04 |
| IF-07.2 | `Guid EnqueueTenantBlocked(TenantBlockedPayload payload)` | EV-02 | M-12 |
| IF-07.3 | `Guid EnqueueUserLocked(Guid userId, DateTimeOffset lockedUntil, DateTimeOffset blockedAt)` | EV-03 com `status = LOCKED` | M-01 |
| IF-07.4 | `Guid EnqueueUserSuspended(Guid userId, string reason, DateTimeOffset blockedAt)` | EV-03 com `status = SUSPENDED` | M-10 |
| IF-07.5 | `Guid EnqueueSessionCompromised(SessionCompromisedPayload payload, FamilyContext family)` | EV-04 | M-02 |
| IF-08.1 | Propriedades de `IAuthContext` (tipos partilhados) | Só leitura. `BypassTenantFilter` = `true` nos pedidos a EP-01 a EP-05 (com ou sem token) e com `platform_role = PLATFORM_ADMIN`; `false` nos restantes | M-02 (`IpAddress`), M-03, M-04, M-05, M-06, M-09, M-10, M-12, M-13, M-14 |
| IF-08.2 | `void AuthorizeTenantWrite(Guid tenantId)` | Acrescenta `tenantId` a `WriteTenants`; chamado depois da camada 4 e antes de abrir a ligação da transação (PRS-51) | M-04 (EP-06), M-05 (EP-07) |
| IF-09.1 | `Task<AccessResult> CheckUserAsync(Guid userId, long? tRef)` | RG-05, PRS-46: `tRef = null` só verifica o estado (EP-01); `tRef = 0` rejeita com qualquer `denylist:user` (alvo de EP-05); senão `iat` (EP-02) ou `created_at` Unix do refresh apresentado (EP-03) | M-01, M-02, M-03, M-06 |
| IF-09.2 | `Task<AccessResult> CheckTenantAsync(Guid tenantId)` | RG-04 | M-02, M-03, M-06 |
| IF-09.3 | `Task<AccessResult> CheckMembershipAsync(Guid userId, Guid tenantId)` | RG-03 | M-02, M-03, M-06 |
| IF-09.4 | `Task<AccessResult<RoleSet>> ResolveRolesAsync(Guid userId, Guid tenantId, OperationalScope scope)` | RG-01, RG-02 | M-02, M-03, M-06 |
| IF-09.5 | `Task<AccessResult<MacroContext>> ResolveMacroGroupAsync(Guid userId, Guid groupId, IReadOnlyList<Guid> tenantIds, OperationalScope scope)` | Ordem fixa; a primeira falha devolve o seu código: (1) lista com menos de 1 ou mais de 50 elementos, ou com repetidos → RG-11; (2) grupo inexistente → RG-11; grupo `SUSPENDED` → RG-04; (3) para cada tenant pela ordem do pedido: inexistente ou de outro grupo → RG-11; sem vínculo `ACTIVE` com `can_view_group = true` → RG-11; tenant `SUSPENDED` ou com `denylist:tenant` → RG-04; RG-02 para `scope` → RG-02; (4) interseção (RG-12); `permissions` vazia → RG-13 | M-02, M-03 |
| IF-09.6 | `Task<AccessResult<IReadOnlyList<AvailableContext>>> ListContextsAsync(Guid userId)` | RG-10, na ordem da RG-10 | M-01 |
| IF-09.7 | `Task<GroupMembershipKind> GetGroupMembershipAsync(Guid userId, Guid groupId)` | Sobre os `tenant_member` do utilizador (qualquer `status`) nos tenants com `group_id = groupId`: nenhum → `NONE`; algum com `is_owner = true` → `OWNER`; senão `MEMBER`. Consulta dentro de `ExecuteWithBypassAsync` do M-13 (bypass só nesta operação, PRS-72); não usa o contexto do pedido | M-10 (condição C9) |
| IF-09.8 | `Task<TenantInfo?> ResolveTenantAsync(Guid tenantId)` | Dados do tenant sem filtro de isolamento (`tenant` não é `ITenantScoped`); `null` se não existir | M-03, M-06 |
| IF-10.1 | `Task<TenantStatusChange?> SetStatusAsync(Guid tenantId, EntityStatus status)` | Grava `tenant.status` e `updated_at` (= agora; com o estado repetido nada muda). `null`: tenant inexistente, nada alterado | M-12 |
| IF-12.1 | `void AddCreatorMembership(Guid tenantId, Guid userId)` | Insere `tenant_member` (`is_owner = false`, `can_view_group = false`, `status = ACTIVE`) e `member_role` com o papel `GROUP_MANAGER`; exige `tenantId` no conjunto de escrita (IF-08.2) | M-04 |
| IF-12.2 | `Task<IReadOnlyList<Guid>> SetGroupMembershipsStatusAsync(Guid userId, Guid groupId, EntityStatus status)` | Dentro de `ExecuteWithBypassAsync` do M-13 (PRS-72): lê os `tenant_member` do utilizador nos tenants do grupo com `is_owner = false` (D-22) e `status` diferente e muda-lhes `status` e `updated_at` com `ExecuteUpdateAsync`; devolve esses `tenant_id` (vazio é sucesso) | M-10 |
| IF-11.1 | `void ApplyQueryFilters(ModelBuilder modelBuilder, IdentityDbContext context)` | Camada 2: para cada entidade `ITenantScoped`, filtro `e => context.AuthContext.BypassTenantFilter \|\| context.TenantIsolationBypass \|\| context.AuthContext.AllowedTenants.Contains(e.TenantId)`; a referência à instância `context` faz o EF parametrizar o filtro por pedido. Chamado no `OnModelCreating` | M-11 |
| IF-11.2 | `IReadOnlyList<IInterceptor> CreateInterceptors(IServiceProvider requestServices)` | Devolve o `TenantSaveChangesInterceptor` (camada 3) e o `RlsConnectionInterceptor` (camada 4) do âmbito do pedido; o M-11 chama-o em `AddDbContext<IdentityDbContext>((sp, o) => o.AddInterceptors(isolation.CreateInterceptors(sp)))`; nunca no contexto de migração | M-11 |
| IF-11.3 | `Task<T> ExecuteWithBypassAsync<T>(IdentityDbContext context, Func<IdentityDbContext, Task<T>> operation)` | Bypass do isolamento só durante `operation` (PRS-72). (1) `context.TenantIsolationBypass = true` à entrada → `InvalidOperationException` (sem aninhamento); (2) ligação de `context` fechada → abre-a (`Database.OpenConnectionAsync`; a camada 4 corre na abertura); (3) `TenantIsolationBypass = true` (camadas 2 e 3) e `SELECT set_config('app.auth_flow', 'true', false)` na ligação (camada 4); (4) `await operation(context)`; (5) em `finally`, também com exceção: `TenantIsolationBypass = false`, `SELECT set_config('app.auth_flow', @b, false)` com `@b` = valor da camada 4 do pedido, e fecha a ligação se a abriu em (2). O fecho de (5) faz-se mesmo que a reposição falhe. Se `operation` lançou, uma falha da reposição (por exemplo 25P02 com a transação abortada) é descartada e relança-se a exceção de `operation`; senão, a falha da reposição propaga. Em ambos os casos o pedido termina em erro, o rollback do chamador desfaz o `set_config` feito na transação e a abertura seguinte de uma ligação redefine as variáveis (camada 4). Com sucesso, a reposição corre antes do commit do chamador. `operation` lê com `AsNoTracking` e escreve só com `ExecuteUpdateAsync`; **NÃO DEVE** chamar `SaveChangesAsync` nem deixar alterações no `ChangeTracker`. Usada só pelo M-08 e pelo M-05, nas operações do EP-09 | M-05, M-08 |

**Tipos partilhados** (registos C# `record` imutáveis; enums com os membros escritos exatamente como abaixo, guardados como texto)
| Tipo | Pasta | Definição |
|---|---|---|
| `EntityStatus` | `M11Esquema/Persistence/Enums/` | `enum { ACTIVE, SUSPENDED }` |
| `BranchType` | idem | `enum { HQ, BRANCH }` |
| `OperationalScope` | idem | `enum { BUYER, SUPPLIER, CARRIER, TENANT_ADMIN }` |
| `UserStatus` | idem | `enum { ACTIVE, LOCKED, SUSPENDED }` |
| `RefreshTokenStatus` | idem | `enum { ACTIVE, CONSUMED, REVOKED }` |
| `ViewMode` | idem | `enum { NONE, SINGLE, MACRO_GROUP }` |
| `PlatformRole` | idem | `enum { NONE, SUPPORT_AGENT, PLATFORM_ADMIN }` |
| `AuditEventType` | idem | `enum { LOGIN_SUCCESS, LOGIN_FAILED, USER_LOCKED, CONTEXT_SWITCHED, TOKEN_REFRESHED, TOKEN_REUSE_DETECTED, LOGOUT, IMPERSONATION_STARTED, TENANT_CREATED, MEMBER_ADDED, TENANT_STATUS_CHANGED, USER_STATUS_CHANGED }` |
| `OutboxStatus` | idem | `enum { PENDING, PUBLISHED, FAILED }` |
| `ActingScope` | idem | `enum { BUYER, SUPPLIER, CARRIER, TENANT_ADMIN, NONE }` |
| `IdentityDbContext` | `M11Esquema/Persistence/` | `DbContext` com as propriedades `IAuthContext AuthContext { get; }` (injetada) e `bool TenantIsolationBypass { get; set; }` (`false` ao criar; só `ExecuteWithBypassAsync` a altera) e um `DbSet` por tabela |
| `IAuthContext` | `M00Transversal/Auth/` | `interface`. Claims (`null` = nula ou omitida): `string?` `Jti`, `TicketId`; `Guid?` `UserId`, `FamilyId`, `GroupId`, `ActiveTenantId`, `ActorUserId`; `long?` `Iat`, `Exp`; `PlatformRole? PlatformRole`; `ViewMode? ViewMode`; `OperationalScope? ActiveScope`; `IReadOnlyList<Guid> AllowedTenants`; `IReadOnlyList<string>` `Roles`, `Permissions`. Pedido: `bool` `IsAuthenticated`, `BypassTenantFilter`; `Guid CorrelationId`; `string IpAddress`; `IReadOnlySet<Guid> WriteTenants`; `void AuthorizeTenantWrite(Guid tenantId)`. Sem token: `IsAuthenticated = false`, anuláveis `null`, coleções vazias; com token, `Jti`, `UserId`, `Iat`, `Exp`, `PlatformRole`, `ViewMode` ≠ `null` |
| `ITenantScoped` | `M13Isolamento/Interface/` | `interface { Guid TenantId { get; } }`, implementada pelas entidades de `tenant_scope`, `tenant_member` e `member_role` |
| `ApiErrorException` | `M00Transversal/Errors/` | `class : Exception { string Code }`; construtor `ApiErrorException(string code)` |
| `NewSession` | `M02Refresh/Interface/` | `record (string RefreshToken, Guid FamilyId, DateTimeOffset ExpiresAt)` |
| `FamilyContext` | `M14Eventos/Interface/` | `record (Guid UserId, ViewMode ViewMode, Guid? GroupId, Guid? ActiveTenantId, IReadOnlyList<Guid> AllowedTenants, OperationalScope? ActiveScope)` |
| `ActorClaim` | `M07Chaves/Interface/` | `record (Guid Sub, string TicketId)` |
| `AccessTokenClaims` | `M07Chaves/Interface/` | `record (Guid Sub, Guid? FamilyId, PlatformRole PlatformRole, ViewMode ViewMode, Guid? GroupId, Guid? TenantId, IReadOnlyList<Guid> AllowedTenants, OperationalScope? ActiveScope, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, ActorClaim? Act)`; `FamilyId = null` só em impersonation; `Act = null` fora de impersonation |
| `IssuedToken` | `M07Chaves/Interface/` | `record (string AccessToken, string Jti, long Iat, long Exp, AccessTokenClaims Claims)` |
| `AccessResult` | `M08Acessos/Interface/` | `record (bool Ok, string? ErrorCode)`; `Ok = true` ⇔ `ErrorCode = null` |
| `AccessResult<T>` | `M08Acessos/Interface/` | `record (bool Ok, T? Value, string? ErrorCode)`; `Ok = true` ⇔ `Value ≠ null` e `ErrorCode = null` |
| `RoleSet` | `M08Acessos/Interface/` | `record (IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)` |
| `MacroContext` | `M08Acessos/Interface/` | `record (IReadOnlyList<Guid> AllowedTenants, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)` |
| `AvailableContext` | `M08Acessos/Interface/` | `record (Guid GroupId, string GroupName, Guid TenantId, string Cnpj, string LegalName, BranchType BranchType, bool IsOwner, bool CanViewGroup, IReadOnlyList<OperationalScope> AvailableScopes, IReadOnlyList<string> Roles)` |
| `GroupMembershipKind` | `M08Acessos/Interface/` | `enum { NONE, MEMBER, OWNER }` |
| `TenantInfo` | `M08Acessos/Interface/` | `record (Guid TenantId, Guid GroupId, EntityStatus Status, EntityStatus GroupStatus)` |
| `TenantStatusChange` | `M04Tenants/Interface/` | `record (EntityStatus Previous, Guid GroupId, string Cnpj, DateTimeOffset UpdatedAt)`; `UpdatedAt` = `tenant.updated_at` depois da operação |
| `TenantCreatedPayload` | `M14Eventos/Interface/` | `record (Guid TenantId, Guid GroupId, string Cnpj, string LegalName, BranchType BranchType, decimal GeoLat, decimal GeoLng, IReadOnlyList<OperationalScope> Scopes, DateTimeOffset CreatedAt)`; `status = ACTIVE` é escrito por M-14 |
| `TenantBlockedPayload` | `M14Eventos/Interface/` | `record (Guid TenantId, Guid GroupId, string Cnpj, string Reason, Guid BlockedByUserId, DateTimeOffset BlockedAt)` |
| `SessionCompromisedPayload` | `M14Eventos/Interface/` | `record (Guid UserId, Guid FamilyId, Guid ReusedRefreshTokenId, string IpAddress, DateTimeOffset DetectedAt)` |

## 5. Matriz de autorização

Camada 4 (§2). Em cada EP autenticado, as rejeições `[A]` da camada 2 vêm antes de qualquer célula. Colunas de escopo: token com `platform_role = NONE`, `active_scope` = coluna, sem `act`. Colunas `SUPPORT_AGENT` e `PLATFORM_ADMIN`: `platform_role` = coluna, qualquer `view_mode`. Token com `view_mode = NONE` e `platform_role = NONE` (sem escopo): como a coluna `BUYER`. Token com `act`: avaliado pelas suas claims (`platform_role` do alvo), salvo onde a última coluna diz o contrário.

| EP | Sem token | BUYER | SUPPLIER | CARRIER | TENANT_ADMIN | SUPPORT_AGENT | PLATFORM_ADMIN | Condição exigida |
|---|---|---|---|---|---|---|---|---|
| EP-01 | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Nenhuma; `Authorization` ignorado |
| EP-02 | 401 `ERR-AUTH-002` | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Token sem `act`; com `act` → 403 `ERR-CTX-007` |
| EP-03 | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Nenhuma; `Authorization` ignorado |
| EP-04 | 401 `ERR-AUTH-002` | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Nenhuma, incluindo token com `act` |
| EP-05 | 401 `ERR-AUTH-002` | 403 `ERR-AUTH-010` | 403 `ERR-AUTH-010` | 403 `ERR-AUTH-010` | 403 `ERR-AUTH-010` | Permitido | Permitido | `platform_role` ∈ {`SUPPORT_AGENT`, `PLATFORM_ADMIN`} e token sem `act`; senão 403 `ERR-AUTH-010` |
| EP-06 | 401 `ERR-AUTH-002` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | Permitido se C6; senão 403 `ERR-CTX-007` | Como o seu `active_scope` | Permitido | `PLATFORM_ADMIN` **OU** C6: `active_scope = TENANT_ADMIN`, `tenant:criar` ∈ `permissions`, `group_id` do token = `group_id` do corpo |
| EP-07 | 401 `ERR-AUTH-002` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | Permitido se C7; senão 403 `ERR-CTX-007` | Como o seu `active_scope` | Permitido | `PLATFORM_ADMIN` **OU** C7: `active_scope = TENANT_ADMIN`, `membro:vincular` ∈ `permissions`, `tenant_id` do path ∈ `allowed_tenants` |
| EP-08 | 401 `ERR-AUTH-002` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | Permitido se C8; senão 403 `ERR-CTX-007` | Como o seu `active_scope` | Permitido | `PLATFORM_ADMIN` **OU** C8: `active_scope = TENANT_ADMIN`, `tenant_id` do path ∈ `allowed_tenants`, `tenant_member(tenant_id, sub).is_owner = true` (sem permissão `dominio:acao`; M-12 lê ENT-05, `30` §1) |
| EP-09 | 401 `ERR-AUTH-002` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | 403 `ERR-CTX-007` | Permitido se C9; senão 403 `ERR-CTX-007` | Como o seu `active_scope` | Permitido | `PLATFORM_ADMIN` **OU** C9: `active_scope = TENANT_ADMIN`, `usuario:suspender` ∈ `permissions`, alvo ≠ `sub`, IF-09.7 com o `group_id` do token = `MEMBER` (o alvo tem vínculo no grupo e nenhum como dono: só `PLATFORM_ADMIN` atua sobre donos, D-22; vale para `SUSPENDED` e `ACTIVE`, PRS-81) e o alvo tem `platform_role = NONE`. Efeito restrito aos vínculos do grupo (D-07) |
| EP-10 | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Permitido | Nenhuma |

**Consequências que os módulos donos DEVEM respeitar.** (1) C6 a C9 valem em `SINGLE` e em `MACRO_GROUP`. (2) Recurso inexistente torna C6, C7, C8 ou C9 falsa: 403 `ERR-CTX-007`; só `PLATFORM_ADMIN` chega ao 404 `ERR-CTX-008` (avaliação #9; prevalece sobre a ordem do RF-10 da v1.0). (3) Um tenant suspenso fica em `denylist:tenant` e com `SUSPENDED` na BD: um `TENANT_ADMIN` com esse tenant no token recebe 403 `ERR-CTX-002` na camada 2 e não obtém contexto novo nele (RG-04); a reativação desse tenant (RF-12) fica, na prática, para `PLATFORM_ADMIN`. (4) As permissões `tenant:criar`, `membro:vincular` e `usuario:suspender` existem só no papel `GROUP_MANAGER` do seed.

## 6. Eventos: headers e semântica comuns

**Headers Kafka** (valor em texto UTF-8; UUID em minúsculas, formato `D`). Um header sem valor fica **ausente**: nunca `""`, nunca `"null"`, nunca um valor fora do tipo (PRS-56). Nenhum outro header. `outbox_message.headers` (ENT-10) guarda só as chaves presentes, com valor texto.

| Header | Tipo | Presença |
|---|---|---|
| `event_id` | UUID | Sempre; = `outbox_message.id` = `event_id` do payload |
| `tenant_id` | UUID | Só com valor (tabela abaixo) |
| `group_id` | UUID | Só com valor |
| `initiator_user_id` | UUID | Sempre |
| `actor_user_id` | UUID | Só com valor |
| `acting_scope` | `ActingScope` | Sempre; `NONE` sem escopo ativo (PRS-18) |
| `correlation_id` | UUID | Sempre; correlação do pedido que gravou a mensagem (`10` §3.4) |
| `context_jws` | JWS compacto | Sempre; header JWS `alg = RS256`, `kid` = IF-05 `KeyId`; claims `event_id`, `tenant_id`, `group_id`, `initiator_user_id`, `actor_user_id`, `acting_scope`, `view_mode` (`ViewMode`), `allowed_tenants` (ordenado), `correlation_id`, `iat`, `exp = iat + 300` (PRS-09). Todas as claims presentes; sem valor → literal JSON `null` |

**Valores por gatilho**
| Gatilho | `tenant_id` | `group_id` | `initiator_user_id` | `actor_user_id` | `acting_scope` | `view_mode`, `allowed_tenants` (JWS) |
|---|---|---|---|---|---|---|
| EV-01 (EP-06) | Novo tenant | Grupo do novo tenant | `sub` | `act.sub`; ausente sem `act` | `active_scope` do token; `NONE` se `null` | Do token |
| EV-02 (EP-08) | Tenant do path | Grupo desse tenant | `sub` | `act.sub`; ausente sem `act` | `active_scope` do token; `NONE` se `null` | Do token |
| EV-03 (EP-01, bloqueio) | Ausente | Ausente | Utilizador bloqueado | Ausente | `NONE` | `NONE`, `[]` |
| EV-03 (EP-09) | `tenant_id` do token; ausente se `null` | `group_id` do token; ausente se `null` | Utilizador alvo | `act.sub` se o token tiver `act`; senão `sub` | `active_scope` do token; `NONE` se `null` | Do token |
| EV-04 (EP-03, reuso) | `active_tenant_id` da família; ausente se `null` | `group_id` da família; ausente se `null` | `refresh_token.user_id` | Ausente | `active_scope` da família; `NONE` se `null` | Da família |

| Item | Valor |
|---|---|
| Semântica de entrega | *At-least-once* pela outbox (ENT-10, RF-14, PRS-43): a mensagem é inserida na transação da alteração de negócio e publicada depois do commit; `PUBLISHED` só após confirmação do broker. Uma falha entre a confirmação e a atualização para `PUBLISHED` repete a publicação. `FAILED` não é reprocessada (fora do âmbito) |
| Ordem | Sem garantia entre mensagens (uma retentativa pode sair depois de mensagens mais recentes). Os consumidores **NÃO DEVEM** depender da ordem |
| Idempotência no consumidor | Chave: header `event_id` (igual a `event_id` do payload). Deduplicação e validação de `context_jws` pelos consumidores (com EP-10): fora do âmbito (PRS-14, L-02) |

## 7. PRS deste contrato e pedidos de alteração (só ORQ e PO)

**Pedidos de alteração a `10`, `30` e `00`:** todos aplicados em UT-2-90 (`90` §7.3). Os PRS do ciclo 4 (PRS-70 a PRS-73) estão em `10` §9.

**CR-16 (UT-2-91; Aplicado; aprovado por D-23). Convenção de citações em texto partilhado** (texto que entra em pacotes de mais de um módulo: núcleo base, extras partilhados, `40` §1 a §4, parte comum de `70`):
1. `PRS-nn` e `D-nn` são **etiquetas de proveniência**: o conteúdo normativo **DEVE** estar escrito na própria frase que os cita; a frase tem de ser cumprível sem ler o PRS ou a decisão. O script de fecho não exige a definição destes IDs no pacote.
2. Uma citação de secção (`` `nn` §x ``) é uma **dependência**: o pacote que contém a frase **DEVE** conter a secção citada. Se algum pacote não a contiver, ou a frase passa para um extra que só entra nos pacotes que têm essa secção, ou o conteúdo necessário é escrito na frase e a citação é retirada.
3. Os restantes IDs (ENUM, ENT, IF, RG, ERR, EP, EV, EXT, AUD) continuam dependências: definidos no mesmo pacote.

**PRS (estado e justificação em `90` §2.1; renumerados em UT-2-90 de PRS-46 a PRS-53 para PRS-53 a PRS-60):**
| ID | Pressuposto | Base |
|---|---|---|
| PRS-53 | Ordem das camadas: autenticação (camada 2) antes do formato (camada 3) | E1 §3, atividade 2.8, põe o formato antes da autenticação; o `AuthContextMiddleware` (`10` §3.4) corre antes do endpoint. Q-11 |
| PRS-54 | Headers de pedido decodificados como Latin-1 em Kestrel (R10) | Evita respostas 400 do servidor fora do formato RFC 9457 |
| PRS-55 | Query string ignorada em todos os EP | Nenhum EP da v1.0 define query |
| PRS-56 | Header Kafka sem valor fica ausente (nunca `""`) | Avaliação #11; a v1.0 usava `""` |
| PRS-57 | EP-02: em `SINGLE`, `target_group_id` e `target_tenants` `null` ou omitidos; em `MACRO_GROUP`, `target_tenant_id` `null` ou omitido; senão 400. `scopes` e `role_ids` com repetidos → 400 | Avaliação #10 |
| PRS-58 | EP-02 sem 404: tenant ou grupo inexistente falha a RG-03 ou a RG-11 | Avaliação #9 |
| PRS-59 | `impersonation_context` (EP-05) inclui `roles` e `permissions` | Avaliação #10; claims de `10` §3.5 |
| PRS-60 | `reason` de EV-03 no bloqueio por login = `"Excesso de tentativas consecutivas de login com palavra-passe incorreta"`; `actor_user_id` de EV-03 em EP-09 segue a regra de `act` de ENT-09 | Exemplo da v1.0 §6.3; avaliação #10 |

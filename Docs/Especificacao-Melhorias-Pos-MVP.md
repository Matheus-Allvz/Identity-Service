# Especificação das Melhorias Pós-MVP — Identity-Service B2B

> **Estado:** Aprovado para construção · **Data:** 2026-11-16
> **Origem:** [Guia-Onboarding-e-Matriz-Melhorias.md](Guia-Onboarding-e-Matriz-Melhorias.md) (Parte 1) e [Relatorio-Analise-e-Melhorias-Identity.md](Relatorio-Analise-e-Melhorias-Identity.md)
> **Relação com a spec v2:** a especificação congelada em `Etapa2/spec-v2/` (Gate 4, `spec.sha256`) **não é alterada**. Este documento é a adenda viva que estende os contratos `40` e os dados `30` para as seis melhorias abaixo; a rastreabilidade para o registo está em `Etapa2/spec-v2/90-registo.md` §12.

Ordem de construção: **da menor para a maior complexidade**. Cada melhoria é uma unidade de trabalho (UT) com um PR próprio e só é aceite depois dos três gates do §8.

| # | ID | Melhoria | Complexidade | Esforço | Novos contratos | Migração de BD |
|---|---|---|---|---|---|---|
| 1 | QW-02 | `GET /api/v1/auth/me` | Muito baixa | 1–2 h | EP-11 | Não |
| 2 | QW-03 | SDKs de verificação em `Docs/sdks/` | Baixa | 2–3 h | Nenhum (documentação executável) | Não |
| 3 | QW-04 | OpenAPI 3.1 + Swagger UI | Baixa | 2–3 h | EP-13, EP-14 | Não |
| 4 | QW-01 | Auto-contexto no login | Baixa | 3–4 h | Extensão opt-in do EP-01 | Não |
| 5 | NEW-06 | Client Credentials (M2M) | Média | 1–2 d | EP-12, ENT-11 | **Sim** |
| 6 | NEW-07 | Métricas e tracing OpenTelemetry | Média | 1–2 d | Nenhum contrato HTTP | Não |

Fora de âmbito (postergado para depois do MVP, sem alteração): EST-02 (MFA/Passkeys) e EST-04 (auditoria com encadeamento criptográfico).

Convenções herdadas e inegociáveis em todas as UT: RS256 com emissor único, erros RFC 9457 (`type`, `title`, `status`, `code`, `detail`, `correlation_id`), RLS do PostgreSQL, denylist Redis fail-closed, `Cache-Control: no-store` em respostas com tokens ou identidade, `dotnet build --warnaserror` limpo.

---

## 1. QW-02 — Inspeção de identidade (`GET /api/v1/auth/me`)

**Objetivo.** Devolver o contexto ativo do portador do token sem obrigar o cliente a decodificar Base64Url. É uma projeção pura do `IAuthContext` (IF-08): não lê base de dados nem escreve em cache.

### 1.1 Contrato (EP-11, dono: M-01)

| Campo | Valor |
|---|---|
| Método e path | `GET /api/v1/auth/me` |
| Autenticação | `Authorization: Bearer <access_token>` obrigatório. Aceita tokens `view_mode = NONE` (pré-contexto), contextualizados e de impersonation |
| Corpo do pedido | Nenhum |
| Sucesso | `200 OK`, `application/json`, `Cache-Control: no-store` |

```json
{
  "sub": "01900000-0000-7000-8000-000000000101",
  "platform_role": "NONE",
  "view_mode": "SINGLE",
  "group_id": "01900000-0000-7000-8000-000000000001",
  "tenant_id": "01900000-0000-7000-8000-000000000010",
  "allowed_tenants": ["01900000-0000-7000-8000-000000000010"],
  "active_scope": "TENANT_ADMIN",
  "roles": ["GROUP_MANAGER"],
  "permissions": ["mapa:visualizar", "membro:listar", "membro:vincular", "tenant:criar", "tenant:listar", "usuario:suspender"],
  "act": null,
  "jti": "019a4d3e-…",
  "iat": 1794866400,
  "exp": 1794867300
}
```

`act` é `null` ou `{ "sub": "<uuid do agente>", "ticket_id": "SUP-123" }` quando o token é de impersonation (RFC 8693). Em `view_mode = NONE`, `group_id`, `tenant_id` e `active_scope` são `null` e as listas vêm vazias.

| Erro | Quando |
|---|---|
| `401 ERR-AUTH-002` | Token ausente, malformado, expirado, com assinatura inválida ou `jti` na denylist |
| `403 ERR-AUTH-004` | Utilizador (ou agente de impersonation) suspenso ou revogado globalmente |
| `403 ERR-CTX-002` | Algum tenant de `allowed_tenants` bloqueado |
| `405 ERR-REQ-002` | Método diferente de `GET` |

### 1.2 Regras

- **RN-QW02.1** A resposta reflete exatamente as claims validadas pelo middleware (RG-06 a RG-08); nenhuma revalidação em BD.
- **RN-QW02.2** A rota entra em `IsTokenRequiredEndpoint`: sem `Authorization` responde `401 ERR-AUTH-002`.
- **RN-QW02.3** Ordenação das listas igual à do token (RN-97, ordinal ascendente).

### 1.3 Critérios de aceitação

| CA | Dado / Quando / Então |
|---|---|
| CA-QW02.1 | Token `SINGLE` válido → `200` com todas as claims iguais às do token e `Cache-Control: no-store` |
| CA-QW02.2 | Token `NONE` → `200` com `view_mode = NONE`, nulos e listas vazias |
| CA-QW02.3 | Token de impersonation → `200` com `act.sub` e `act.ticket_id` |
| CA-QW02.4 | Sem `Authorization` → `401 ERR-AUTH-002` em `application/problem+json` |
| CA-QW02.5 | `jti` revogado (logout) → `401 ERR-AUTH-002` |

### 1.4 Tarefas

- [x] `Modules/M01Login/MeEndpoint.cs` + `MapGet` em `MapM01Login`
- [x] `AuthContextMiddleware.IsTokenRequiredEndpoint`: incluir `/api/v1/auth/me`
- [x] Testes: `tests/IdentityService.Tests/AuthMeTests.cs` (CA-QW02.1 a .5)
- [x] Docs: `Alinhamento` §3.12, `Guia-Consumo` passo 3-B, Postman (pasta 3.1), `pages/js/catalog.js` (A14)

---

## 2. QW-03 — SDKs de verificação de token (`Docs/sdks/`)

**Objetivo.** Eliminar a barreira de entrada dos grupos consumidores: quatro ficheiros independentes, copiáveis, que validam o JWT offline via JWKS e expõem um contexto tipado e guardas de permissão.

### 2.1 Entregáveis

| Ficheiro | Stack | Dependências |
|---|---|---|
| `Docs/sdks/b2b_auth_fastapi.py` | Python / FastAPI | `PyJWT[crypto]` |
| `Docs/sdks/B2bAuthMiddleware.cs` | ASP.NET Core | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| `Docs/sdks/auth-middleware.ts` | Node.js / Express | `jose` |
| `Docs/sdks/jwt_validator.go` | Go `net/http` | `github.com/golang-jwt/jwt/v5` |
| `Docs/sdks/README.md` | — | Instalação, uso e limites |

### 2.2 Regras (iguais nos quatro)

- **RN-QW03.1** Só `RS256`; `iss = identity-service`; `aud = b2b-platform`; tolerância de relógio ≤ 60 s.
- **RN-QW03.2** JWKS de `{IDENTITY_BASE_URL}/.well-known/jwks.json` com cache local (~1 h, alinhado ao `Cache-Control`) e um único refetch em `kid` desconhecido.
- **RN-QW03.3** Token `view_mode = NONE` é rejeitado por omissão (`403 ERR-CTX-007`), com flag de opt-out.
- **RN-QW03.4** Falhas respondem RFC 9457: `401 ERR-AUTH-002` (token) e `403 ERR-CTX-007` (permissão, escopo ou contexto), propagando `X-Correlation-Id`.
- **RN-QW03.5** Cada ficheiro expõe: verificação → contexto tipado; guarda de permissão; guarda de escopo.
- **RN-QW03.6** Limite documentado: a validação offline não vê a denylist; logout e suspensão produzem efeito em até 15 min (vida do token).

### 2.3 Critérios de aceitação

| CA | Verificação |
|---|---|
| CA-QW03.1 | Os quatro ficheiros existem, não são compilados por nenhum `.csproj` do repositório e não contêm segredos |
| CA-QW03.2 | `python -m py_compile` passa; os restantes são revistos contra o contrato (sem toolchain obrigatória no CI) |
| CA-QW03.3 | O README mostra instalação e exemplo mínimo por linguagem e a tabela de claims |

### 2.4 Tarefas

- [x] Quatro ficheiros + README em `Docs/sdks/`
- [x] `Alinhamento` §3.3: apontar para `Docs/sdks/` como caminho recomendado
- [x] `Guia-Consumo` §4-B: secção "Validar o token no seu microsserviço"

Estado da verificação (CA-QW03.2): Python com autoteste embutido a passar; C# compilado em .NET 10 com avisos como erros e exercitado contra um JWKS local; TypeScript e Go **revistos mas não compilados** (sem `tsc` nem `go` no ambiente de construção) — o README instrui quem copia a correr `tsc --noEmit` e `go vet`.

---

## 3. QW-04 — OpenAPI 3.1 e Swagger UI

**Objetivo.** Publicar o contrato HTTP completo num documento importável (Postman, Insomnia) e navegável no browser.

**Decisão de desenho.** O documento é **escrito à mão e versionado** (`openapi.json`, recurso embebido). Os handlers leem o corpo cru (`HttpContext`) e devolvem tipos anónimos, por isso a geração automática do `Microsoft.AspNetCore.OpenApi` produziria um documento sem schemas de pedido, resposta e erro. Um teste de deriva garante que o ficheiro não se afasta das rotas reais.

### 3.1 Contrato (dono: M-15 Documentação)

| ID | Método e path | Resposta |
|---|---|---|
| EP-13 | `GET /openapi.json` | `200`, `application/json`, documento OpenAPI **3.1.0**; `Cache-Control: public, max-age=300` |
| EP-14 | `GET /swagger` | `200`, `text/html`, Swagger UI (versão fixa via CDN) a apontar para `/openapi.json` |

Ambas anónimas. Com `OPENAPI_ENABLED=false` as duas rotas não são mapeadas (`404 ERR-REQ-001`). Por omissão estão ativas.

### 3.2 Regras

- **RN-QW04.1** Todas as rotas mapeadas na aplicação (exceto `/openapi.json` e `/swagger`) constam em `paths` com o método correto, e vice-versa.
- **RN-QW04.2** Schema `Problem` (RFC 9457) reutilizado em todas as respostas de erro; `components.securitySchemes.bearerAuth` (`http`, `bearer`, `JWT`).
- **RN-QW04.3** Cada operação tem `operationId`, `tags`, exemplo de pedido com os dados do seed de demonstração e os códigos `ERR-*` possíveis.
- **RN-QW04.4** `servers`: URL pública de homologação e `http://localhost:8080`.

### 3.3 Critérios de aceitação

| CA | Dado / Quando / Então |
|---|---|
| CA-QW04.1 | `GET /openapi.json` → `200`, JSON válido com `openapi` a começar por `3.1` |
| CA-QW04.2 | Deriva: conjunto `{método, path}` do `EndpointDataSource` = conjunto do documento |
| CA-QW04.3 | `GET /swagger` → `200 text/html` contendo a referência a `/openapi.json` |
| CA-QW04.4 | `OPENAPI_ENABLED=false` → ambas `404 ERR-REQ-001` |

### 3.4 Tarefas

- [x] `Modules/M15Docs/openapi.json` (embebido), `ModuleRegistration.cs` (`MapM15Docs`)
- [x] Middleware de autenticação: rotas anónimas (um `Authorization` inválido não as quebra). A validação de transporte não precisou de exceção: `GET` sem corpo já passa
- [x] Testes: `M15DocsTests.cs` (CA-QW04.1 a .4), contra o `Program` real
- [x] Docs: `Alinhamento` §3.13, `Guia-Consumo` §5 (importar `openapi.json`), `catalog.js` (A15)
- [ ] **Regra permanente:** toda UT posterior que crie ou altere rota atualiza `openapi.json` (o CA-QW04.2 falha caso contrário)

---

## 4. QW-01 — Auto-contexto no login

**Objetivo.** Permitir que clientes automatizados obtenham um token já contextualizado numa única chamada, sem alterar o comportamento por omissão.

### 4.1 Contrato (extensão opt-in do EP-01, `POST /api/v1/auth/login`)

Campos novos e **opcionais** no corpo:

| Campo | Tipo | Regra |
|---|---|---|
| `auto_context` | boolean | Se `true` e o utilizador tiver **exatamente um** contexto disponível com **exatamente um** escopo, ativa-o (`SINGLE`) |
| `target_tenant_id` | string UUID (`D`) | Ativa esse tenant em `SINGLE`. Exige `target_scope` |
| `target_scope` | `BUYER` \| `SUPPLIER` \| `CARRIER` \| `TENANT_ADMIN` | Exige `target_tenant_id` |

Resposta `200`: a mesma do EP-01; quando um contexto é ativado, `access_token` já vem contextualizado e é acrescentado o objeto `context`, com a mesma forma do EP-02:

```json
{
  "access_token": "…", "token_type": "Bearer", "expires_in": 900,
  "refresh_token": "…", "refresh_expires_in": 604800,
  "user": { "id": "…", "email": "…", "name": "…", "platform_role": "NONE" },
  "available_contexts": [ … ],
  "context": {
    "sub": "…", "group_id": "…", "tenant_id": "…",
    "allowed_tenants": ["…"], "view_mode": "SINGLE",
    "active_scope": "TENANT_ADMIN", "roles": ["…"], "permissions": ["…"]
  }
}
```

Sem os campos novos, a resposta é byte a byte a de hoje (`view_mode: NONE`, sem `context`).

### 4.2 Regras

- **RN-QW01.1** Retrocompatibilidade: pedido só com `email` e `password` mantém validação, resposta e efeitos atuais.
- **RN-QW01.2** `target_tenant_id` e `target_scope` vêm os dois ou nenhum; tipos errados, UUID fora do formato `D`, escopo desconhecido ou `auto_context` não booleano → `400 ERR-VAL-001`. Chaves desconhecidas continuam a ser rejeitadas.
- **RN-QW01.3** Alvo explícito tem precedência sobre `auto_context`.
- **RN-QW01.4** A resolução do contexto usa as **mesmas** verificações do EP-02 em `SINGLE` (existência e estado do tenant, vínculo, escopo e papéis) e devolve os mesmos erros: `403 ERR-CTX-001`, `ERR-CTX-002`, `ERR-CTX-003`. A lógica é partilhada, não duplicada.
- **RN-QW01.5** Com alvo explícito inválido o login **falha por inteiro**: nenhuma sessão é criada e nada é persistido. As credenciais são sempre validadas primeiro, por isso credenciais erradas continuam a responder `401 ERR-AUTH-001` sem revelar nada sobre o tenant.
- **RN-QW01.6** `auto_context: true` sem alvo e com ambiguidade (0 contextos, mais de 1 contexto, ou mais de 1 escopo) **não é erro**: devolve o login padrão `NONE`.
- **RN-QW01.7** Quando ativa contexto, a família de refresh nasce já com o contexto (`refresh_token.view_mode = SINGLE`), e a auditoria regista `LOGIN_SUCCESS` e `CONTEXT_SWITCHED` na mesma transação.
- **RN-QW01.8** `MACRO_GROUP` não é suportado no login; continua exclusivo do EP-02.

### 4.3 Critérios de aceitação

| CA | Dado / Quando / Então |
|---|---|
| CA-QW01.1 | Corpo só com `email` e `password` → resposta sem `context` e token `NONE` (regressão) |
| CA-QW01.2 | `target_tenant_id` + `target_scope` válidos → token `SINGLE` com papéis e permissões; `context` presente; refresh subsequente mantém o contexto |
| CA-QW01.3 | `auto_context: true`, utilizador com 1 contexto e 1 escopo → token `SINGLE` |
| CA-QW01.4 | `auto_context: true`, utilizador multi-tenant → token `NONE`, sem `context`, `200` |
| CA-QW01.5 | Alvo sem vínculo → `403 ERR-CTX-001` e nenhuma linha nova em `refresh_token` |
| CA-QW01.6 | Só `target_scope`, ou `auto_context: "true"` (string) → `400 ERR-VAL-001` |
| CA-QW01.7 | Palavra-passe errada com alvo válido → `401 ERR-AUTH-001` |

### 4.4 Tarefas

- [x] `M03Contexto`: resolução `SINGLE` extraída para `SingleContextResolver`, usado por EP-02 e EP-01
- [x] `ISessionStore.CreateFamilyAsync`: contexto inicial opcional (`InitialSingleContext`)
- [x] `LoginEndpoint`: parsing dos campos novos, ramo de auto-contexto, resposta
- [x] Testes: `AuthLoginAutoContextTests` (CA-QW01.1 a .7; 13 casos), contra PostgreSQL e Redis reais, incluindo o refresh a herdar o contexto. Não foi criada uma classe separada no projeto de aceitação: os cenários ponta a ponta já estão cobertos aqui
- [x] Docs: `Alinhamento` §3.4-C, `Guia-Consumo` (passo 2-B), Postman, `catalog.js` (A16), `openapi.json`

---

## 5. NEW-06 — Autenticação máquina a máquina (Client Credentials)

**Objetivo.** Dar identidade própria a serviços (rotinas batch, workers, chamadas serviço a serviço) sem utilizador humano, com o mesmo formato de token que os consumidores já validam.

### 5.1 Dados (ENT-11 `service_account`, dono: M-16)

| Coluna | Tipo | Regra |
|---|---|---|
| `id` | `uuid` PK | `sub` do token |
| `client_id` | `varchar(64)` único | `^[a-z0-9][a-z0-9-]{2,63}$` |
| `client_secret_hash` | `varchar(255)` | Argon2id, mesmos parâmetros de `user_account` |
| `tenant_id` | `uuid` FK `tenant` | Tenant em nome do qual o serviço atua |
| `role_id` | `uuid` FK `role` | Papel único; dele derivam `active_scope` e `permissions` |
| `status` | `varchar(16)` | `ACTIVE` \| `SUSPENDED` |
| `created_at`, `updated_at` | `timestamptz` | — |

RLS ativo e forçado, com a mesma política das tabelas com `tenant_id` (`app.auth_flow = 'true'` ou tenant em `app.allowed_tenants`). `auth_audit_log.event_type` passa a aceitar `SERVICE_TOKEN_ISSUED`. Migração EF nova; o `InitialCreate` não é tocado.

### 5.2 Contrato (EP-12, `POST /api/v1/auth/token`, RFC 6749 §4.4)

| Campo | Valor |
|---|---|
| Autenticação do cliente | `client_id` e `client_secret` no corpo **ou** `Authorization: Basic base64(client_id:client_secret)` |
| `Content-Type` | `application/x-www-form-urlencoded` (padrão OAuth) ou `application/json` |
| Corpo | `grant_type=client_credentials` (obrigatório) |
| Sucesso | `200`, `Cache-Control: no-store`, `Pragma: no-cache` |

```json
{ "access_token": "<JWT RS256>", "token_type": "Bearer", "expires_in": 900 }
```

Sem refresh token (RFC 6749 §4.4.3): o serviço pede outro token quando faltar pouco para expirar.

Claims do token de serviço: as mesmas de um token `SINGLE` (`iss`, `aud`, `jti`, `iat`, `exp`, `sub` = `service_account.id`, `platform_role = NONE`, `view_mode = SINGLE`, `group_id`, `tenant_id`, `allowed_tenants = [tenant_id]`, `active_scope`, `roles`, `permissions`), **mais** `client_id` e `token_use = "service"`, e **sem** `fid` nem `act`.

| Erro | Quando |
|---|---|
| `400 ERR-VAL-001` | `grant_type` ausente ou diferente de `client_credentials`; credenciais ausentes; corpo malformado |
| `401 ERR-AUTH-001` | `client_id` desconhecido, segredo errado ou conta `SUSPENDED` (resposta indistinguível, verificação com tempo constante) |
| `403 ERR-CTX-002` | Tenant ou grupo da conta suspenso |
| `403 ERR-CTX-003` | Escopo do papel inativo no tenant |
| `415 ERR-REQ-004` | `Content-Type` não suportado |

### 5.3 Regras

- **RN-NEW06.1** Os erros seguem o padrão RFC 9457 da plataforma, não o formato `{"error": …}` do OAuth; é um desvio deliberado e documentado, para que os consumidores tratem um único formato.
- **RN-NEW06.2** Sem bloqueio por tentativas: o segredo tem ≥ 256 bits de entropia e um bloqueio daria a terceiros um vetor de negação de serviço sobre integrações.
- **RN-NEW06.3** Tokens de serviço **não** são aceites pelos endpoints do próprio Identity (sem `fid` nem `act` → `401 ERR-AUTH-002`, invariante RN-96 inalterada). Servem para os microsserviços de negócio.
- **RN-NEW06.4** Consumidores distinguem humano de serviço pela claim `token_use`; a ausência equivale a utilizador.
- **RN-NEW06.5** Revogação: suspender a conta impede novas emissões; tokens já emitidos expiram em ≤ 15 min; suspender o tenant aciona `denylist:tenant` e invalida de imediato nos serviços que consultam a denylist.
- **RN-NEW06.6** Auditoria `SERVICE_TOKEN_ISSUED` com `tenant_id`, `group_id` e `metadata.client_id` (o `user_id` fica nulo: a conta não é um `user_account`).
- **RN-NEW06.7** Provisionamento por seed ou SQL administrativo. Um endpoint de gestão de contas de serviço fica **fora desta UT**.

### 5.4 Critérios de aceitação

| CA | Dado / Quando / Então |
|---|---|
| CA-NEW06.1 | Credenciais válidas em form-urlencoded → `200`, JWT RS256 validável pelo JWKS, com `token_use = service`, `client_id`, sem `fid` |
| CA-NEW06.2 | Mesmas credenciais via `Authorization: Basic` e via JSON → `200` |
| CA-NEW06.3 | Segredo errado, `client_id` inexistente e conta suspensa → `401 ERR-AUTH-001` idênticos |
| CA-NEW06.4 | `grant_type=password` → `400 ERR-VAL-001` |
| CA-NEW06.5 | Tenant suspenso → `403 ERR-CTX-002` |
| CA-NEW06.6 | Token de serviço em `POST /api/v1/auth/switch-context` → `401 ERR-AUTH-002` |
| CA-NEW06.7 | `POST /api/v1/auth/introspect` com token de serviço → `active: true` |
| CA-NEW06.8 | Emissão grava `SERVICE_TOKEN_ISSUED` em `auth_audit_log` |
| CA-NEW06.9 | Migração aplica em base existente e RLS impede leitura de `service_account` fora do fluxo de autenticação |

### 5.5 Tarefas

- [x] Entidade `ServiceAccount`, mapeamento, migração `AddServiceAccount` (tabela, RLS, grant só de leitura, check de auditoria)
- [x] `AccessTokenClaims`/`TokenIssuer`: variante de serviço (`client_id`, `token_use`, sem `fid`), com invariante própria
- [x] `Modules/M16ContasServico/TokenEndpoint.cs` + registo; middlewares (rota anónima, form-urlencoded)
- [x] `seed-demo.sql`: `order-service` e `catalog-service`
- [x] Testes: `M16ContasServicoTests.cs` (CA-NEW06.1 a .9, invariante do emissor e aplicação do seed; 17 casos), contra PostgreSQL e Redis reais
- [x] Docs: `Alinhamento` §3.14, `Guia-Consumo` (passo 7), Postman (pasta 3.2), `catalog.js` (A17), `openapi.json`, README dos SDKs (`token_use`, `client_id`)

---

## 6. NEW-07 — Métricas e tracing OpenTelemetry

**Objetivo.** Dar visibilidade operacional (latência, erros por código, emissão de tokens, denylist, outbox) usando as APIs nativas do .NET e exportação OTLP.

### 6.1 Desenho

- **Instrumentação:** `System.Diagnostics.Metrics.Meter` e `ActivitySource`, ambos com o nome `IdentityService`, num único ponto (`M00Transversal/Telemetry`).
- **Exportação:** OTLP (gRPC) para um OpenTelemetry Collector, ativada **apenas** quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definida. Sem a variável não há exportador nem custo (caso dos testes e do desenvolvimento local).
- **Sinais:** métricas e traces. Os logs continuam em stdout (recolhidos pela plataforma).
- **Recurso:** `service.name = identity-service` (sobreponível por `OTEL_SERVICE_NAME`), `service.version`, `deployment.environment`.
- **Amostragem de traces:** `parentbased_traceidratio`, razão por `OTEL_TRACES_SAMPLER_ARG` (omissão `0.1`).

### 6.2 Catálogo de métricas

| Instrumento | Tipo | Etiquetas | Fonte |
|---|---|---|---|
| `http.server.request.duration` | histograma | rota, método, status | ASP.NET Core (nativa) |
| `identity.tokens.issued` | contador | `kind` = `user` \| `impersonation` \| `service`; `view_mode` | `TokenIssuer` |
| `identity.problems` | contador | `code` (`ERR-*`), `status` | `GlobalExceptionHandlerMiddleware` |
| `identity.denylist.hits` | contador | `kind` = `jti` \| `user` \| `tenant` | `AuthContextMiddleware` |
| `identity.jwks.requests` | contador | — | EP-10 |
| `identity.outbox.published` | contador | `topic` | `OutboxPublisherWorker` |
| `identity.outbox.failures` | contador | `topic`, `final` (`true` quando esgota as 5 tentativas) | `OutboxPublisherWorker` |
| Runtime .NET (`dotnet.*`) | várias | — | Instrumentação de runtime |

Taxa de 401/403 = `identity.problems` filtrado por `status`. Cardinalidade limitada: nenhuma etiqueta leva identificadores de utilizador, tenant ou token.

### 6.3 Tracing

- Um span de servidor por pedido HTTP (ASP.NET Core), com o atributo `correlation.id` igual ao `X-Correlation-Id`.
- Spans de cliente para PostgreSQL (Npgsql) e chamadas HTTP de saída.
- `/health/*` excluídos de traces.

### 6.4 Regras

- **RN-NEW07.1** Telemetria nunca altera o resultado de um pedido: falha de exportação é silenciosa.
- **RN-NEW07.2** Nenhum dado pessoal, segredo, token ou `jti` em etiquetas, atributos ou nomes de span.
- **RN-NEW07.3** Sem `OTEL_EXPORTER_OTLP_ENDPOINT`, o comportamento e as respostas são idênticos aos atuais.

### 6.5 Critérios de aceitação

| CA | Verificação |
|---|---|
| CA-NEW07.1 | `MeterListener` em teste: um login bem-sucedido incrementa `identity.tokens.issued{kind=user}` |
| CA-NEW07.2 | Pedido sem token a rota protegida incrementa `identity.problems{code=ERR-AUTH-002,status=401}` |
| CA-NEW07.3 | `jti` revogado incrementa `identity.denylist.hits{kind=jti}` |
| CA-NEW07.4 | Aplicação arranca e responde sem `OTEL_EXPORTER_OTLP_ENDPOINT` (suíte inteira verde) |
| CA-NEW07.5 | Em homologação, com o endpoint configurado, as séries `identity_*` aparecem no backend de métricas e há traces do serviço |

### 6.6 Tarefas

- [ ] Pacotes OpenTelemetry (hosting, ASP.NET Core, runtime, OTLP) + `Npgsql.OpenTelemetry`
- [ ] `M00Transversal/Telemetry/IdentityTelemetry.cs` e registo em `AddM00Transversal`
- [ ] Pontos de instrumentação do §6.2; `correlation.id` no span
- [ ] `docker-compose.yml`: variáveis `OTEL_*` e rede do collector
- [ ] Testes: `TelemetryTests.cs` (CA-NEW07.1 a .4)
- [ ] Docs: `Docs/Observabilidade.md` (catálogo, consultas, painel), `Arquitetura-Alta-Disponibilidade` (nota), README

---

## 7. Impacto consolidado nos contratos

| ID | Método e path | Módulo | UT |
|---|---|---|---|
| EP-01 (estendido) | `POST /api/v1/auth/login` (+ `auto_context`, `target_tenant_id`, `target_scope`) | M-01 | QW-01 |
| EP-11 | `GET /api/v1/auth/me` | M-01 | QW-02 |
| EP-12 | `POST /api/v1/auth/token` | M-16 | NEW-06 |
| EP-13 | `GET /openapi.json` | M-15 | QW-04 |
| EP-14 | `GET /swagger` | M-15 | QW-04 |
| ENT-11 | `service_account` | M-16 | NEW-06 |

Para o gateway dos consumidores: `/api/v1/auth/me` e `/api/v1/auth/token` ficam sob o prefixo `/api/v1/auth/`, já encaminhado; `/openapi.json` e `/swagger` precisam de regra própria apenas se forem expostos pelo gateway.

Códigos de erro: nenhum código novo; reutiliza-se o catálogo existente.

---

## 8. Gates por UT (definição de pronto)

| Gate | Verificação | Comando ou evidência |
|---|---|---|
| **G1 · Não-regressão** | Compila sem avisos e toda a suíte passa (unitários + aceitação), incluindo os testes novos da UT | `dotnet build --warnaserror` e `dotnet test` |
| **G2 · Documentação sincronizada** | `Alinhamento-Ecossistema-B2B.md`, `Guia-Consumo-Testes-B2B.md`, `postman-collection-b2b.json`, `pages/js/catalog.js` e, a partir da QW-04, `openapi.json` refletem a UT | Diff do PR + CA-QW04.2 |
| **G3 · Privacidade** | Commits, PR e documentos públicos sem nomes de organizações privadas nem detalhes de infraestrutura privada | Revisão do diff |

A contagem canónica de testes é atualizada no Guia a cada UT (linha de base: 419).

## 9. Quadro de acompanhamento

| UT | Branch | Estado | PR | Testes novos |
|---|---|---|---|---|
| Documentação (este ficheiro) | `docs/ma/onboarding-e-matriz-melhorias` | Concluída | #16 | — |
| QW-02 | `feat/ma/auth-me` | Concluída | #17 | 5 (`AuthMeTests`) → total 424 |
| QW-03 | `feat/ma/sdks-verificacao` | Concluída | #18 | Nenhum no repositório (ficheiros fora da solução) |
| QW-04 | `feat/ma/openapi-swagger` | Concluída | #19 | 4 (`M15DocsTests`) → total 428 |
| QW-01 | `feat/ma/login-auto-contexto` | Concluída | #20 | 13 (`AuthLoginAutoContextTests`) → total 441 |
| NEW-06 | `feat/ma/client-credentials` | Concluída | #21 | 17 (`M16ContasServicoTests`) → total 458 |
| NEW-07 | `feat/ma/opentelemetry` | Por iniciar | — | — |

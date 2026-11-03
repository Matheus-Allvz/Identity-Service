# 10 · Núcleo — Identity-Service

> **Conjunto:** [00 Índice](00-indice.md) · **Dono:** ARQ (UT-1-01) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Núcleo base** (todos os pacotes): tudo antes da secção final "Extras". Cada extra diz no título em que pacotes entra. Estado dos PRS: `90` §2.

## 1. Instruções obrigatórias para a LLM

1. Implemente **exclusivamente** o que está no seu pacote de contexto. Qualquer endpoint, campo, tabela, dependência ou comportamento que não esteja descrito **NÃO DEVE** ser criado.
2. **DEVE**, **NÃO DEVE**, **DEVERIA**, **NÃO DEVERIA** e **PODE** têm o significado da RFC 2119/8174.
3. Em conflito vale, por ordem: (1) RN do módulo dono e RG (§3.6); (2) CA do módulo dono; (3) contratos (`40` e detalhe do EP no módulo); (4) dados (`30`); (5) restante núcleo; (6) plano de fases e restantes. Uma RN contra uma RG é defeito: registe-o em "Dúvidas", não o resolva por interpretação.
4. Use **apenas** as tecnologias e versões da §4.
5. Respeite **literalmente** os nomes de entidades, colunas, campos JSON, enums, tópicos, chaves de cache e códigos de erro.
6. **Se algo estiver ambíguo ou em falta, NÃO invente.** Registe-o em "Dúvidas" na sua entrega e siga com a hipótese mais simples, marcada como tal. Toda a decisão tomada sem base na especificação vai para "Decisões fora dos docs".
7. Não toque em nada listado em "Não tocar ainda" no seu brief.
8. Cada CA da sua fase tem um teste com o ID no nome (convenção em `70`).
9. Entregue no formato fixo do seu brief (`80`).

## 2. Contexto

### 2.1 Propósito
Autenticar pessoas físicas de um portal B2B multi-CNPJ, emitir JWT RS256 com o contexto ativo (`SINGLE` ou `MACRO_GROUP`) num dos 4 escopos, revogar sessões de imediato pela denylist no Redis e publicar eventos de identidade no Kafka com contexto assinado. [DOC] `Docs/Ideia Base.md`.

### 2.4 Glossário
| Termo | Nome canónico | Sinónimos proibidos |
|---|---|---|
| Grupo Económico | `economic_group` | `Account`, `Holding` |
| Tenant (pessoa jurídica, um CNPJ) | `tenant` | `Company`, `Filial` |
| Escopo operacional | `OperationalScope` | `Profile`, `Persona` |
| Conta de utilizador (pessoa física) | `user_account` | `Person`, `LoginUser` |
| Membro; papel; papel do membro | `tenant_member`; `role`; `member_role` | `Membership`; `Group`; `UserRole` |
| Refresh token; família | `refresh_token`; `family_id` (claim `fid`) | `SessionToken`; `session_id` |
| Modo de visão | `ViewMode` | `ContextMode` |
| Contexto de autenticação | `AuthContext` | `UserContext`, `TenantContext` |
| Auditoria; mensagem de outbox | `auth_audit_log`; `outbox_message` | `AuditTrail`; `EventQueue` |
| Denylist; contexto assinado de evento | `denylist:*`; `context_jws` | `blacklist`; `event_token` |
Classes C# com o nome da tabela em `PascalCase` (`TenantMember`, `RefreshToken`).

## 3. Restrições arquiteturais

### 3.1 ADR
| ID | Decisão | Consequência obrigatória no código | Origem (IB = [DOC] Ideia Base) |
|---|---|---|---|
| ADR-01 | Control Plane separado | `user_account.platform_role` (`NONE`, `SUPPORT_AGENT`, `PLATFORM_ADMIN`); nenhuma `role` concede privilégio de plataforma | IB §2 |
| ADR-02 | `economic_group` → `tenant` | `tenant.group_id NOT NULL`; token com `view_mode ≠ NONE` tem `group_id ≠ null` | IB §2, IB §3.1 |
| ADR-03 | Quatro escopos | `OperationalScope` com 4 valores (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`); outro valor → 400 `ERR-VAL-001` | IB §2, IB §3.2 |
| ADR-04 | Troca explícita de contexto | EP-01 emite `view_mode = NONE`; só EP-02 ativa `SINGLE` ou `MACRO_GROUP` | IB §5; PRS-01 |
| ADR-05 | Isolamento em 4 camadas | `tenant_scope`, `tenant_member`, `member_role` são `ITenantScoped`: middleware, filtro de leitura, interceptor de escrita e RLS (detalhe no extra do M-13) | IB §7.2; PRS-07, PRS-17 |
| ADR-06 | JWT RS256 + JWKS + denylist | RSA 2048, `kid` no header; EP-10 publica a chave; middleware aplica RG-06 a RG-08 | IB §2, IB §6.2; PRS-04 |
| ADR-07 | Rotação com deteção de reuso | `refresh_token` guarda só `token_hash`; reapresentar `CONSUMED` revoga a família | IB §6.3 |
| ADR-08 | Impersonation RFC 8693 | Claim `act`; sem `refresh_token`; token com `act` rejeitado em EP-02 e EP-05 | IB §6.4; PRS-08 |
| ADR-09 | Contexto assinado nos eventos | Toda a `outbox_message` leva headers de identidade e `context_jws` | IB §6.5; PRS-09 |
| ADR-10 | Monólito modular | Um projeto (§3.2); um módulo só escreve na sua pasta e nas tabelas de que é dono; chama outro módulo só pelas `IF-nn` (`40`) | ARQ |
| ADR-11 | Um único mecanismo de migração | EF Core Migrations aplicadas no arranque pelo M-11 | ARQ |

### 3.2 Estrutura do projeto
```text
IdentityService.slnx, global.json, .config/dotnet-tools.json, Directory.Build.props, Dockerfile, docker-compose.yml
src/IdentityService/Program.cs   chama AddMnn() e MapMnn() de cada módulo
src/IdentityService/Modules/Mnn<Nome>/   uma pasta por módulo (M-00 a M-14), com Interface/ (tipos públicos) e ModuleRegistration.cs
  M11Esquema/Persistence/: IdentityDbContext, entidades, configurações EF, Migrations/
tests/IdentityService.Tests/Mnn<Nome>/   testes do construtor
tests/IdentityService.Acceptance/   suite do AUD: o construtor NÃO DEVE criá-la nem alterá-la
```
**Dependências:** `Modules/Mxx/` só referencia `Modules/Myy/Interface/`, `Modules/M00Transversal/` e `Modules/M11Esquema/Persistence/`. Endpoints em Minimal APIs. Novos `PackageReference` proibidos: os pacotes são criados uma vez (M-11, F-00).

### 3.3 Convenções
| Item | Convenção |
|---|---|
| Classes / ficheiros | `PascalCase`, um tipo público por ficheiro |
| Tabelas / colunas | `snake_case` singular (`UseSnakeCaseNamingConvention()`); nomes de restrições em `30` |
| JSON | `JsonNamingPolicy.SnakeCaseLower`; `JsonUnmappedMemberHandling.Disallow`; enums como texto |
| Datas | JSON: UTC `yyyy-MM-ddTHH:mm:ssZ` truncado ao segundo; `iat`, `exp` e valores Redis em segundos Unix |
| Tempo | Só `TimeProvider` injetado |
| Identificadores | UUID v7 gerado pela aplicação; texto minúsculo, formato `D` |
| Arrays de saída | Ordem ordinal ascendente em `roles`, `permissions`, `allowed_tenants`, `scopes`, `available_scopes` (PRS-24) |
| Commits | `tipo(Mnn): descrição (RF-nn, CA-nn.m)` |

### 3.4 Comportamentos transversais (donos: M-00; isolamento M-13)
| Aspeto | Especificação |
|---|---|
| Autenticação | EP com token obrigatório (EP-02, EP-04 a EP-09): leitura da chave de `ISigningKey` (indisponível → 503, também sem token), depois RG-06, RG-07, RG-08 por esta ordem |
| Erro | RFC 9457 `application/problem+json; charset=utf-8`: `type` = `https://identity.b2b.local/errors/{code}`, `title` e `status` do §6, `code`, `detail` = `title` (PRS-26), `correlation_id`; sem stack trace |
| Logging | `AddJsonConsole()` para `stdout`. **NÃO DEVE** registar `password`, `password_hash`, `refresh_token`, `token_hash`, `access_token`, `context_jws`, chaves privadas nem corpos de pedido |
| Auditoria | `IAuditLog` insere em `auth_audit_log` na transação da operação, com as colunas dos eventos do módulo (`30`) |
| Denylist | Só `IDenylist` acede a `denylist:*` |
| Escrita em `ITenantScoped` | Um endpoint de gestão só escreve no tenant que a autorização do EP aprovou, declarado por `AuthorizeTenantWrite` antes de abrir a transação (PRS-51). Exceção: EP-09 por `TENANT_ADMIN`, pelo bypass limitado do M-13 (PRS-72) |

### 3.5 Claims do `access_token` e `AuthContext`
Header: `alg = RS256`, `typ = JWT`, `kid = JWT_KEY_ID`. Todas presentes salvo "omitida"; valor nulo = literal JSON `null`. Tipos C# do `AuthContext`: `40`, tipos partilhados.

| Claim | Tipo | Valor | `AuthContext` |
|---|---|---|---|
| `jti` | string | UUID v7 novo | `Jti` |
| `sub` | string | `user_account.id` (impersonation: o alvo) | `UserId` |
| `fid` | string | `family_id`; omitida em impersonation | `FamilyId` |
| `iat`, `exp` | número | Unix; `exp = iat + 900` (PRS-20) | `Iat`, `Exp` |
| `iss`, `aud` | string | `JWT_ISSUER`, `JWT_AUDIENCE` | — |
| `platform_role` | string | `PlatformRole` de `sub` | `PlatformRole` |
| `view_mode` | string | `ViewMode` | `ViewMode` |
| `group_id`, `tenant_id` | string \| `null` | `null` em `NONE`; `tenant_id = null` em `MACRO_GROUP` | `GroupId`, `ActiveTenantId` |
| `allowed_tenants` | array | `[]` em `NONE`; 1 em `SINGLE`; 1 a 50 em `MACRO_GROUP` | `AllowedTenants` |
| `active_scope` | string \| `null` | `OperationalScope`; `null` em `NONE` | `ActiveScope` |
| `roles`, `permissions` | array | RG-01 (`SINGLE`), RG-12 (`MACRO_GROUP`); `[]` em `NONE` | `Roles`, `Permissions` |
| `act` | `{ "sub", "ticket_id" }` | Só em impersonation | `ActorUserId`, `TicketId` |

### 3.6 Catálogo de regras globais (RG)
Nível 1 da precedência (§1). Texto canónico nos extras do núcleo dos donos: RG-01 a RG-05 e RG-10 a RG-13 implementadas só por M-08 (`IAccessQueries`); RG-06 a RG-08 só por M-00; RG-09 só por M-13. Os consumidores aplicam-nas pela interface e indicam o passo da sua ordem de avaliação.

| ID | Resumo | Erro | Dono |
|---|---|---|---|
| RG-01 | Papéis e permissões de `u` em `t` para `S`: dono → todas as `role` de `S`; senão as de `member_role` | – | M-08 |
| RG-02 | Escopo `S` inativo em `t` ou sem papéis | 403 `ERR-CTX-003` | M-08 |
| RG-03 | Sem vínculo `ACTIVE` em `t` | 403 `ERR-CTX-001` | M-08 |
| RG-04 | Tenant ou grupo `SUSPENDED`, ou `denylist:tenant:{t}` | 403 `ERR-CTX-002` | M-08 |
| RG-05 | Utilizador bloqueado. EP-01: só `status = SUSPENDED`, avaliado só depois de a palavra-passe estar correta (D-08, D-11). EP-02: `SUSPENDED` ou `denylist:user` ≥ `iat`. EP-03: `SUSPENDED` ou `denylist:user` ≥ `created_at` do refresh apresentado. EP-05 (alvo): `SUSPENDED` ou qualquer `denylist:user` do alvo (PRS-46) | 403 `ERR-AUTH-004` | M-08 |
| RG-06 | Token ausente, inválido, expirado ou com `denylist:jti` | 401 `ERR-AUTH-002` | M-00 |
| RG-07 | `denylist:user` de `sub` ou `act.sub` com valor ≥ `iat` | 403 `ERR-AUTH-004` | M-00 |
| RG-08 | `denylist:tenant` de algum tenant de `allowed_tenants` | 403 `ERR-CTX-002` | M-00 |
| RG-09 | Escrita ou leitura rejeitada pela camada 3 ou 4 | 403 `ERR-CTX-007` | M-13 |
| RG-10 | Contextos disponíveis no login | – | M-08 |
| RG-11 | `MACRO_GROUP`: lista com 1 a 50 tenants distintos do grupo e `can_view_group = true` em todos | 403 `ERR-CTX-004` | M-08 |
| RG-12 | `MACRO_GROUP`: `roles`/`permissions` = interseção da RG-01 nos tenants | – | M-08 |
| RG-13 | Interseção de `permissions` vazia | 403 `ERR-CTX-003` | M-08 |

## 4. Stack
| Camada | Tecnologia | Versão exata |
|---|---|---|
| SDK / runtime | .NET SDK / ASP.NET Core (`net10.0`, C# 14) | 10.0.402 / 10.0.13 |
| Base de dados / cache / mensageria | PostgreSQL / Redis / Apache Kafka (KRaft) | 17.6 / 7.4.6 / 3.9.1 |
| Contentores | Docker Engine | 29.7.2 |

Pacotes: lista exaustiva no extra de projeto do M-11. Proibido qualquer outro, incluindo `JwtBearer`, `System.IdentityModel.Tokens.Jwt`, `FluentValidation`, `Serilog.*`, `Actuar.*`, `AutoMapper`, `MediatR`, `Newtonsoft.Json`, `Dapper`, `MassTransit`, `Moq`, `FluentAssertions`, `Bogus`. A validação de pedidos é código explícito.

## 6. Catálogo global de erros
| Código | HTTP | `title` (fixo) | Condição |
|---|---|---|---|
| `ERR-VAL-001` | 400 | Pedido inválido ou malformado | JSON inválido, campo obrigatório ausente ou `null`, tipo errado, valor fora do enum, campo desconhecido, formato |
| `ERR-VAL-002` | 422 | Dados de CNPJ ou geolocalização inválidos | M-04 |
| `ERR-VAL-003` | 422 | Formato de e-mail ou política de palavra-passe violada | M-01 |
| `ERR-VAL-004` | 422 | Código de chamado técnico inválido | M-06 |
| `ERR-REQ-001` | 404 | Rota inexistente | Nenhuma rota corresponde |
| `ERR-REQ-002` | 405 | Método não permitido | Path com outro método |
| `ERR-REQ-003` | 413 | Corpo do pedido demasiado grande | Corpo > 65 536 bytes |
| `ERR-REQ-004` | 415 | Tipo de conteúdo não suportado | Corpo com `Content-Type` ≠ `application/json` (`charset` só `utf-8`) |
| `ERR-AUTH-001` | 401 | Credenciais inválidas | M-01 |
| `ERR-AUTH-002` | 401 | Token de acesso ausente, inválido, expirado ou revogado | RG-06; EP-02 com família sem linha `ACTIVE` (PRS-73) |
| `ERR-AUTH-003` | — | `[REMOVIDO]` | Não é devolvido por nenhum EP: a conta `LOCKED` responde 401 `ERR-AUTH-001` (D-08) |
| `ERR-AUTH-004` | 403 | Utilizador suspenso ou revogado globalmente | RG-05, RG-07; alvo `LOCKED` em EP-05 (M-06) |
| `ERR-AUTH-005` | 401 | Refresh token inválido, revogado ou expirado | M-02 |
| `ERR-AUTH-006` | 401 | Reuso de refresh token detetado e sessão revogada | M-02 |
| `ERR-AUTH-010` | 403 | Operação de impersonation não autorizada | M-06 |
| `ERR-CTX-001` | 403 | Utilizador sem vínculo ativo no Tenant solicitado | RG-03 |
| `ERR-CTX-002` | 403 | Tenant ou Grupo Económico suspenso | RG-04, RG-08; grupo `SUSPENDED` em EP-06 (M-04) |
| `ERR-CTX-003` | 403 | Escopo operacional inativo ou papel não atribuído | RG-02, RG-13 |
| `ERR-CTX-004` | 403 | Violação das regras de agregação em visão macro | RG-11 |
| `ERR-CTX-005` | 409 | Conflito de unicidade de registo | M-04, M-05 |
| `ERR-CTX-006` | 422 | Papel incompatível com os escopos ativos do Tenant | M-05 |
| `ERR-CTX-007` | 403 | Violação de isolamento de contexto ou permissão insuficiente | Autorização do EP negada, RG-09, `act` em EP-02 |
| `ERR-CTX-008` | 404 | Recurso de domínio não encontrado | `group_id`, `tenant_id`, `user_id` ou `role_id` inexistente |
| `ERR-SYS-001` | 500 | Erro interno do servidor | Exceção não prevista |
| `ERR-SYS-002` | 503 | Dependência indisponível | PostgreSQL, Redis ou chave indisponível (PRS-16) |

Um módulo **NÃO DEVE** criar códigos: um código em falta é um `CR-nn`.

## 7. Regras por omissão
| Situação não coberta | Comportamento obrigatório |
|---|---|
| Campo de request não listado | 400 `ERR-VAL-001` |
| Autorização não descrita | Negar: 403 `ERR-CTX-007` |
| Rota inexistente / método errado | 404 `ERR-REQ-001` / 405 `ERR-REQ-002` |
| Erro inesperado | 500 `ERR-SYS-001`, transação desfeita |
| PostgreSQL, Redis ou chave indisponível | 503 `ERR-SYS-002`, transação desfeita, salvo um `DEL` posterior ao commit (PRS-15, PRS-16) |
| Escrever tabela de outro módulo | Só pela interface do dono (`40` §4.3) |
| Exceção de BD, Redis ou chave | Tratador global do M-00, pela cadeia de `InnerException`: 42501 → 403 `ERR-CTX-007`; 23505 não tratado → 409 `ERR-CTX-005` |

## 8. Explicitamente proibido
- `refresh_token` em claro; segredos em log.
- Hidratar o `AuthContext` a partir de headers em texto (`X-Tenant-Id`, `X-User-Id`, `X-Group-Id`).
- `HS256` ou RSA com menos de 2048 bits; `refresh_token` em impersonation.
- `DELETE` em qualquer tabela; `UPDATE` em `auth_audit_log` (PRS-19).
- `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `EnsureCreated`, `Thread.Sleep` em código de produção.

---

## Extras (fora do núcleo base; só nos pacotes indicados em cada título)

### 2.2 Âmbito (extra: PO, ORQ e validadores)
| Dentro (módulo) | Fora (NÃO implementar) |
|---|---|
| M-00 middleware, erros, correlação, auditoria, denylist; M-13 isolamento; M-11 esquema, migração, seed, arranque | Frontend; API Gateway (o serviço só fornece EP-10 e `denylist:*`); Phantom Token (PRS-04) |
| M-01 login; M-02 refresh; M-09 logout | Criar, editar ou remover `user_account` e `economic_group`; registo; alterar ou recuperar palavra-passe (PRS-13) |
| M-03 troca de contexto `SINGLE`/`MACRO_GROUP`; M-08 regras de acesso (`IAccessQueries`) | Editar ou remover tenant, membro, papel de membro; desativar escopo; listagens `GET`; CRUD de `role`; federação; MFA |
| M-04 criar tenant; M-12 suspender e reativar tenant; M-05 vincular membro | ERP, certificados ICP-Brasil, indicadores, painéis e mapa; filtragem de dados de outros serviços (Ideia Base §3, Ideia Base §5.2, Ideia Base §7.2) |
| M-06 impersonation; M-10 suspender e reativar utilizador (`PLATFORM_ADMIN`: conta; `TENANT_ADMIN`: vínculos no seu grupo, D-07) | `KafkaConsumerFilter` e biblioteca `AuthContext` partilhada (PRS-14); consumidores dos eventos; self-dealing (PRS-10) |
| M-07 JWKS e emissão de tokens; M-14 outbox e publicação Kafka | Rotação de chaves (PRS-25); expurgo de tabelas; reprocessamento de `FAILED`; health check; Loki/ELK, Prometheus, Kubernetes |

### 2.3 Atores (extra: PO, ORQ e validadores)
Operador B2B (`platform_role = NONE`): EP-01 a EP-04. Administrador corporativo (`active_scope = TENANT_ADMIN`): EP-06 a EP-09 no seu grupo. `SUPPORT_AGENT`: EP-05. `PLATFORM_ADMIN`: EP-05 a EP-09 sem restrição de grupo. Sistemas: API Gateway e microsserviços de domínio (`20` EXT-04, EXT-05); PostgreSQL, Redis, Kafka (EXT-01 a EXT-03).

### 3.7 Configuração e produtor Kafka (extra: M-00, M-07, M-11, M-14, F-00, `70`)
| Aspeto | Especificação |
|---|---|
| Configuração | Obrigatórias (ausente → código de saída ≠ 0): `DB_CONNECTION_STRING`, `REDIS_CONNECTION_STRING`, `KAFKA_BOOTSTRAP_SERVERS`. Com default: `JWT_ISSUER` = `identity-service`, `JWT_AUDIENCE` = `b2b-platform`, `JWT_KEY_ID` = `key-2026-01`, `OUTBOX_POLLING_INTERVAL_MS` = `500`. `JWT_RSA_PRIVATE_KEY_PEM` (PKCS#8 RSA 2048): vazia só com `ASPNETCORE_ENVIRONMENT` ∈ {`Development`, `Testing`}, gerando um par em memória; vazia noutro ambiente → código ≠ 0 (PRS-25) |
| Produtor Kafka (M-14) | `Confluent.Kafka` com `MessageTimeoutMs = 3000` e `Acks = All`; o resto por omissão (PRS-63) |

### 3.8 Texto canónico das RG de M-08 (extra: M-08)
| ID | Regra | Erro |
|---|---|---|
| RG-01 | **SE** `tenant_member(t,u).is_owner = true` **ENTÃO** `roles` = nomes de todas as `role` com `scope = S` **SENÃO** nomes das `role` ligadas por `member_role` a esse membro com `scope = S`. `permissions` = união sem repetição das `permissions` dessas `role` (PRS-03) | – |
| RG-02 | **SE** não existir `tenant_scope(t,S)` com `is_active = true` **OU** `roles` da RG-01 for vazio **ENTÃO** rejeitar | 403 `ERR-CTX-003` |
| RG-03 | **SE** não existir `tenant_member(t,u)` com `status = ACTIVE` **ENTÃO** rejeitar | 403 `ERR-CTX-001` |
| RG-04 | **SE** `tenant.status = SUSPENDED` **OU** o seu `economic_group.status = SUSPENDED` **OU** existir `denylist:tenant:{t}` **ENTÃO** rejeitar | 403 `ERR-CTX-002` |
| RG-05 | **SE** `user_account.status = SUSPENDED` **OU** (o endpoint não for EP-01 **E** existir `denylist:user:{u}` com valor `V` e `T_ref ≤ V`) **ENTÃO** rejeitar. Em EP-01 a regra só é avaliada depois de a palavra-passe estar correta (D-08). `T_ref`: `iat` do token de entrada; em EP-03, `created_at` (Unix) do `refresh_token` apresentado; para o alvo de EP-05, `T_ref = 0` (qualquer chave rejeita) (PRS-46) | 403 `ERR-AUTH-004` |
| RG-10 | Um contexto por `tenant_member(t,u)` com `status = ACTIVE` em que a RG-04 não rejeita `t`; `available_scopes` = escopos `S` em que a RG-02 não rejeita; `roles` = união da RG-01 nesses `S`. Tenant sem escopo disponível não aparece. Ordem ordinal por `group_name`, `legal_name`, `tenant_id` (PRS-28) | – |
| RG-11 | **SE** a lista `L` tiver menos de 1 ou mais de 50 elementos (PRS-35), repetidos, algum tenant com `group_id ≠ g`, **OU** algum tenant sem `tenant_member(t,u)` `ACTIVE` com `can_view_group = true` (mesmo com `is_owner = true`) **ENTÃO** rejeitar o pedido inteiro (PRS-05) | 403 `ERR-CTX-004` |
| RG-12 | `roles` e `permissions` = interseção, sobre todos os tenants de `L`, dos conjuntos da RG-01 para `S` (PRS-05) | – |
| RG-13 | **SE** a interseção de `permissions` da RG-12 for vazia **ENTÃO** rejeitar (`roles` vazio com `permissions` não vazio é aceite) | 403 `ERR-CTX-003` |

### 3.9 Detalhe transversal do M-00 (extra: M-00)
| Aspeto | Especificação |
|---|---|
| RG-06 | **SE** `Authorization` ausente ou não `Bearer`, **OU** JWT inválido (assinatura, `alg ≠ RS256`, `kid ≠ JWT_KEY_ID`, `iss`, `aud`, `exp ≤ agora` com tolerância 0, claim da §3.5 ausente ou de tipo errado), **OU** existir `denylist:jti:{jti}` **ENTÃO** 401 `ERR-AUTH-002` |
| RG-07 | **SE** existir `denylist:user:{sub}` ou `denylist:user:{act.sub}` com valor `V` e `iat ≤ V` **ENTÃO** 403 `ERR-AUTH-004`. A comparação usa `≤` (PRS-61): um token emitido no mesmo segundo do bloqueio é rejeitado |
| Chave de validação | Lida de `ISigningKey.SigningKey` antes de `Authorization` (PRS-71); `kid` do header ≠ `KeyId` → RG-06; valida com `IssuerSigningKey` = essa chave, sem `IssuerSigningKeyResolver`. Exceção da chave → 503 em todos os EP que a usam |
| RG-08 | **SE** existir `denylist:tenant:{t}` para algum `t` de `allowed_tenants` (`MGET`) **ENTÃO** 403 `ERR-CTX-002` |
| `BypassTenantFilter` | `true` em EP-01 a EP-05, com ou sem token (EP-01 e EP-03 correm sem token; consultas restritas ao utilizador em causa, PRS-07), ou com `platform_role = PLATFORM_ADMIN`; `false` em qualquer outro caso, incluindo pedidos sem token fora de EP-01 a EP-05 |
| Conjunto de escrita | `WriteTenants` = `AllowedTenants` ∪ tenants declarados com IF-08 `AuthorizeTenantWrite(t)`. Chamado pelo endpoint depois de a autorização do EP aprovar `t` e antes de abrir a ligação da transação: EP-06 com o `id` do tenant novo; EP-07 com o `tenant_id` do path. Nenhum outro endpoint o chama (PRS-51); EP-09 escreve nos vínculos do grupo pelo bypass limitado do M-13 (PRS-72) |
| Correlação | `X-Correlation-Id` aceite só se `Guid.TryParseExact(valor, "D")`; usado e devolvido como `ToString("D")` (minúsculas). Ausente ou inválido → UUID v7 novo (PRS-52) |
| IP de origem | 1.º elemento de `X-Forwarded-For` (até à 1.ª vírgula, sem espaços) se `IPAddress.TryParse` aceitar e `IPAddress.ToString()` tiver no máximo 45 caracteres, gravado como `IPAddress.ToString()`; senão `RemoteIpAddress?.ToString()`; senão `"0.0.0.0"` (PRS-52) |
| Denylist e commit | `SET denylist:*` antes do commit; `DEL denylist:tenant` depois do commit e só na transição real `SUSPENDED → ACTIVE` (PRS-15). `denylist:user` nunca é apagada: expira pelo TTL (PRS-48) |
| Logging | Scope com `correlation_id`, `user_id`, `actor_user_id`, `tenant_id`, `group_id`, `active_scope` |

### 3.10 Isolamento (extra: M-13; implementa RG-09 e as camadas 2 a 4 pela interface `ITenantIsolation`)
| Aspeto | Especificação |
|---|---|
| RG-09 | **SE** a camada 3 ou a 4 rejeitar **ENTÃO** desfazer a transação e 403 `ERR-CTX-007` |
| Camada 2 | Global Query Filter: `ctx.BypassTenantFilter \|\| db.TenantIsolationBypass \|\| ctx.AllowedTenants.Contains(e.TenantId)` (`db` = `IdentityDbContext`) |
| Camada 3 | `TenantSaveChangesInterceptor`: entidade `ITenantScoped` em `Added`, `Modified` ou `Deleted` com `TenantId ∉ WriteTenants`, `BypassTenantFilter = false` e `TenantIsolationBypass = false` → RG-09. Não preenche `TenantId` automaticamente; `TenantId = Guid.Empty` → RG-09 (PRS-47) |
| Camada 4 | `RlsConnectionInterceptor` em cada abertura de ligação: `SET ROLE identity_app; SELECT set_config('app.allowed_tenants', @a, false), set_config('app.write_tenants', @w, false), set_config('app.auth_flow', @b, false);` com `@a`/`@w` = listas unidas por `,` (texto vazio se vazias) e `@b` = `'true'` se `BypassTenantFilter`, senão `'false'` (PRS-17; políticas em `30` §5) |
| Bypass limitado | IF-11.3 `ExecuteWithBypassAsync` (PRS-72): camadas 2 e 3 por `TenantIsolationBypass`; camada 4 por `set_config('app.auth_flow', 'true', false)` na ligação já aberta; repõe ambos no `finally`. Só a usam a consulta de pertença ao grupo (M-08) e a mudança de vínculos no grupo (M-05), ambas do EP-09; qualquer outra escrita cruzada continua rejeitada |

### 3.11 Projeto, migrações, contentores e pacotes (extra: M-11, F-00, `70`)
| Aspeto | Especificação |
|---|---|
| Ficheiros de raiz | `IdentityService.slnx` (formato criado por `dotnet new sln` no SDK 10.0.402); `global.json` `{"sdk":{"version":"10.0.402","rollForward":"disable"}}`; `.config/dotnet-tools.json` com `dotnet-ef` 10.0.13; `Directory.Build.props` com `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`. Referência executável: `evidencias/pins/` |
| Migrações | Único mecanismo: EF Core Migrations em `M11Esquema/Persistence/Migrations/`, geradas com `dotnet tool run dotnet-ef`. Antes de `app.Run()`, `Program.cs` cria um `IdentityDbContext` com `new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(DB_CONNECTION_STRING).UseSnakeCaseNamingConvention().Options` (sem `AddInterceptors`) e executa `Database.MigrateAsync()`. A migração inicial cria todo o `30` §3, e por `migrationBuilder.Sql` o `30` §5 e §6. Proibidos: `EnsureCreated`, SQL fora das migrações, `dotnet ef database update` no README |
| Contentores | `Dockerfile` multi-stage `mcr.microsoft.com/dotnet/sdk:10.0.402` → `mcr.microsoft.com/dotnet/aspnet:10.0.13`, porta 8080, `ENTRYPOINT ["dotnet", "IdentityService.dll"]`. `docker-compose.yml` = texto fixo de `70` §2 (imagens `postgres:17.6`, `redis:7.4.6`, `apache/kafka:3.9.1`; health checks e `service_healthy`), copiado sem alterações; `70` §2 entra no pacote do M-11 |
| Pacotes (exaustivo) | `src`: `Microsoft.IdentityModel.JsonWebTokens@8.23.0`, `Microsoft.EntityFrameworkCore.Relational@10.0.13`, `Npgsql.EntityFrameworkCore.PostgreSQL@10.0.3`, `EFCore.NamingConventions@10.0.1`, `Microsoft.EntityFrameworkCore.Design@10.0.13` (`PrivateAssets="all"`), `StackExchange.Redis@2.13.17`, `Confluent.Kafka@2.16.0`, `Konscious.Security.Cryptography.Argon2@1.3.1`. `tests`: `Microsoft.NET.Test.Sdk@18.11.0`, `xunit@2.9.3`, `xunit.runner.visualstudio@3.1.5`, `Microsoft.AspNetCore.Mvc.Testing@10.0.13`, `Testcontainers.PostgreSql@4.16.0`, `Testcontainers.Redis@4.16.0`, `Testcontainers.Kafka@4.16.0` |

### 5. Requisitos não funcionais (extra: M-01, M-13, M-14; os CA de RNF de cada módulo vão no brief)
| ID | Categoria | Requisito | Limiar | Verificação | Na métrica |
|---|---|---|---|---|---|
| RNF-01 | Segurança | Hash Argon2id (PRS-21) | `m=19456` KiB, `t=2`, `p=1`, salt 16 B, hash 32 B, PHC `$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>` | Prefixo de `password_hash` | Sim: CA em M-01 |
| RNF-02 | Segurança | RS256 em JWT e `context_jws` | RSA 2048; `kid` igual no JWT e no JWKS | Verificar só com a chave de EP-10 | Sim: CA em M-07 |
| RNF-03 | Segurança | Vida dos tokens | `exp − iat = 900`; `expires_at − created_at = 604800 s` (PRS-33); tolerância 0 | Claims e colunas | Sim: CA em M-01, M-02 |
| RNF-04 | Desempenho | Latência (PRS-50) | p95 ≤ 150 ms (EP-02, EP-03); ≤ 400 ms (EP-01) | Contentor local | Fora: depende do hardware, sem resultado binário reprodutível |
| RNF-05 | Confiabilidade | Outbox | `PENDING` → `PUBLISHED` e mensagem no tópico em ≤ 3 s (PRS-43) | Kafka real, espera ativa com teto | Sim: CA em M-14 |
| RNF-06 | Segurança | Isolamento | 100 % das operações em `ITenantScoped` fora do permitido rejeitadas pelas camadas 2, 3 e 4 | Um teste `@critico` por camada | Sim: CA em M-13 |

### 9. Pressupostos globais (extra: PO e ORQ)
Decisões do PO em `90` §3 (D-03 a D-23). PRS-70 a PRS-73 foram criados pela UT-2-91 na faixa do ARQ em vez da sua (PRS-80 a PRS-89): desvio registado, IDs mantidos (imutáveis, aprovados por D-23); os PRS novos da UT-2-91 usam PRS-80 a PRS-89. Os PRS de um só módulo estão em `90` §2.2.

| ID | Pressuposto / Decisão | Estado |
|---|---|---|
| PRS-01 | EP-01 emite token `view_mode = NONE` com `fid` | Aprovado (D-12, confirma a v1.0) |
| PRS-02 | Contexto ativo no `refresh_token` `ACTIVE`; EP-02 atualiza-o, EP-03 copia o contexto | Aprovado (D-12, confirma a v1.0) |
| PRS-03 | RBAC por escopo; dono com todas as `role` do escopo | Aprovado (D-12, confirma a v1.0) |
| PRS-04 | JWT RS256 + JWKS + denylist; sem Phantom Token | Aprovado (D-12, confirma a v1.0) |
| PRS-06 | Denylist: `jti` → `"1"`; `user` → instante Unix comparado com `iat`; `tenant` → `"1"` | Aprovado (D-12, confirma a v1.0) |
| PRS-07 | Só 3 tabelas `ITenantScoped`; bypass nos fluxos de autenticação | Aprovado (D-12, confirma a v1.0) |
| PRS-10 | Self-dealing fora | Aprovado (D-12, confirma a v1.0) |
| PRS-11 | Normalização de `email` e `cnpj` | Aprovado (D-12, confirma a v1.0) |
| PRS-12 | TTL `denylist:tenant` = 900 s | Aprovado (D-05) |
| PRS-13 | `user_account` e `economic_group` criados fora | Aprovado (D-03) |
| PRS-14 | Sem consumo de tópicos nem `KafkaConsumerFilter` | Aprovado (D-03) |
| PRS-15 | `SET` antes do commit; `DEL denylist:tenant` depois e só em transição real | Aprovado (D-06) |
| PRS-16 | Dependência indisponível → 503 fail-closed | Aprovado (D-06) |
| PRS-17 | RLS por `SET ROLE` + `set_config` na abertura da ligação | Aprovado (D-10) |
| PRS-18 | `acting_scope` `NONE` (ENUM-10) | Aprovado (D-10) |
| PRS-19 | Sem remoção física; auditoria só `INSERT` | Aprovado (D-10) |
| PRS-20 | `access_token` de 900 s | Aprovado (D-10) |
| PRS-21 | Parâmetros Argon2id | Aprovado (D-10) |
| PRS-22 | Coordenadas `NUMERIC(9,6)` | Aprovado (D-10) |
| PRS-23 | Corpo máximo 65 536 bytes | Aprovado (D-10) |
| PRS-24 | Arrays de saída em ordem ordinal | Aprovado (D-10) |
| PRS-25 | Uma chave RSA; `kid` = `JWT_KEY_ID` | Aprovado (D-10) |
| PRS-26 | `detail` = `title` | Aprovado (D-10) |
| PRS-27 | `TEXT[]`/`UUID[]` em vez de `JSONB` | Aprovado (D-10) |
| PRS-28 | Contextos sem escopo omitidos; ordem de `available_contexts` | Aprovado (D-10) |
| PRS-35 | `MACRO_GROUP` com 1 a 50 tenants | Aprovado (D-10) |
| PRS-46 | `T_ref` da RG-05 por endpoint (EP-01 só estado; alvo de EP-05 com qualquer chave) | Aprovado (D-11) |
| PRS-47 | Camada 3 sem preenchimento automático de `TenantId` | Aprovado (D-10) |
| PRS-48 | `denylist:user` nunca apagada | Aprovado (D-10) |
| PRS-49 | `SUSPENDED` em `economic_group` só por carga externa; em `tenant_member` também por EP-09 com `TENANT_ADMIN` (D-07) | Aprovado (D-10); revisto por D-07 |
| PRS-50 | Limiares do RNF-04 | Aprovado (D-10) |
| PRS-51 | Conjunto de escrita declarado por `AuthorizeTenantWrite` | Aprovado (D-10) |
| PRS-52 | Regras de aceitação de `X-Correlation-Id` e de IP de origem | Aprovado (D-10) |
| PRS-61 | `denylist:user` rejeita tokens com `iat ≤ V` (a Ideia Base §6.2 diz "menor") | Aprovado (D-16) |
| PRS-62 | Sem CA de "processo morto a meio" e de "resposta perdida do broker"; comportamento fixado por atomicidade da transação e entrega at-least-once | Aprovado (D-16) |
| PRS-63 | Produtor Kafka com `MessageTimeoutMs = 3000` e `Acks = All` | Aprovado (D-16) |
| PRS-64 | Ordem de avaliação de `MACRO_GROUP` fixada em `40` IF-09.5 | Aprovado (D-16) |
| PRS-65 | Classificação das exceções de BD, Redis e chave pelo tratador global (`40` §1) | Aprovado (D-16) |
| PRS-66 | Vínculo do criador de um tenant (D-04) com `can_view_group = false` | Aprovado (D-16) |
| PRS-67 | EP-06 por `TENANT_ADMIN` acrescenta o escopo `TENANT_ADMIN` ao tenant novo se faltar, para o papel `GROUP_MANAGER` do criador ser válido | Aprovado (D-16) |
| PRS-68 | Suspensão de vínculos por `TENANT_ADMIN` (D-07): tokens de acesso já emitidos valem até expirar (≤ 900 s); as famílias com contexto nesses tenants são revogadas | Aprovado (D-16) |
| PRS-69 | Resposta de EP-09 com `scope` (`GLOBAL`, `GROUP`) e `group_id` | Aprovado (D-16) |
| PRS-53 a PRS-60 | PRS de `40` (ordem das camadas, Latin-1, query, headers Kafka, EP-02, `impersonation_context`, EV-03) | Aprovados (D-10; PRS-53 também D-13) |
| PRS-70 | Estratégia de execução por omissão do Npgsql EF (sem `EnableRetryOnFailure`); exceções classificadas pela cadeia de `InnerException` com tipos exatos; `CryptographicException` = chave indisponível (`40` §1.2) | Aprovado (D-23) |
| PRS-71 | O middleware lê a chave antes da RG-06: chave indisponível → 503 também em pedidos sem token | Aprovado (D-23) |
| PRS-72 | Bypass limitado da IF-11.3, só em IF-09.7 e IF-12.2, com reposição em `finally` | Aprovado (D-23) |
| PRS-73 | EP-02 com família sem linha `ACTIVE` (IF-01.2 = `false`) → 401 `ERR-AUTH-002`, sem efeitos | Aprovado (D-23) |
| PRS-80 | Formato de e-mail (RFC 5322) verificado por `MailAddress.TryCreate` com `Address` igual ao texto após `trim()` | Aprovado (D-26) |
| PRS-81 | D-22 aplica-se também à reativação: `TENANT_ADMIN` não muda vínculos próprios nem de quem é dono no grupo, em `SUSPENDED` e em `ACTIVE` | Aprovado (D-26) |
| PRS-82 | EP-09 com `scope = GROUP` devolve `updated_at` = instante da operação, também sem vínculos alterados | Aprovado (D-26) |
| PRS-83 | Conta `SUSPENDED` com palavra-passe errada: 401 e auditoria `LOGIN_FAILED`, sem somar a `failed_login_attempts` | Aprovado (D-26) |
| PRS-84 | EP-03: qualquer falha de revalidação (RG-02 a RG-05, RG-11, RG-13) revoga a família; rotação concorrente (0 linhas ou 23505) tratada como reuso, 401 `ERR-AUTH-006` | Aprovado (D-26) |
| PRS-86 | EP-09 por `TENANT_ADMIN`: revogação de famílias só nos tenants cujos vínculos mudaram; a repetição de `SUSPENDED` não revoga | Aprovado (D-26) |
| PRS-90 a PRS-97 | PRS de `70` (suite, chave, relógio, dados fictícios, falhas, timeouts, compose, concorrência) | Aprovados (D-10; PRS-97: D-16) |

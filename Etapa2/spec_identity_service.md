# Especificação de Requisitos — Identity-Service

## 0. Metadados e Controlo de Versão

| Campo | Valor |
|---|---|
| ID do documento | SPEC-IDENTITY-001 |
| Versão | v1.0-baseline |
| Estado | Baseline congelada |
| Hash SHA-256 da baseline | Registado externamente em `Etapa2/spec_identity_service.md.sha256` e `Etapa2/registo_ambiguidade.md` |
| Autor (AR) | Antigravity (Analista de Requisitos / Arquiteto de Software) |
| Aprovadores (PO / ARQ / RI) | Matheus (PO — `PRS-01` a `PRS-11` aprovados em 2026-10-18) / Antigravity (ARQ) / Subagente Revisor Independente (RI) |
| Data | 2026-10-18 |
| Modelo LLM alvo | claude-opus-5-5 |

### Histórico
| Versão | Data | Autor | Alteração |
|---|---|---|---|
| v0.1 | 2026-10-18 | Antigravity | Elicitação inicial e consolidação arquitetural (Fase 1) a partir da Ideia Base e estudo do Actuar-Projects |
| v0.5 | 2026-10-18 | Antigravity | Modelação física de dados, enums, contratos REST, eventos Kafka e catálogo de erros (Fase 2) |
| v0.9 | 2026-10-18 | Antigravity | Escrita determinística de RF, RN, fluxos de exceção, critérios Gherkin e matriz de rastreabilidade (Fase 3) |
| v1.0-baseline | 2026-10-18 | Antigravity | Resolução integral dos defeitos da Fase 4 (IA = 0), aprovação dos PRS pelo PO e congelamento da baseline (Fase 4.7) |

---

## 1. Instruções Obrigatórias para a LLM

> Esta secção destina-se à LLM que vai gerar o código. O texto padrão abaixo **não deve** ser alterado.

1. Implemente **exclusivamente** o que está especificado neste documento. Qualquer endpoint, campo, tabela, dependência ou comportamento que não esteja descrito aqui **NÃO DEVE** ser criado.
2. As palavras **DEVE**, **NÃO DEVE**, **DEVERIA**, **NÃO DEVERIA** e **PODE** têm o significado definido na RFC 2119/8174.
3. Em caso de conflito entre secções, a precedência é a seguinte: **§8 Regras de Negócio > §10 Critérios de Aceitação > §6 Contratos > §5 Modelo de Dados > §7 Descrições de RF > restantes secções.**
4. Use **apenas** as tecnologias e versões da §3. Não adicione bibliotecas que não estejam na lista de dependências permitidas.
5. Respeite **literalmente** os nomes de entidades, colunas, campos JSON, enums, tópicos, chaves Redis e códigos de erro. Não traduza nem renomeie.
6. Cada Critério de Aceitação (§10) **DEVE** ter um teste automatizado correspondente, com o ID do CA no nome do teste (por exemplo, `CA_04_2_refresh_reutilizado_revoga_familia`).
7. Se uma informação necessária estiver em falta, **NÃO** a invente. Aplique a regra por omissão da §12.2. Se nenhuma regra se aplicar, implemente a alternativa mais restritiva e registe-a na secção "Suposições" do relatório de entrega (§13).
8. Entregue o resultado no formato da §13.

---

## 2. Contexto do Sistema

### 2.1 Propósito
O `Identity-Service` é o microsserviço responsável pela gestão de identidades de pessoas físicas, hierarquia corporativa multi-CNPJ (`EconomicGroup` e `Tenant`), autenticação OAuth 2.1 com rotação de `RefreshToken`, troca explícita de contexto operacional (`SINGLE` e `MACRO_GROUP`) nos 4 escopos da plataforma B2B (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`), delegação auditável de suporte (RFC 8693), revogação imediata via Denylist no Redis e propagação do contexto criptograficamente assinado (`AuthContext` e `context_jws`) para o API Gateway e demais microsserviços do ecossistema.

### 2.2 Âmbito
| Dentro do âmbito (implementar) | Fora do âmbito (NÃO implementar) |
|---|---|
| Autenticação de pessoa física por e-mail e palavra-passe com Argon2id e bloqueio temporário por falhas consecutivas (`EP-01`, `RF-01`) | Interface gráfica web ou mobile (Frontend React/Angular) |
| Descoberta de contextos corporativos disponíveis (`EconomicGroup`, `Tenant`, `TenantScope`, `can_view_group`) no login (`EP-01`, `RF-01`) | Roteamento externo, rate limiting de borda e proxy reverso do API Gateway |
| Troca explícita de contexto ativo para Modo Unitário (`SINGLE`) e Modo Visão Macro (`MACRO_GROUP`) com emissão de JWT RS256 (`EP-02`, `RF-02`, `RF-03`) | Tradução de token opaco para JWT na borda (padrão Phantom Token descartado conforme `PRS-04`) |
| Rotação de `RefreshToken` por família (`family_id`) com deteção automática de reuso e revogação em cascata (`EP-03`, `RF-04`) | Autenticação federada externa (Google, Microsoft Entra ID, Supabase, SAML) e fluxos de recuperação de palavra-passe por e-mail/SMS |
| Logout com invalidação imediata do JWT (`denylist:jti:{jti}`) e revogação da família de `RefreshToken` (`EP-04`, `RF-05`) | Gestão de catálogos de produtos, fornecimentos, demandas, leilões, pedidos, fretes e transportadoras |
| Emissão de token de delegação/impersonation auditável (RFC 8693) com claim estruturada `act` e `ticket_id` (`EP-05`, `RF-06`) | Aplicação das regras comerciais de bloqueio de auto-negociação (*Self-Dealing*) nos domínios de Mercado, Negociação/Leilão e Logística/Frete (`PRS-10`) |
| Cadastro de `Tenant` (CNPJ) vinculado a `EconomicGroup`, ativação dos 4 escopos operacionais e publicação via Outbox de `EV-01 empresa_cadastrada` (`EP-06`, `RF-07`) | Consumidores externos dos eventos Kafka publicados pelo `Identity-Service` |
| Vinculação de membros (`TenantMember`) e papéis (`Role`, `MemberRole`) a um `Tenant` (`EP-07`, `RF-08`) | Gestão de faturas, pagamentos, certificados digitais ICP-Brasil e conectores de ERP |
| Suspensão e reativação imediata de `Tenant` e de `UserAccount` com escrita na Denylist Redis e publicação de `EV-02` e `EV-03` (`EP-08`, `EP-09`, `RF-09`, `RF-10`) | Sistemas analíticos externos de Business Intelligence (BI) |
| Exposição de chaves públicas em formato JWKS (`EP-10`, `RF-11`) e biblioteca transversal `AuthContext` com as 4 camadas de isolamento (HTTP/Kafka, EF Core Global Filter, SaveChanges Interceptor e PostgreSQL RLS) | Provisionamento de infraestrutura cloud, clusters Kubernetes e servidores de monitorização Prometheus/Grafana |

### 2.3 Atores e Sistemas Externos
| Ator / Sistema | Tipo | Interação com o componente |
|---|---|---|
| Operador B2B (`UserAccount`) | Humano | Autentica-se em `EP-01`, alterna contexto operacional (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) em `EP-02`, renova sessão em `EP-03` e encerra sessão em `EP-04` via API Gateway |
| Administrador Corporativo (`TENANT_ADMIN`) | Humano | Operador autenticado com `active_scope = 'TENANT_ADMIN'` que cadastra filiais/CNPJs (`EP-06`), gere membros e papéis (`EP-07`) e suspende utilizadores do seu grupo (`EP-09`) |
| Agente de Suporte / Admin da Plataforma (`SUPPORT_AGENT` / `PLATFORM_ADMIN`) | Humano | Operador do Control Plane que solicita token de impersonation vinculado a chamado (`EP-05`) e gere o estado de Tenants e Utilizadores (`EP-06`, `EP-08`, `EP-09`) |
| API Gateway | Sistema | Encaminha requisições HTTP REST ao `Identity-Service`, obtém chaves públicas em `EP-10` (`/.well-known/jwks.json`) e consulta a Denylist no Redis (`denylist:jti:*`, `denylist:user:*`, `denylist:tenant:*`) |
| PostgreSQL 17 | Sistema | Base de dados relacional centralizada onde o `Identity-Service` persiste as entidades `ENT-01` a `ENT-10` com políticas de Row-Level Security (RLS) ativas |
| Redis 7.4 | Sistema | Armazenamento chave-valor em memória onde o `Identity-Service` escreve e lê entradas da Denylist distribuída com expiração automática (TTL) |
| Apache Kafka 3.8 | Sistema | Barramento de eventos onde o worker de Outbox do `Identity-Service` publica os tópicos `EV-01` (`empresa_cadastrada`), `EV-02` (`tenant_bloqueado`), `EV-03` (`usuario_bloqueado`) e `EV-04` (`sessao_comprometida`) com cabeçalho `context_jws` |
| Microsserviços de Domínio B2B | Serviço interno | Validam JWT e `context_jws` usando o JWKS exposto em `EP-10`, hidratam a estrutura `AuthContext` e consomem os eventos `EV-01` a `EV-04` |

### 2.4 Diagrama de Contexto
```mermaid
flowchart LR
    U["Operador B2B / TENANT_ADMIN"] -->|"HTTPS JSON"| GW["API Gateway"]
    SUP["Agente de Suporte / PLATFORM_ADMIN"] -->|"HTTPS JSON"| GW
    GW -->|"REST /api/v1/*"| ID["Identity-Service"]
    GW -->|"GET /.well-known/jwks.json"| ID
    GW -->|"Leitura Denylist"| REDIS[("Redis 7.4")]
    ID -->|"Escrita e Leitura Denylist"| REDIS
    ID -->|"EF Core + RLS (TCP 5432)"| PG[("PostgreSQL 17")]
    ID -->|"Outbox Publisher (TCP 9092)"| KAFKA[["Apache Kafka 3.8"]]
    KAFKA -->|"EV-01 a EV-04 + context_jws"| MS["Microsserviços B2B (Mercado, Leilão, Pedidos, Frete)"]
    MS -->|"GET /.well-known/jwks.json"| ID
```

### 2.5 Glossário
| Termo de negócio | Nome no código | Definição | Sinónimos proibidos |
|---|---|---|---|
| Grupo Económico | `EconomicGroup` | Entidade raiz (`ENT-01`) que agrupa 1 a N CNPJs pertencentes à mesma holding ou organização empresarial | `Account`, `Holding`, `CompanyGroup`, `OrganizationGroup`, `Corporation` |
| Tenant | `Tenant` | Pessoa jurídica (`ENT-02`) identificada por um CNPJ único de 14 dígitos, vinculada a um único `EconomicGroup` | `Company`, `Client`, `Organization`, `Enterprise`, `Customer`, `Filial` |
| Escopo Operacional | `OperationalScope` | Cada uma das 4 visões de atuação no ecossistema B2B (`ENUM-03`: `BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) habilitadas num `Tenant` via `TenantScope` (`ENT-03`) | `Profile`, `Module`, `Vertical`, `Persona`, `BusinessType` |
| Conta de Utilizador | `UserAccount` | Identidade global de pessoa física (`ENT-04`) autenticada por e-mail e palavra-passe na plataforma | `Person`, `Account`, `Individual`, `LoginUser`, `Operator` |
| Membro do Tenant | `TenantMember` | Vínculo formal (`ENT-05`) entre um `UserAccount` e um `Tenant`, contendo flags de propriedade (`is_owner`) e visão de grupo (`can_view_group`) | `Membership`, `Affiliation`, `Employee`, `CompanyUser`, `PersonTenant` |
| Papel | `Role` | Agrupador de permissões granulares (`ENT-06`) vinculado a um único `OperationalScope` | `Group`, `ProfileRole`, `Function`, `Position`, `AccessLevel` |
| Papel do Membro | `MemberRole` | Associação (`ENT-07`) entre um `TenantMember` e uma `Role` | `UserRole`, `RoleAssignment`, `PersonRole` |
| Token de Atualização | `RefreshToken` | Registo persistido por hash SHA-256 (`ENT-08`) pertencente a uma família de rotação (`family_id`) OAuth 2.1 | `SessionToken`, `OpaqueToken`, `RenewalToken` |
| Modo de Visão | `ViewMode` | Modalidade de isolamento do token (`ENUM-06`: `NONE`, `SINGLE`, `MACRO_GROUP`) que governa `tenant_id` e `allowed_tenants` | `OperationMode`, `ScopeMode`, `ContextMode`, `FilterMode` |
| Contexto de Autenticação | `AuthContext` | Objeto Scoped hidratado a cada requisição HTTP ou consumo Kafka que expõe a identidade, o `ViewMode` e os `AllowedTenants` às 4 camadas de segurança | `UserContext`, `SecurityContext`, `TenantContext`, `SessionContext` |
| Registo de Auditoria | `AuthAuditLog` | Registo imutável (`ENT-09`) de eventos de autenticação, troca de contexto, bloqueio e impersonation | `SecurityLog`, `EventLog`, `AccessHistory`, `AuditTrail` |
| Mensagem de Outbox | `OutboxMessage` | Registo transacional (`ENT-10`) para publicação assíncrona garantida no Apache Kafka | `EventQueue`, `PendingEvent`, `KafkaBuffer` |

---

## 3. Stack Tecnológica

### 3.1 Tecnologias
| Camada | Tecnologia | Versão exata | Observação |
|---|---|---|---|
| Linguagem / Runtime | C# 14 / .NET SDK | 10.0.100 | TargetFramework `net10.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` |
| Framework web | ASP.NET Core | 10.0.0 | Minimal APIs ou Controllers REST com serialização `System.Text.Json` |
| ORM / Acesso a dados | Entity Framework Core + Npgsql | 10.0.0 | Com `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.0` e `EFCore.NamingConventions` `10.0.0` (`UseSnakeCaseNamingConvention`) |
| Base de dados | PostgreSQL | 17.2 | Instância relacional centralizada com Row-Level Security (RLS) nativo |
| Cache / Armazenamento chave-valor | Redis | 7.4.1 | Acedido via `StackExchange.Redis` para Denylist distribuída |
| Mensageria | Apache Kafka | 3.8.0 | Acedido via `Confluent.Kafka` com padrão Transactional Outbox |
| Testes | xUnit + Testcontainers | 2.9.3 | Testes de integração caixa-preta com `Microsoft.AspNetCore.Mvc.Testing` e contentores reais |
| Build / Contentores | Docker Engine / Dockerfile | 27.3.1 | Imagem base `mcr.microsoft.com/dotnet/aspnet:10.0` e `mcr.microsoft.com/dotnet/sdk:10.0` |

### 3.2 Dependências
| Permitidas (pacote@versão) | Finalidade |
|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer@10.0.0` | Validação de tokens JWT RS256 no pipeline HTTP |
| `System.IdentityModel.Tokens.Jwt@8.3.0` | Emissão e assinatura criptográfica RS256 de JWT e `context_jws` |
| `Microsoft.IdentityModel.Tokens@8.3.0` | Manipulação de chaves RSA e geração do documento JWKS (RFC 7517) |
| `Microsoft.EntityFrameworkCore@10.0.0` | ORM principal, Global Query Filters e SaveChangesInterceptor |
| `Microsoft.EntityFrameworkCore.Relational@10.0.0` | Suporte a transações relacionais e `DbConnectionInterceptor` para RLS |
| `Microsoft.EntityFrameworkCore.Design@10.0.0` | Geração e aplicação de migrações de esquema da base de dados |
| `Npgsql.EntityFrameworkCore.PostgreSQL@10.0.0` | Provider PostgreSQL para Entity Framework Core |
| `EFCore.NamingConventions@10.0.0` | Aplicação automática de `snake_case` em tabelas, colunas, chaves e índices |
| `StackExchange.Redis@2.8.16` | Cliente de baixa latência para leitura e escrita da Denylist no Redis |
| `Confluent.Kafka@2.6.1` | Produtor e consumidor Apache Kafka com suporte a cabeçalhos binários/UTF-8 |
| `Konscious.Security.Cryptography.Argon2@1.3.1` | Cálculo e verificação de hashes de palavra-passe com algoritmo Argon2id |
| `FluentValidation@11.11.0` | Validação determinística de contratos de entrada (requests) |
| `FluentValidation.DependencyInjectionExtensions@11.11.0` | Registo de validadores no contentor de injeção de dependência |
| `Serilog.AspNetCore@9.0.0` | Logging estruturado em JSON com enriquecimento de correlação e identidade |
| `Microsoft.NET.Test.Sdk@17.12.0` | Execução da suite de testes automatizados |
| `xunit@2.9.3` | Framework de testes para os Critérios de Aceitação (`CA-nn.m`) |
| `xunit.runner.visualstudio@3.0.0` | Adaptador de execução de testes xUnit |
| `Microsoft.AspNetCore.Mvc.Testing@10.0.0` | Hospedagem em memória (`WebApplicationFactory`) da API para testes de aceitação |
| `Testcontainers.PostgreSql@4.1.0` | Contentor PostgreSQL 17 efémero para execução dos testes de aceitação |
| `Testcontainers.Redis@4.1.0` | Contentor Redis 7.4 efémero para execução dos testes de aceitação |
| `Testcontainers.Kafka@4.1.0` | Contentor Kafka efémero para execução dos testes de aceitação |

**Dependências proibidas:** Qualquer pacote NuGet não listado na tabela acima (incluindo pacotes privados `Actuar.*`, `AutoMapper`, `MediatR`, `Newtonsoft.Json`, `Dapper`, `Hangfire`, `MassTransit`, `Moq`, `FluentAssertions` e `Bogus`).

---

## 4. Restrições Arquiteturais e Requisitos Não Funcionais

### 4.1 Decisões Arquiteturais (ADR)
| ID | Decisão | Consequência obrigatória no código |
|---|---|---|
| ADR-01 | Segregação Control Plane vs. Data Plane | A tabela `user_account` **DEVE** possuir a coluna `platform_role` (`ENUM-07`: `NONE`, `SUPPORT_AGENT`, `PLATFORM_ADMIN`). Operadores de clientes B2B têm `platform_role = 'NONE'`. Endpoints exclusivos do Control Plane (`EP-05`, `EP-08`) **DEVEM** validar `platform_role` conforme `RN-13` e `RN-18`. |
| ADR-02 | Hierarquia Multi-CNPJ (`EconomicGroup` → `Tenant`) | Todo registo em `tenant` (`ENT-02`) **DEVE** possuir `group_id NOT NULL` referenciando `economic_group.id` (`ENT-01`) com `ON DELETE RESTRICT`. Todo token JWT de contexto ativo **DEVE** incluir a claim `group_id`. |
| ADR-03 | Quatro Escopos Operacionais Explícitos | O tipo `ENUM-03 OPERATIONAL_SCOPE` **DEVE** conter exclusivamente os 4 valores `BUYER`, `SUPPLIER`, `CARRIER` e `TENANT_ADMIN`. Nenhuma rota de negócio aceita escopo fora desta lista. |
| ADR-04 | Troca Explícita de Contexto (`SINGLE` e `MACRO_GROUP`) | O login (`EP-01`) **DEVE** emitir um token inicial com `view_mode = 'NONE'`, `active_scope = null`, `tenant_id = null` e `allowed_tenants = []`. A ativação de um escopo de negócio exige chamada explícita a `EP-02` (`POST /api/v1/auth/switch-context`) em modo `SINGLE` (1 CNPJ) ou `MACRO_GROUP` (1 a 50 CNPJs do mesmo `EconomicGroup`). |
| ADR-05 | Base de Dados Centralizada com Isolamento via `AuthContext` em 4 Camadas | O código **DEVE** implementar: (1) `AuthContextMiddleware` (HTTP) e `KafkaConsumerFilter` (eventos); (2) EF Core Global Query Filter nas entidades `ITenantScoped` (`ENT-03`, `ENT-05`, `ENT-07`) com a expressão `_authContext.IsAuthFlow \|\| _authContext.PlatformRole == PlatformRole.PLATFORM_ADMIN \|\| _authContext.AllowedTenants.Contains(e.TenantId)`; (3) `TenantSaveChangesInterceptor` que valida `tenant_id` em `INSERT`/`UPDATE`/`DELETE`; e (4) `RlsConnectionInterceptor` que executa `SET LOCAL app.allowed_tenants` (UUIDs separados por vírgula) e `SET LOCAL app.auth_flow` (`'true'` nos fluxos de autenticação `EP-01`..`EP-05` e quando `PlatformRole == 'PLATFORM_ADMIN'`, `'false'` caso contrário), suportando a política PostgreSQL RLS `USING (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY(string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]))` e idêntica cláusula `WITH CHECK`. |
| ADR-06 | Validação Stateless (JWKS RS256) + Revogação Imediata via Redis | O serviço **DEVE** assinar JWTs com algoritmo `RS256` (chave RSA 2048 bits, `kid` no cabeçalho) e expor a chave pública em `EP-10` (`GET /.well-known/jwks.json`). Em cada requisição autenticada, o `AuthContextMiddleware` **DEVE** consultar no Redis as chaves `denylist:jti:{jti}`, `denylist:user:{sub}` (e `denylist:user:{act.sub}` quando houver claim `act`) e `denylist:tenant:{id}` para todos os elementos de `allowed_tenants`. |
| ADR-07 | OAuth 2.1 com Rotação de Refresh Token e Deteção de Reuso | A tabela `refresh_token` (`ENT-08`) **DEVE** armazenar apenas o hash SHA-256 (`token_hash`) e o `family_id`. Em `EP-03`, o envio de um token com `status = 'CONSUMED'` **DEVE** revogar todos os tokens da mesma `family_id`, gravar `denylist:user:{user_id}` no Redis, publicar `EV-04 sessao_comprometida` e responder `401 ERR-AUTH-006`. |
| ADR-08 | Token de Delegação / Impersonation Auditável (RFC 8693) | `EP-05` (`POST /api/v1/auth/impersonate`) **DEVE** emitir um `access_token` com `exp = iat + 900` segundos em modo `SINGLE`, sem emitir `RefreshToken`, contendo a claim estruturada `act: { "sub": "<actor_user_id>", "ticket_id": "<ticket_id>" }`. Tokens com claim `act` **NÃO DEVEM** ser aceites em `EP-02` nem em `EP-05`. |
| ADR-09 | Segurança Leste-Oeste e Assinatura de Contexto Assíncrono (`context_jws`) | Todo evento gravado em `outbox_message` (`ENT-10`) e publicado no Kafka **DEVE** conter os cabeçalhos de identidade e o cabeçalho `context_jws` (JWS compacto `RS256` assinado com a chave privada ativa do serviço). O `KafkaConsumerFilter` **DEVE** validar a assinatura de `context_jws` antes de hidratar o `AuthContext`. |

### 4.2 Estrutura de Projeto e Camadas
```text
IdentityService.sln
├── src/
│   ├── IdentityService.Domain/
│   │   ├── Entities/
│   │   │   ├── EconomicGroup.cs
│   │   │   ├── Tenant.cs
│   │   │   ├── TenantScope.cs
│   │   │   ├── UserAccount.cs
│   │   │   ├── TenantMember.cs
│   │   │   ├── Role.cs
│   │   │   ├── MemberRole.cs
│   │   │   ├── RefreshToken.cs
│   │   │   ├── AuthAuditLog.cs
│   │   │   └── OutboxMessage.cs
│   │   ├── Enums/
│   │   │   ├── EntityStatus.cs
│   │   │   ├── BranchType.cs
│   │   │   ├── OperationalScope.cs
│   │   │   ├── UserStatus.cs
│   │   │   ├── RefreshTokenStatus.cs
│   │   │   ├── ViewMode.cs
│   │   │   ├── PlatformRole.cs
│   │   │   ├── AuditEventType.cs
│   │   │   └── OutboxStatus.cs
│   │   └── Interfaces/
│   │       ├── ITenantScoped.cs
│   │       └── IAuthContext.cs
│   ├── IdentityService.Application/
│   │   ├── Context/
│   │   │   └── AuthContext.cs
│   │   ├── DTOs/
│   │   ├── Validators/
│   │   ├── Services/
│   │   │   ├── AuthService.cs
│   │   │   ├── TenantManagementService.cs
│   │   │   ├── JwtTokenService.cs
│   │   │   └── PasswordHashService.cs
│   │   └── Errors/
│   │       └── ErrorCatalog.cs
│   ├── IdentityService.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── IdentityDbContext.cs
│   │   │   ├── Configurations/
│   │   │   ├── Interceptors/
│   │   │   │   ├── TenantSaveChangesInterceptor.cs
│   │   │   │   └── RlsConnectionInterceptor.cs
│   │   │   └── Migrations/
│   │   ├── Cache/
│   │   │   └── RedisDenylistService.cs
│   │   └── Messaging/
│   │       ├── OutboxPublisherWorker.cs
│   │       └── KafkaConsumerFilter.cs
│   └── IdentityService.Api/
│       ├── Controllers/
│       │   ├── AuthController.cs
│       │   ├── TenantsController.cs
│       │   ├── UsersController.cs
│       │   └── JwksController.cs
│       ├── Middleware/
│       │   ├── CorrelationIdMiddleware.cs
│       │   ├── ExceptionHandlingMiddleware.cs
│       │   └── AuthContextMiddleware.cs
│       └── Program.cs
└── tests/
    └── IdentityService.AcceptanceTests/
        ├── Infrastructure/
        │   └── IdentityTestWebApplicationFactory.cs
        └── Scenarios/
```
**Regras de dependência entre camadas:** `IdentityService.Domain` **NÃO DEVE** referenciar nenhuma outra camada nem pacotes de infraestrutura; `IdentityService.Application` **DEVE** referenciar apenas `IdentityService.Domain` e `FluentValidation`; `IdentityService.Infrastructure` **DEVE** referenciar `IdentityService.Domain` e `IdentityService.Application`; `IdentityService.Api` referencia `Application` e `Infrastructure`.

### 4.3 Convenções de Código
| Item | Convenção |
|---|---|
| Nomes de classes / ficheiros | `PascalCase`, um tipo público por ficheiro com o mesmo nome da classe |
| Nomes de tabelas / colunas | `snake_case`, singular, em letras minúsculas (`economic_group`, `tenant_member`, `password_hash`) |
| JSON | `snake_case` em todos os campos de request, response, claims JWT e payloads Kafka (`JsonNamingPolicy.SnakeCaseLower`) |
| Datas | ISO-8601 em UTC com sufixo `Z` (`2026-10-18T16:25:00Z`); timestamps em claims JWT (`iat`, `exp`) e valores Redis de bloqueio de utilizador em segundos Unix epoch (`long`) |
| Identificadores | `UUID` v7 (`Guid.CreateVersion7()`) gerado pela aplicação antes da persistência |

### 4.4 Comportamentos Transversais
| Aspeto | Especificação |
|---|---|
| Autenticação de entrada | Esquema `Bearer` no header `Authorization: Bearer <access_token>`. O token é validado com a chave pública RSA ativa (`RS256`), `ClockSkew = TimeSpan.Zero`, `ValidateIssuer = true` (`iss = "identity-service"`), `ValidateAudience = true` (`aud = "b2b-platform"`), seguido da verificação na Denylist Redis pelo `AuthContextMiddleware`. No payload JWT, claims com valor nulo (`tenant_id`, `group_id`, `active_scope`) são serializadas explicitamente com o literal JSON `null`; a claim `fid` é omitida em tokens de impersonation (`EP-05`) e a claim `act` é omitida em sessões sem delegação |
| Formato de erro | RFC 9457 *Problem Details* (`application/problem+json; charset=utf-8`), contendo obrigatoriamente os campos `type`, `title`, `status`, `code`, `detail` e `correlation_id` |
| Correlação / Rastreio | Header `X-Correlation-Id` (formato `UUID`). Se enviado pelo cliente e válido como UUID, **DEVE** ser preservado; se ausente ou malformado, o `CorrelationIdMiddleware` **DEVE** gerar um novo `UUID` v7, devolvê-lo no header de resposta `X-Correlation-Id` e propagá-lo para `AuthAuditLog.correlation_id` e headers do Kafka. O campo `ip_address` é extraído do primeiro endereço do header `X-Forwarded-For` (antes da primeira vírgula, após `trim()`) ou, se ausente/vazio, de `HttpContext.Connection.RemoteIpAddress?.ToString() ?? "0.0.0.0"` |
| Logging | Logs estruturados em JSON na consola (`stdout`) via Serilog com os campos `timestamp`, `level`, `message`, `correlation_id`, `user_id`, `actor_user_id`, `tenant_id`, `group_id` e `active_scope`. **NÃO DEVE** registar em log `password`, `password_hash`, `refresh_token`, `token_hash`, `access_token` completo nem chaves privadas RSA |
| Auditoria | Toda ação listada em `ENUM-08 AUDIT_EVENT_TYPE` **DEVE** inserir 1 registo na tabela `auth_audit_log` (`ENT-09`) com `user_id`, `actor_user_id`, `tenant_id`, `group_id`, `ticket_id`, `ip_address`, `correlation_id`, `metadata` (`{"view_mode": "<view_mode>", "active_scope": "<active_scope>"}` em `CONTEXT_SWITCHED`; `{"status": "<status>", "reason": "<reason>"}` em `TENANT_STATUS_CHANGED` e `USER_STATUS_CHANGED`; `{}` nos demais eventos) e `created_at` |
| Configuração | Variáveis de ambiente obrigatórias: `DB_CONNECTION_STRING` (string PostgreSQL), `REDIS_CONNECTION_STRING` (string Redis), `KAFKA_BOOTSTRAP_SERVERS` (string host:port), `JWT_ISSUER` (default `"identity-service"`), `JWT_AUDIENCE` (default `"b2b-platform"`), `JWT_RSA_PRIVATE_KEY_PEM` (chave RSA 2048 PEM; se vazia em ambiente de teste, gera par RSA 2048 em memória no arranque com `kid = "key-2026-01"`), `OUTBOX_POLLING_INTERVAL_MS` (int, default `500`) |

### 4.5 Requisitos Não Funcionais (mensuráveis)
| ID | Categoria | Requisito | Métrica / Limiar | Método de verificação |
|---|---|---|---|---|
| RNF-01 | Segurança | Algoritmo de hashing de palavras-passe | Argon2id com `MemorySize = 19456` KiB (19 MiB), `Iterations = 2`, `DegreeOfParallelism = 1`, salt aleatório criptográfico de 16 bytes e hash de 32 bytes, codificado no formato PHC `$argon2id$v=19$m=19456,t=2,p=1$<salt_b64>$<hash_b64>` | Inspeção do código em `PasswordHashService` e asserção do prefixo `$argon2id$v=19$m=19456,t=2,p=1$` na coluna `user_account.password_hash` nos testes automatizados |
| RNF-02 | Segurança | Algoritmo de assinatura de JWT e `context_jws` | RSA-SHA256 (`RS256`) com chave mínima de 2048 bits e cabeçalho `kid` não vazio presente tanto no JWT quanto no endpoint `GET /.well-known/jwks.json` | Teste automatizado de `EP-10` validando a assinatura criptográfica do `access_token` e do `context_jws` apenas com a chave pública obtida do JWKS |
| RNF-03 | Segurança | Tempo de vida de tokens e tolerância de relógio | `access_token` com `exp = iat + 900` segundos (15 minutos); `refresh_token` com `expires_at = created_at + 604800` segundos (7 dias); `ClockSkew = 0` segundos na validação | Asserção direta das claims `iat` e `exp` (`exp - iat == 900`) e da coluna `refresh_token.expires_at` nos testes de aceitação |
| RNF-04 | Desempenho | Latência de verificação na Denylist Redis e troca de contexto | `EP-02` (`switch-context`) e `EP-03` (`refresh`) com percentil 95 (`p95`) ≤ 150 ms; `EP-01` (`login` com Argon2id) com `p95` ≤ 400 ms em contentor local | Medição de tempo de resposta HTTP nos testes de integração automatizados |
| RNF-05 | Confiabilidade | Entrega assíncrona via Transactional Outbox | O `OutboxPublisherWorker` **DEVE** ler e publicar mensagens `PENDING` a cada `500` ms em lotes de até `50` registos, com até `5` tentativas em falha antes de transitar para `FAILED` | Teste de integração verificando transição `PENDING` → `PUBLISHED` em `outbox_message` e receção da mensagem no tópico Kafka em ≤ 3 segundos |
| RNF-06 | Segurança | Isolamento Multi-Tenant em 4 camadas | 100 % das consultas e mutações às entidades `ITenantScoped` (`tenant_scope`, `tenant_member`, `member_role`) em endpoints de gestão **DEVEM** aplicar simultaneamente filtro EF Core, interceptor de escrita e variáveis de sessão RLS no PostgreSQL | Testes automatizados marcados com `@critico` tentando ler e escrever registos de outro `tenant_id` fora de `allowed_tenants` |

---

## 5. Modelo de Dados

### 5.1 Diagrama Entidade-Relacionamento
```mermaid
erDiagram
    ECONOMIC_GROUP ||--|{ TENANT : "agrupa"
    TENANT ||--|{ TENANT_SCOPE : "habilita"
    TENANT ||--o{ TENANT_MEMBER : "possui"
    USER_ACCOUNT ||--o{ TENANT_MEMBER : "vincula"
    TENANT_MEMBER ||--o{ MEMBER_ROLE : "detem"
    ROLE ||--o{ MEMBER_ROLE : "atribui"
    USER_ACCOUNT ||--o{ REFRESH_TOKEN : "mantem"
    USER_ACCOUNT ||--o{ AUTH_AUDIT_LOG : "gera"
    TENANT ||--o{ AUTH_AUDIT_LOG : "contextualiza"
    ECONOMIC_GROUP ||--o{ AUTH_AUDIT_LOG : "agrupa_auditoria"
    OUTBOX_MESSAGE {
        uuid id PK
        varchar topic
        varchar partition_key
        varchar status
    }
```

### 5.2 Tipos Enumerados

#### ENUM-01 `ENTITY_STATUS`
| Valor | Significado |
|---|---|
| `ACTIVE` | Registo ativo e autorizado a operar na plataforma |
| `SUSPENDED` | Registo suspenso administrativamente; impede autenticação, troca de contexto e operações no CNPJ ou vínculo |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `ACTIVE` | `SUSPENDED` | RF-09 (`EP-08` suspensão de `Tenant`) |
| `SUSPENDED` | `ACTIVE` | RF-09 (`EP-08` reativação de `Tenant`) |

#### ENUM-02 `BRANCH_TYPE`
| Valor | Significado |
|---|---|
| `HQ` | Matriz da empresa (sede administrativa da organização) |
| `BRANCH` | Filial ou unidade operacional da empresa |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| Não se aplica | Não se aplica | Valor fixado na criação do `Tenant` (`RF-07`) sem transição de estado |

#### ENUM-03 `OPERATIONAL_SCOPE`
| Valor | Significado |
|---|---|
| `BUYER` | Escopo operacional de Comprador (registo de demandas, participação em leilões e acompanhamento de pedidos) |
| `SUPPLIER` | Escopo operacional de Fornecedor (publicação de fornecimentos, atualização de stock e envio de lances) |
| `CARRIER` | Escopo operacional de Transportadora (receção de solicitações de frete e envio de cotações) |
| `TENANT_ADMIN` | 4º escopo dedicado à gestão corporativa (administração de CNPJs do grupo, membros, papéis e visão macro) |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| Não se aplica | Não se aplica | Catálogo fixo de escopos operacionais (ADR-03) |

#### ENUM-04 `USER_STATUS`
| Valor | Significado |
|---|---|
| `ACTIVE` | Conta de pessoa física ativa e apta a autenticar-se |
| `LOCKED` | Conta temporariamente bloqueada devido a `5` tentativas consecutivas de login com palavra-passe incorreta |
| `SUSPENDED` | Conta suspensa administrativamente por tempo indeterminado |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `ACTIVE` | `LOCKED` | RF-01 / RN-03 (ao atingir `failed_login_attempts == 5`) |
| `LOCKED` | `ACTIVE` | RF-01 / RN-03 (expiração automática do bloqueio quando `now() >= locked_until` no início de nova tentativa em `EP-01`, repondo `failed_login_attempts = 0` e `locked_until = NULL` antes de validar a palavra-passe) |
| `ACTIVE` | `SUSPENDED` | RF-10 (`EP-09` suspensão administrativa de utilizador) |
| `LOCKED` | `SUSPENDED` | RF-10 (`EP-09` suspensão administrativa de utilizador bloqueado) |
| `SUSPENDED` | `ACTIVE` | RF-10 (`EP-09` reativação administrativa de utilizador) |

#### ENUM-05 `REFRESH_TOKEN_STATUS`
| Valor | Significado |
|---|---|
| `ACTIVE` | Token válido e disponível para uma única operação de rotação em `EP-03` |
| `CONSUMED` | Token já utilizado numa rotação anterior; a sua reapresentação aciona a deteção de reuso (`RN-11`) |
| `REVOKED` | Token invalidado por logout, suspensão de utilizador/tenant ou deteção de reuso na família |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `ACTIVE` | `CONSUMED` | RF-04 / RN-12 (rotação bem-sucedida em `EP-03`) |
| `ACTIVE` | `REVOKED` | RF-04 / RN-11 (reuso na família), RF-05 (logout em `EP-04`), RF-09 (suspensão de tenant), RF-10 (suspensão de utilizador) |
| `CONSUMED` | `REVOKED` | RF-04 / RN-11 (deteção de reuso de token na mesma `family_id`), RF-05 (logout em `EP-04`), RF-09 (suspensão de tenant), RF-10 (suspensão de utilizador) |

#### ENUM-06 `VIEW_MODE`
| Valor | Significado |
|---|---|
| `NONE` | Estado inicial de sessão emitido no login (`EP-01`) antes da seleção de contexto; não permite operações de domínio |
| `SINGLE` | Modo operacional unitário vinculado a exatamente 1 `Tenant` (`tenant_id` preenchido e `allowed_tenants` com 1 elemento) |
| `MACRO_GROUP` | Modo de visão consolidada/mapa vinculado a um `EconomicGroup` (`tenant_id = null` e `allowed_tenants` com 1 a 50 CNPJs do grupo) |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `NONE` | `SINGLE` | RF-02 (`EP-02` troca para contexto unitário) |
| `NONE` | `MACRO_GROUP` | RF-03 (`EP-02` troca para visão macro) |
| `SINGLE` | `SINGLE` | RF-02 (`EP-02` troca entre CNPJs ou escopos em modo unitário) |
| `SINGLE` | `MACRO_GROUP` | RF-03 (`EP-02` transição de modo unitário para visão macro) |
| `MACRO_GROUP` | `SINGLE` | RF-02 (`EP-02` transição de visão macro para 1 CNPJ específico) |
| `MACRO_GROUP` | `MACRO_GROUP` | RF-03 (`EP-02` alteração da lista de CNPJs ou escopo em visão macro) |

#### ENUM-07 `PLATFORM_ROLE`
| Valor | Significado |
|---|---|
| `NONE` | Utilizador padrão do Data Plane (clientes B2B) sem privilégios de administração da plataforma SaaS |
| `SUPPORT_AGENT` | Analista de suporte técnico da plataforma autorizado a executar impersonation auditável (`EP-05`) |
| `PLATFORM_ADMIN` | Administrador global do Control Plane autorizado a administrar grupos económicos, tenants, suspensão e impersonation (`EP-05`, `EP-06`, `EP-08`, `EP-09`) |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| Não se aplica | Não se aplica | Definido no registo do utilizador (`ENT-04`) |

#### ENUM-08 `AUDIT_EVENT_TYPE`
| Valor | Significado |
|---|---|
| `LOGIN_SUCCESS` | Autenticação de pessoa física concluída com sucesso em `EP-01` |
| `LOGIN_FAILED` | Falha de autenticação por credenciais inválidas em `EP-01` |
| `USER_LOCKED` | Bloqueio temporário automático da conta após 5 tentativas falhadas em `EP-01` |
| `CONTEXT_SWITCHED` | Troca de contexto (`SINGLE` ou `MACRO_GROUP`) concluída com sucesso em `EP-02` |
| `TOKEN_REFRESHED` | Rotação de `RefreshToken` concluída com sucesso em `EP-03` |
| `TOKEN_REUSE_DETECTED` | Deteção de tentativa de reuso de `RefreshToken` já `CONSUMED` em `EP-03` |
| `LOGOUT` | Encerramento explícito de sessão em `EP-04` |
| `IMPERSONATION_STARTED` | Emissão de token de delegação para suporte técnico em `EP-05` |
| `TENANT_CREATED` | Cadastro de novo `Tenant` e respetivos `TenantScope` em `EP-06` |
| `MEMBER_ADDED` | Vinculação de `TenantMember` e `MemberRole` a um `Tenant` em `EP-07` |
| `TENANT_STATUS_CHANGED` | Suspensão ou reativação de `Tenant` em `EP-08` |
| `USER_STATUS_CHANGED` | Suspensão ou reativação administrativa de `UserAccount` em `EP-09` |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| Não se aplica | Não se aplica | Registo de auditoria imutável (append-only) |

#### ENUM-09 `OUTBOX_STATUS`
| Valor | Significado |
|---|---|
| `PENDING` | Mensagem gravada na transação ACID aguardando publicação no Apache Kafka |
| `PUBLISHED` | Mensagem publicada e confirmada (`ack`) pelo broker Apache Kafka |
| `FAILED` | Mensagem cuja publicação falhou após atingir o limite de `5` tentativas |

**Transições de estado permitidas:**
| De | Para | Gatilho (RF/RN) |
|---|---|---|
| `PENDING` | `PUBLISHED` | Confirmação de entrega no Kafka pelo `OutboxPublisherWorker` (RNF-05) |
| `PENDING` | `FAILED` | Falha na 5ª tentativa de publicação pelo `OutboxPublisherWorker` (`retry_count == 5`) |

---

### 5.3 Dicionário de Dados

#### ENT-01 `economic_group`
**Descrição:** Representa o Grupo Económico (Holding ou organização empresarial) que agrupa 1 a N CNPJs (`Tenant`).
**Restrições gerais / Invariantes:** Todo `EconomicGroup` possui pelo menos um nome não vazio e um estado válido em `ENUM-01`.
**Isolamento multi-tenant:** Não, entidade raiz da hierarquia corporativa (`PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do grupo económico |
| `name` | `VARCHAR(160)` | NÃO | Nenhum | Nenhuma | `CHECK (char_length(trim(name)) >= 2)` | Nome empresarial da holding ou grupo económico |
| `status` | `VARCHAR(16)` | NÃO | `'ACTIVE'` | Nenhuma | `CHECK (status IN ('ACTIVE', 'SUSPENDED'))` | Estado operacional do grupo (`ENUM-01`) |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de criação do registo |
| `updated_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora da última atualização |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_economic_group` | `id` | B-tree | Sim |
| `ix_economic_group_status` | `status` | B-tree | Não |

#### ENT-02 `tenant`
**Descrição:** Representa uma Pessoa Jurídica (CNPJ) participante da plataforma B2B, sempre vinculada a um `EconomicGroup`.
**Restrições gerais / Invariantes:** Um `Tenant` pertence a exatamente um `EconomicGroup` e possui um `cnpj` único de 14 dígitos numéricos validado pelo módulo 11 (`RN-15`).
**Isolamento multi-tenant:** Não, tabela mestre de definição de CNPJs (`PRS-07`); a autorização de escrita e leitura administrativa valida `id` contra `AuthContext.AllowedTenants` ou `group_id == AuthContext.GroupId`.

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do Tenant |
| `group_id` | `UUID` | NÃO | Nenhum | FK → `economic_group.id`, `ON DELETE RESTRICT` | Formato UUID | Identificador do grupo económico pai |
| `cnpj` | `CHAR(14)` | NÃO | Nenhum | UQ | `CHECK (cnpj ~ '^[0-9]{14}$')` | CNPJ composto apenas por 14 dígitos numéricos |
| `legal_name` | `VARCHAR(200)` | NÃO | Nenhum | Nenhuma | `CHECK (char_length(trim(legal_name)) >= 2)` | Razão social da pessoa jurídica |
| `branch_type` | `VARCHAR(16)` | NÃO | Nenhum | Nenhuma | `CHECK (branch_type IN ('HQ', 'BRANCH'))` | Tipo de unidade: Matriz ou Filial (`ENUM-02`) |
| `geo_lat` | `NUMERIC(9,6)` | NÃO | Nenhum | Nenhuma | `CHECK (geo_lat >= -90.0 AND geo_lat <= 90.0)` | Latitude geográfica para renderização no mapa |
| `geo_lng` | `NUMERIC(9,6)` | NÃO | Nenhum | Nenhuma | `CHECK (geo_lng >= -180.0 AND geo_lng <= 180.0)` | Longitude geográfica para renderização no mapa |
| `status` | `VARCHAR(16)` | NÃO | `'ACTIVE'` | Nenhuma | `CHECK (status IN ('ACTIVE', 'SUSPENDED'))` | Estado operacional do CNPJ (`ENUM-01`) |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de criação |
| `updated_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora da última atualização |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_tenant` | `id` | B-tree | Sim |
| `uq_tenant_cnpj` | `cnpj` | B-tree | Sim |
| `ix_tenant_group_id` | `group_id` | B-tree | Não |

#### ENT-03 `tenant_scope`
**Descrição:** Define quais dos 4 escopos operacionais (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) estão habilitados para um determinado `Tenant`.
**Restrições gerais / Invariantes:** É proibida a existência de mais do que um registo para o mesmo par `(tenant_id, scope)`.
**Isolamento multi-tenant:** Sim, coluna `tenant_id` (implementa `ITenantScoped`, filtrada por EF Core Global Query Filter e PostgreSQL RLS via `app.allowed_tenants`, com bypass controlado por `app.auth_flow = 'true'` nos fluxos de autenticação conforme `PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do escopo do tenant |
| `tenant_id` | `UUID` | NÃO | Nenhum | FK → `tenant.id`, `ON DELETE CASCADE` | `UQ(tenant_id, scope)` | Identificador do Tenant |
| `scope` | `VARCHAR(16)` | NÃO | Nenhum | Nenhuma | `CHECK (scope IN ('BUYER', 'SUPPLIER', 'CARRIER', 'TENANT_ADMIN'))` | Escopo operacional (`ENUM-03`) |
| `is_active` | `BOOLEAN` | NÃO | `true` | Nenhuma | Nenhuma | Indica se o escopo está ativo para o CNPJ |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de criação |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_tenant_scope` | `id` | B-tree | Sim |
| `uq_tenant_scope_tenant_id_scope` | `tenant_id, scope` | B-tree | Sim |

#### ENT-04 `user_account`
**Descrição:** Representa a conta de Pessoa Física global na plataforma, vinculável a múltiplos CNPJs (`TenantMember`) ou habilitada para atuar no Control Plane (`platform_role`).
**Restrições gerais / Invariantes:** O `email` é sempre armazenado em letras minúsculas sem espaços nas extremidades e é único na plataforma (`PRS-11`).
**Isolamento multi-tenant:** Não, entidade global de identidade de pessoa física (`PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do utilizador |
| `email` | `VARCHAR(320)` | NÃO | Nenhum | UQ | `CHECK (email = lower(trim(email)) AND char_length(email) >= 5)` | Endereço de e-mail único em minúsculas |
| `password_hash` | `VARCHAR(255)` | NÃO | Nenhum | Nenhuma | Prefixo `$argon2id$v=19$m=19456,t=2,p=1$` | Hash Argon2id da palavra-passe |
| `name` | `VARCHAR(160)` | NÃO | Nenhum | Nenhuma | `CHECK (char_length(trim(name)) >= 2)` | Nome completo da pessoa física |
| `status` | `VARCHAR(16)` | NÃO | `'ACTIVE'` | Nenhuma | `CHECK (status IN ('ACTIVE', 'LOCKED', 'SUSPENDED'))` | Estado da conta do utilizador (`ENUM-04`) |
| `platform_role` | `VARCHAR(24)` | NÃO | `'NONE'` | Nenhuma | `CHECK (platform_role IN ('NONE', 'SUPPORT_AGENT', 'PLATFORM_ADMIN'))` | Papel no Control Plane da plataforma (`ENUM-07`) |
| `failed_login_attempts` | `INTEGER` | NÃO | `0` | Nenhuma | `CHECK (failed_login_attempts >= 0)` | Contador de falhas consecutivas de palavra-passe |
| `locked_until` | `TIMESTAMPTZ` | SIM | `NULL` | Nenhuma | UTC | Instante até ao qual a conta permanece bloqueada (`RN-03`) |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de criação |
| `updated_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora da última atualização |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_user_account` | `id` | B-tree | Sim |
| `uq_user_account_email` | `email` | B-tree | Sim |

#### ENT-05 `tenant_member`
**Descrição:** Representa o vínculo de um utilizador (`UserAccount`) a um `Tenant` específico, definindo se é proprietário (`is_owner`) e se possui permissão de visão agregada de grupo (`can_view_group`).
**Restrições gerais / Invariantes:** Cada par `(tenant_id, user_id)` é único. Quando `is_owner = true`, o membro detém automaticamente todos os papéis (`Role`) e permissões do catálogo (§5.5) correspondentes aos escopos ativos (`tenant_scope.is_active == true`) daquele `Tenant` (`PRS-03`), mas a inclusão desse `Tenant` em operações `MACRO_GROUP` continua a exigir `can_view_group == true` (`RN-09`, `PRS-05`).
**Isolamento multi-tenant:** Sim, coluna `tenant_id` (implementa `ITenantScoped`, sujeita ao Global Query Filter, SaveChanges Interceptor e PostgreSQL RLS via `app.allowed_tenants`, com bypass `app.auth_flow = 'true'` apenas nos fluxos internos de autenticação).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do vínculo de membro |
| `tenant_id` | `UUID` | NÃO | Nenhum | FK → `tenant.id`, `ON DELETE CASCADE` | `UQ(tenant_id, user_id)` | Identificador do Tenant |
| `user_id` | `UUID` | NÃO | Nenhum | FK → `user_account.id`, `ON DELETE RESTRICT` | `UQ(tenant_id, user_id)` | Identificador do utilizador vinculado |
| `is_owner` | `BOOLEAN` | NÃO | `false` | Nenhuma | Nenhuma | Indica se é proprietário do Tenant com todas as roles e permissões dos escopos ativos |
| `can_view_group` | `BOOLEAN` | NÃO | `false` | Nenhuma | Nenhuma | Autoriza a inclusão deste Tenant em operações `MACRO_GROUP` (obrigatório mesmo se `is_owner = true`) |
| `status` | `VARCHAR(16)` | NÃO | `'ACTIVE'` | Nenhuma | `CHECK (status IN ('ACTIVE', 'SUSPENDED'))` | Estado do vínculo do membro no Tenant (`ENUM-01`) |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de criação |
| `updated_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora da última atualização |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_tenant_member` | `id` | B-tree | Sim |
| `uq_tenant_member_tenant_user` | `tenant_id, user_id` | B-tree | Sim |
| `ix_tenant_member_user_id` | `user_id` | B-tree | Não |

#### ENT-06 `role`
**Descrição:** Catálogo de papéis operacionais do sistema, onde cada papel pertence a exatamente um `OperationalScope` e agrega uma lista de permissões granulares `dominio:acao`.
**Restrições gerais / Invariantes:** O campo `permissions` é um array JSONB não vazio de strings no formato `^[a-z_]+:[a-z_]+$`.
**Isolamento multi-tenant:** Não, catálogo padronizado de papéis da plataforma (`PRS-03`, `PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do papel |
| `name` | `VARCHAR(64)` | NÃO | Nenhum | UQ | `CHECK (name ~ '^[A-Z0-9_]{3,64}$')` | Código canónico do papel (ex.: `SUPPLIER_SALES`, `GROUP_MANAGER`) |
| `scope` | `VARCHAR(16)` | NÃO | Nenhum | Nenhuma | `CHECK (scope IN ('BUYER', 'SUPPLIER', 'CARRIER', 'TENANT_ADMIN'))` | Escopo operacional ao qual o papel pertence (`ENUM-03`) |
| `permissions` | `JSONB` | NÃO | `'[]'::jsonb` | Nenhuma | `CHECK (jsonb_typeof(permissions) = 'array')` | Lista JSON de permissões granulares (ex.: `["fornecimento:publicar", "leilao:lance"]`) |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de criação |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_role` | `id` | B-tree | Sim |
| `uq_role_name` | `name` | B-tree | Sim |
| `ix_role_scope` | `scope` | B-tree | Não |

#### ENT-07 `member_role`
**Descrição:** Associação entre um `TenantMember` e uma `Role` dentro de um `Tenant`.
**Restrições gerais / Invariantes:** É proibida a duplicidade de `(tenant_member_id, role_id)`. A coluna `tenant_id` é desnormalizada a partir de `tenant_member.tenant_id` para permitir aplicação direta do EF Core Global Query Filter e PostgreSQL RLS sem *join*.
**Isolamento multi-tenant:** Sim, coluna `tenant_id` (implementa `ITenantScoped`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único da associação |
| `tenant_id` | `UUID` | NÃO | Nenhum | FK → `tenant.id`, `ON DELETE CASCADE` | Formato UUID | Identificador do Tenant para filtro direto RLS/ORM |
| `tenant_member_id` | `UUID` | NÃO | Nenhum | FK → `tenant_member.id`, `ON DELETE CASCADE` | `UQ(tenant_member_id, role_id)` | Identificador do membro do Tenant |
| `role_id` | `UUID` | NÃO | Nenhum | FK → `role.id`, `ON DELETE RESTRICT` | `UQ(tenant_member_id, role_id)` | Identificador do papel atribuído |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de atribuição |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_member_role` | `id` | B-tree | Sim |
| `uq_member_role_member_role` | `tenant_member_id, role_id` | B-tree | Sim |
| `ix_member_role_tenant_id` | `tenant_id` | B-tree | Não |

#### ENT-08 `refresh_token`
**Descrição:** Registo de `RefreshToken` da sessão OAuth 2.1, agrupado por `family_id` para deteção de roubo/reuso e persistência do último contexto operacional ativo da sessão (`PRS-02`).
**Restrições gerais / Invariantes:** Em cada `family_id`, existe no máximo 1 registo com `status = 'ACTIVE'`. O token em texto claro **NÃO DEVE** ser persistido; armazena-se apenas `token_hash` (SHA-256 hex em minúsculas).
**Isolamento multi-tenant:** Não, entidade de sessão vinculada a `user_id` e `family_id` (`PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do registo de refresh token |
| `family_id` | `UUID` | NÃO | Nenhum | Nenhuma | Formato UUID | Identificador da família criada no login (`EP-01`) |
| `user_id` | `UUID` | NÃO | Nenhum | FK → `user_account.id`, `ON DELETE CASCADE` | Formato UUID | Utilizador titular da sessão |
| `token_hash` | `CHAR(64)` | NÃO | Nenhum | UQ | `CHECK (token_hash ~ '^[0-9a-f]{64}$')` | Hash SHA-256 em hexadecimal minúsculo do token opaco de 43 caracteres base64url |
| `status` | `VARCHAR(16)` | NÃO | `'ACTIVE'` | Nenhuma | `CHECK (status IN ('ACTIVE', 'CONSUMED', 'REVOKED'))` | Estado na máquina de rotação (`ENUM-05`) |
| `replaced_by_id` | `UUID` | SIM | `NULL` | FK → `refresh_token.id`, `ON DELETE SET NULL` | Formato UUID | Aponta para o novo `refresh_token` gerado na rotação |
| `view_mode` | `VARCHAR(16)` | NÃO | `'NONE'` | Nenhuma | `CHECK (view_mode IN ('NONE', 'SINGLE', 'MACRO_GROUP'))` | Modo de visão ativo na sessão (`ENUM-06`) |
| `group_id` | `UUID` | SIM | `NULL` | FK → `economic_group.id`, `ON DELETE SET NULL` | Formato UUID | Grupo económico ativo na sessão |
| `active_tenant_id` | `UUID` | SIM | `NULL` | FK → `tenant.id`, `ON DELETE SET NULL` | Formato UUID | Tenant ativo quando `view_mode = 'SINGLE'` |
| `allowed_tenants` | `JSONB` | NÃO | `'[]'::jsonb` | Nenhuma | `CHECK (jsonb_typeof(allowed_tenants) = 'array')` | Array JSON de UUIDs dos tenants autorizados no contexto ativo |
| `active_scope` | `VARCHAR(16)` | SIM | `NULL` | Nenhuma | `CHECK (active_scope IS NULL OR active_scope IN ('BUYER', 'SUPPLIER', 'CARRIER', 'TENANT_ADMIN'))` | Escopo ativo na sessão (`ENUM-03`) |
| `expires_at` | `TIMESTAMPTZ` | NÃO | Nenhum | Nenhuma | `CHECK (expires_at > created_at)` | Data e hora de expiração (`created_at + 604800 s`) |
| `consumed_at` | `TIMESTAMPTZ` | SIM | `NULL` | Nenhuma | UTC | Data e hora em que o token foi consumido em `EP-03` |
| `revoked_at` | `TIMESTAMPTZ` | SIM | `NULL` | Nenhuma | UTC | Data e hora em que o token foi revogado |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de emissão |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_refresh_token` | `id` | B-tree | Sim |
| `uq_refresh_token_token_hash` | `token_hash` | B-tree | Sim |
| `ix_refresh_token_family_id` | `family_id` | B-tree | Não |
| `ix_refresh_token_user_id_status` | `user_id, status` | B-tree | Não |

#### ENT-09 `auth_audit_log`
**Descrição:** Registo imutável (append-only) de todos os eventos relevantes de autenticação, autorização, troca de contexto, bloqueio e impersonation.
**Restrições gerais / Invariantes:** Registos desta tabela **NÃO DEVEM** sofrer `UPDATE` nem `DELETE`.
**Isolamento multi-tenant:** Não, tabela transversal de auditoria de segurança (`PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único do registo de auditoria |
| `event_type` | `VARCHAR(32)` | NÃO | Nenhum | Nenhuma | `CHECK (event_type IN ('LOGIN_SUCCESS', 'LOGIN_FAILED', 'USER_LOCKED', 'CONTEXT_SWITCHED', 'TOKEN_REFRESHED', 'TOKEN_REUSE_DETECTED', 'LOGOUT', 'IMPERSONATION_STARTED', 'TENANT_CREATED', 'MEMBER_ADDED', 'TENANT_STATUS_CHANGED', 'USER_STATUS_CHANGED'))` | Tipo do evento auditado (`ENUM-08`) |
| `user_id` | `UUID` | SIM | `NULL` | FK → `user_account.id`, `ON DELETE SET NULL` | Formato UUID | Utilizador principal ou alvo da operação |
| `actor_user_id` | `UUID` | SIM | `NULL` | FK → `user_account.id`, `ON DELETE SET NULL` | Formato UUID | Utilizador real que executou a operação (preenchido em impersonation ou ações administrativas) |
| `tenant_id` | `UUID` | SIM | `NULL` | FK → `tenant.id`, `ON DELETE SET NULL` | Formato UUID | Preenchido quando o evento ocorre num Tenant específico; NULL em eventos globais de utilizador ou visão macro |
| `group_id` | `UUID` | SIM | `NULL` | FK → `economic_group.id`, `ON DELETE SET NULL` | Formato UUID | Grupo económico associado ao evento |
| `ticket_id` | `VARCHAR(32)` | SIM | `NULL` | Nenhuma | `CHECK (ticket_id IS NULL OR ticket_id ~ '^[A-Z]{2,10}-[0-9]{1,10}$')` | Código do chamado técnico em eventos de impersonation |
| `ip_address` | `VARCHAR(45)` | NÃO | `'0.0.0.0'` | Nenhuma | IPv4 ou IPv6 | Endereço IP de origem da requisição |
| `correlation_id` | `UUID` | NÃO | Nenhum | Nenhuma | Formato UUID | Identificador de correlação propagado da requisição |
| `metadata` | `JSONB` | NÃO | `'{}'::jsonb` | Nenhuma | `CHECK (jsonb_typeof(metadata) = 'object')` | Dados complementares não sensíveis do evento |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Instante exato do evento |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_auth_audit_log` | `id` | B-tree | Sim |
| `ix_auth_audit_log_user_id_created_at` | `user_id, created_at` | B-tree | Não |
| `ix_auth_audit_log_tenant_id_created_at` | `tenant_id, created_at` | B-tree | Não |

#### ENT-10 `outbox_message`
**Descrição:** Tabela de Transactional Outbox para garantia de publicação *at-least-once* no Apache Kafka sem *dual-write*.
**Restrições gerais / Invariantes:** Toda mensagem é inserida na mesma transação ACID da mutação de negócio com `status = 'PENDING'`.
**Isolamento multi-tenant:** Não, tabela de infraestrutura de mensageria lida pelo `OutboxPublisherWorker` (`PRS-07`).

| Coluna | Tipo físico | Nulo | Default | Chave | Restrições (CHECK/UQ/formato) | Descrição |
|---|---|---|---|---|---|---|
| `id` | `UUID` | NÃO | Nenhum | PK | Formato UUID | Identificador único da mensagem (usado como `event_id` para idempotência) |
| `topic` | `VARCHAR(128)` | NÃO | Nenhum | Nenhuma | `CHECK (topic IN ('empresa_cadastrada', 'tenant_bloqueado', 'usuario_bloqueado', 'sessao_comprometida'))` | Nome do tópico Kafka de destino |
| `partition_key` | `VARCHAR(128)` | NÃO | Nenhum | Nenhuma | `CHECK (char_length(partition_key) >= 1)` | Chave de partição Kafka (`tenant_id` ou `user_id`) |
| `headers` | `JSONB` | NÃO | Nenhum | Nenhuma | `CHECK (jsonb_typeof(headers) = 'object')` | Cabeçalhos obrigatórios incluindo `context_jws` e `correlation_id` |
| `payload` | `JSONB` | NÃO | Nenhum | Nenhuma | `CHECK (jsonb_typeof(payload) = 'object')` | Corpo serializado do evento |
| `status` | `VARCHAR(16)` | NÃO | `'PENDING'` | Nenhuma | `CHECK (status IN ('PENDING', 'PUBLISHED', 'FAILED'))` | Estado de envio (`ENUM-09`) |
| `retry_count` | `INTEGER` | NÃO | `0` | Nenhuma | `CHECK (retry_count >= 0 AND retry_count <= 5)` | Número de tentativas de publicação realizadas |
| `created_at` | `TIMESTAMPTZ` | NÃO | `now()` | Nenhuma | UTC | Data e hora de gravação na transação |
| `processed_at` | `TIMESTAMPTZ` | SIM | `NULL` | Nenhuma | UTC | Data e hora da publicação ou falha final |

**Índices:**
| Nome | Colunas | Tipo | Único |
|---|---|---|---|
| `pk_outbox_message` | `id` | B-tree | Sim |
| `ix_outbox_message_status_created_at` | `status, created_at` | B-tree | Não |

---

### 5.4 Estruturas em Cache / Armazenamento Volátil
| Padrão da chave | Tipo | Valor | TTL | Escrito por (RF/RN) | Lido por (RF/RN) |
|---|---|---|---|---|---|
| `denylist:jti:{jti}` | String | `"1"` | `max(1, token.exp - now_unix)` segundos (máximo `900` s) | RF-02, RF-03, RF-05 (`RN-10`) | `AuthContextMiddleware` em todas as rotas autenticadas (`RN-05`, `RN-10`) |
| `denylist:user:{user_id}` | String | Timestamp Unix em segundos (`now_unix`, ex.: `"1792351500"`) do bloqueio ou comprometimento | `900` segundos (`USER_DENYLIST_TTL_SEC`, igual à vida máxima do `access_token`) | RF-01 (`RN-03`), RF-04 (`RN-11`), RF-10 | `AuthContextMiddleware` (`RN-04`: rejeita token se `token.iat <= valor_redis` para `sub` ou `act.sub`) |
| `denylist:tenant:{tenant_id}` | String | `"1"` | `86400` segundos (`TENANT_DENYLIST_TTL_SEC`; removida com `DEL` se o Tenant for reativado em `RF-09`) | RF-09 (`EP-08` quando `status = 'SUSPENDED'`) | `AuthContextMiddleware`, RF-02, RF-03, RF-04, RF-06 (`RN-07`: consulta `MGET` para `target_tenant_id` e todos os itens de `allowed_tenants`) |

---

### 5.5 Dados Iniciais (Seed)
A primeira migração (ou inicializador determinístico de base de dados no arranque) **DEVE** garantir a existência dos 4 papéis padrão abaixo na tabela `role` (`ENT-06`), utilizando `INSERT INTO role ON CONFLICT (name) DO NOTHING`:

| `id` | `name` | `scope` | `permissions` (JSONB) |
|---|---|---|---|
| `01900000-0000-7000-8000-000000000001` | `BUYER_OPERATOR` | `BUYER` | `["demanda:criar", "demanda:listar", "leilao:participar", "pedido:visualizar"]` |
| `01900000-0000-7000-8000-000000000002` | `SUPPLIER_SALES` | `SUPPLIER` | `["fornecimento:publicar", "estoque:atualizar", "leilao:lance", "pedido:confirmar"]` |
| `01900000-0000-7000-8000-000000000003` | `CARRIER_DISPATCHER` | `CARRIER` | `["transportadora:configurar", "frete:visualizar", "frete:cotar"]` |
| `01900000-0000-7000-8000-000000000004` | `GROUP_MANAGER` | `TENANT_ADMIN` | `["tenant:criar", "tenant:listar", "membro:vincular", "membro:listar", "usuario:suspender", "mapa:visualizar"]` |

---

## 6. Contratos de Interface

### 6.1 Convenções Globais da API
| Item | Valor |
|---|---|
| Base path / versão | `/api/v1` (exceção única: endpoint de descoberta `GET /.well-known/jwks.json` na raiz conforme RFC 8414 / RFC 7517) |
| Content-Type | Pedidos e respostas de sucesso: `application/json; charset=utf-8`; respostas de erro (4xx/5xx): `application/problem+json; charset=utf-8` |
| Header de autenticação | `Authorization: Bearer <access_token>` (JWT JWS `RS256` contendo as claims `jti`, `sub`, `fid`, `iat`, `exp`, `iss`, `aud`, `platform_role`, `group_id`, `tenant_id`, `allowed_tenants`, `view_mode`, `active_scope`, `roles`, `permissions` e, em impersonation, `act`) |
| Header de correlação | `X-Correlation-Id` (UUID; opcional no pedido; se omitido ou inválido, gerado pelo servidor como UUID v7 e sempre devolvido no header de resposta `X-Correlation-Id` e no campo `correlation_id` de erros) |
| Paginação | Não se aplica (os endpoints desta especificação operam sobre recursos unitários ou listas limitadas a `MAX_MACRO_TENANTS = 50`) |
| Corpo de erro (modelo) | ver bloco abaixo |

```json
{
  "type": "https://identity.b2b.local/errors/ERR-AUTH-001",
  "title": "Credenciais inválidas",
  "status": 401,
  "code": "ERR-AUTH-001",
  "detail": "O e-mail ou a palavra-passe informados não correspondem a uma conta válida.",
  "correlation_id": "0192a000-0000-7000-8000-000000000099"
}
```

---

### 6.2 Endpoints

#### EP-01 `POST /api/v1/auth/login`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-01 |
| Descrição | Autentica um utilizador por e-mail e palavra-passe, emite um `access_token` inicial pré-contexto (`view_mode = 'NONE'`), um `refresh_token` inicial e devolve a lista de contextos disponíveis |
| Autenticação | Pública |
| Autorização | Nenhuma |
| Idempotência | Não (cada chamada bem-sucedida cria uma nova família de `RefreshToken` e um novo `jti`) |
| RN aplicáveis | RN-01, RN-02, RN-03, RN-04 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `email` | `string` | Sim | Formato e-mail RFC 5322, mín 5 e máx 320 caracteres; normalizado com `trim().ToLowerInvariant()` | Endereço de e-mail da pessoa física |
| `password` | `string` | Sim | Mín 12 e máx 128 caracteres; pelo menos 1 maiúscula, 1 minúscula, 1 dígito e 1 carácter especial (`RN-01`) | Palavra-passe em texto claro |

```json
{
  "email": "operador@agrobrasil.com.br",
  "password": "Agro@2026!SecurePassword"
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Autenticação concluída com sucesso (`available_scopes` lista os `tenant_scope` com `is_active == true` onde o membro possui ≥ 1 `Role` em `member_role`, ou todos os `tenant_scope` ativos se `is_owner == true`; `roles` lista os nomes das `Role` do membro nos escopos ativos, ou todas as `Role` do catálogo para os escopos ativos se `is_owner == true`, ordenadas alfabeticamente) | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado, campo obrigatório ausente (`null` ou omitido) ou campo desconhecido no pedido | Problem Details | `ERR-VAL-001` |
| 401 | E-mail inexistente ou `password` não corresponde ao `password_hash` Argon2id | Problem Details | `ERR-AUTH-001` |
| 403 | Conta com `status = 'SUSPENDED'` | Problem Details | `ERR-AUTH-004` |
| 422 | `email` com formato inválido ou `password` fora dos critérios da `RN-01` | Problem Details | `ERR-VAL-003` |
| 423 | Conta bloqueada temporariamente (`status = 'LOCKED'` e `now() < locked_until`, ou 5ª falha consecutiva atingida) | Problem Details | `ERR-AUTH-003` |

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6ImtleS0yMDI2LTAxIiwidHlwIjoiSldUIn0.eyJzdWIiOiIwMTkwYTZlMi0wMDAwLTcwMDAtODAwMC0wMDAwMDAwMDAwMDEiLCJ2aWV3X21vZGUiOiJOT05FIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c",
  "token_type": "Bearer",
  "expires_in": 900,
  "refresh_token": "dGhpcyBpcyBhIDMyIGJ5dGUgcmFuZG9tIHRva2VuMTI",
  "refresh_expires_in": 604800,
  "user": {
    "id": "0190a6e2-0000-7000-8000-000000000001",
    "email": "operador@agrobrasil.com.br",
    "name": "Carlos Operador",
    "platform_role": "NONE"
  },
  "available_contexts": [
    {
      "group_id": "0190a6e2-0000-7000-8000-000000000010",
      "group_name": "Holding AgroBrasil",
      "tenant_id": "0190a6e2-0000-7000-8000-000000000020",
      "cnpj": "11222333000181",
      "legal_name": "AgroBrasil Insumos Matriz Ltda",
      "branch_type": "HQ",
      "is_owner": true,
      "can_view_group": true,
      "available_scopes": ["BUYER", "SUPPLIER", "TENANT_ADMIN"],
      "roles": ["BUYER_OPERATOR", "GROUP_MANAGER", "SUPPLIER_SALES"]
    }
  ]
}
```

**Headers de resposta:** `Cache-Control: no-store`, `Pragma: no-cache`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. Antes de validar a palavra-passe: se `user_account.status == 'LOCKED'` e `now() >= locked_until`, executa `UPDATE user_account SET status = 'ACTIVE', failed_login_attempts = 0, locked_until = NULL, updated_at = now() WHERE id = @user_id`.
2. Em sucesso (200): `UPDATE user_account SET failed_login_attempts = 0, locked_until = NULL, status = 'ACTIVE', updated_at = now() WHERE id = @user_id`.
3. Em sucesso (200): `INSERT INTO refresh_token` com novo `family_id`, `token_hash` (SHA-256 do token gerado), `status = 'ACTIVE'`, `view_mode = 'NONE'`, `allowed_tenants = '[]'`, `expires_at = now() + 604800 s`.
4. Em sucesso (200): `INSERT INTO auth_audit_log` com `event_type = 'LOGIN_SUCCESS'`.
5. Em falha de palavra-passe com `failed_login_attempts + 1 < 5` ou e-mail inexistente (401 `ERR-AUTH-001`): `UPDATE user_account SET failed_login_attempts = failed_login_attempts + 1, updated_at = now()` (se o utilizador existir) e `INSERT INTO auth_audit_log` com `event_type = 'LOGIN_FAILED'`.
6. Em 5ª falha consecutiva de palavra-passe (`failed_login_attempts + 1 == 5` → 423 `ERR-AUTH-003`): `UPDATE user_account SET failed_login_attempts = 5, status = 'LOCKED', locked_until = now() + 900 s, updated_at = now()`, grava `denylist:user:{user_id} = "<now_unix>"` no Redis (`TTL = 900 s`), insere `USER_LOCKED` em `auth_audit_log` e insere `EV-03 usuario_bloqueado` em `outbox_message`. Se a conta já estava com `status == 'LOCKED'` e `now() < locked_until` (423 `ERR-AUTH-003`), insere `LOGIN_FAILED` em `auth_audit_log`.
7. Em tentativa de login de conta com `status == 'SUSPENDED'` (403 `ERR-AUTH-004`): `INSERT INTO auth_audit_log` com `event_type = 'LOGIN_FAILED'`.

**Transacionalidade:** Os passos de escrita na base de dados (`user_account`, `refresh_token`, `auth_audit_log`, `outbox_message`) ocorrem numa única transação ACID PostgreSQL.

---

#### EP-02 `POST /api/v1/auth/switch-context`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-02, RF-03 |
| Descrição | Alterna o contexto operacional da sessão ativa para o modo unitário (`SINGLE`) ou visão macro de grupo (`MACRO_GROUP`), revogando o JWT anterior na Denylist Redis e emitindo um novo `access_token` |
| Autenticação | JWT obrigatório |
| Autorização | Sessão autenticada sem claim `act` (tokens de impersonation não podem trocar de contexto) e vínculo ativo no(s) Tenant(s) alvo |
| Idempotência | Não (revoga o `jti` do token de entrada e emite um novo `jti`) |
| RN aplicáveis | RN-04, RN-05, RN-06, RN-07, RN-08, RN-09, RN-10, RN-18 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT ativo da sessão do utilizador |
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `view_mode` | `string` | Sim | Valores permitidos: `SINGLE`, `MACRO_GROUP` (`ENUM-06`, exceto `NONE`) | Modo de visão pretendido |
| `target_scope` | `string` | Sim | Valores permitidos: `BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN` (`ENUM-03`) | Escopo operacional pretendido |
| `target_tenant_id` | `string \| null` | Condicional | Obrigatório (UUID válido) quando `view_mode == 'SINGLE'`; **DEVE** ser `null` ou omitido quando `view_mode == 'MACRO_GROUP'` | Identificador do CNPJ alvo no modo unitário |
| `target_group_id` | `string \| null` | Condicional | Obrigatório (UUID válido) quando `view_mode == 'MACRO_GROUP'`; opcional/ignorado em `SINGLE` (derivado do `Tenant`) | Identificador do Grupo Económico no modo macro |
| `target_tenants` | `array<string> \| null` | Condicional | Obrigatório quando `view_mode == 'MACRO_GROUP'`: array de 1 a 50 UUIDs distintos; **DEVE** ser `null` ou omitido em `SINGLE` | Lista de CNPJs a agregar na visão macro/mapa |

```json
{
  "view_mode": "SINGLE",
  "target_scope": "SUPPLIER",
  "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020",
  "target_group_id": null,
  "target_tenants": null
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Contexto validado e novo `access_token` emitido | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado, `view_mode` inválido ou combinação incoerente de campos (`target_tenant_id` vs `target_tenants`) | Problem Details | `ERR-VAL-001` |
| 401 | Token JWT ausente, expirado, assinatura inválida ou `jti` presente em `denylist:jti:{jti}` | Problem Details | `ERR-AUTH-002` |
| 403 | Utilizador suspenso ou em `denylist:user:{user_id}` | Problem Details | `ERR-AUTH-004` |
| 403 | Utilizador não possui vínculo `tenant_member` com `status = 'ACTIVE'` no `target_tenant_id` | Problem Details | `ERR-CTX-001` |
| 403 | Tenant ou `EconomicGroup` com `status = 'SUSPENDED'` ou em `denylist:tenant:{id}` | Problem Details | `ERR-CTX-002` |
| 403 | `target_scope` não está ativo em `tenant_scope` ou o membro não detém papel nesse escopo (nem `is_owner = true`) | Problem Details | `ERR-CTX-003` |
| 403 | Violação das regras de `MACRO_GROUP` (`can_view_group == false` mesmo quando `is_owner == true`, tenant fora do `target_group_id`, duplicados ou mais de 50 tenants) | Problem Details | `ERR-CTX-004` |
| 403 | Tentativa de chamar `switch-context` usando um token de impersonation (claim `act` presente) | Problem Details | `ERR-CTX-007` |

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6ImtleS0yMDI2LTAxIiwidHlwIjoiSldUIn0.eyJzdWIiOiIwMTkwYTZlMi0wMDAwLTcwMDAtODAwMC0wMDAwMDAwMDAwMDEiLCJ2aWV3X21vZGUiOiJTSU5HTEUifQ.c2lnbmF0dXJlX3JzMjU2X3ZhbGlkX2Jhc2U2NHVybA",
  "token_type": "Bearer",
  "expires_in": 900,
  "context": {
    "sub": "0190a6e2-0000-7000-8000-000000000001",
    "group_id": "0190a6e2-0000-7000-8000-000000000010",
    "tenant_id": "0190a6e2-0000-7000-8000-000000000020",
    "allowed_tenants": [
      "0190a6e2-0000-7000-8000-000000000020"
    ],
    "view_mode": "SINGLE",
    "active_scope": "SUPPLIER",
    "roles": [
      "SUPPLIER_SALES"
    ],
    "permissions": [
      "estoque:atualizar",
      "fornecimento:publicar",
      "leilao:lance",
      "pedido:confirmar"
    ]
  }
}
```

**Headers de resposta:** `Cache-Control: no-store`, `Pragma: no-cache`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. `UPDATE refresh_token` no registo `ACTIVE` da família `fid` do token atual, atualizando `view_mode`, `group_id`, `active_tenant_id`, `allowed_tenants` e `active_scope`.
2. `INSERT INTO auth_audit_log` com `event_type = 'CONTEXT_SWITCHED'`, `user_id`, `tenant_id`, `group_id` e `metadata` contendo `view_mode` e `active_scope`.
3. Escrita no Redis: `SET denylist:jti:{old_jti} "1" EX <max(1, old_exp - now_unix)>`.

**Transacionalidade:** Os passos 1 e 2 ocorrem na mesma transação ACID PostgreSQL com `SET LOCAL app.auth_flow = 'true'`. O passo 3 (Redis) é executado imediatamente após o commit da BD.

---

#### EP-03 `POST /api/v1/auth/refresh`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-04 |
| Descrição | Rotaciona um `RefreshToken` ativo (`ACTIVE` → `CONSUMED`), emitindo um novo par (`access_token` + `refresh_token`) com o último contexto ativo da família, ou revoga toda a família em caso de deteção de reuso |
| Autenticação | Pública (baseada na posse do `refresh_token`) |
| Autorização | Validação do estado do `RefreshToken`, do `UserAccount` e do contexto associado |
| Idempotência | Não (a segunda chamada com o mesmo `refresh_token` aciona a regra de segurança de reuso `RN-11`) |
| RN aplicáveis | RN-04, RN-07, RN-11, RN-12 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `refresh_token` | `string` | Sim | Exatamente 43 caracteres `base64url` sem padding (`^[A-Za-z0-9_-]{43}$`) | Token de atualização opaco recebido no login ou no refresh anterior |

```json
{
  "refresh_token": "dGhpcyBpcyBhIDMyIGJ5dGUgcmFuZG9tIHRva2VuMTI"
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Rotação concluída com sucesso | Esquema de sucesso abaixo | Nenhum |
| 400 | `refresh_token` ausente, nulo, vazio ou fora do formato `^[A-Za-z0-9_-]{43}$` | Problem Details | `ERR-VAL-001` |
| 401 | Hash SHA-256 não encontrado, ou (`status == 'REVOKED'` / `expires_at <= now()` quando utilizador e contexto estão ativos) | Problem Details | `ERR-AUTH-005` |
| 401 | Deteção de reuso: token encontrado com `status == 'CONSUMED'` (revoga toda a família) | Problem Details | `ERR-AUTH-006` |
| 403 | Utilizador titular suspenso (`status == 'SUSPENDED'`) ou bloqueado na Denylist Redis (`denylist:user:{user_id}`) | Problem Details | `ERR-AUTH-004` |
| 403 | Tenant ou `EconomicGroup` do contexto ativo da sessão (`allowed_tenants` / `group_id`) foi suspenso ou consta em `denylist:tenant:{id}` | Problem Details | `ERR-CTX-002` |

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6ImtleS0yMDI2LTAxIiwidHlwIjoiSldUIn0.eyJzdWIiOiIwMTkwYTZlMi0wMDAwLTcwMDAtODAwMC0wMDAwMDAwMDAwMDEiLCJ2aWV3X21vZGUiOiJTSU5HTEUifQ.cmVmcmVzaGVkX3JzMjU2X3ZhbGlkX2Jhc2U2NHVybA",
  "token_type": "Bearer",
  "expires_in": 900,
  "refresh_token": "bmV3IDMyIGJ5dGUgcmFuZG9tIHRva2VuIGNoaWxkOTk",
  "refresh_expires_in": 604800
}
```

**Headers de resposta:** `Cache-Control: no-store`, `Pragma: no-cache`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. **Caminho normal (200):**
   1. `INSERT INTO refresh_token` (novo registo filho com `status = 'ACTIVE'`, mesma `family_id`, herdando `view_mode`, `group_id`, `active_tenant_id`, `allowed_tenants`, `active_scope`, e `expires_at = now() + 604800 s`).
   2. `UPDATE refresh_token SET status = 'CONSUMED', consumed_at = now(), replaced_by_id = @new_id WHERE id = @current_id`.
   3. `INSERT INTO auth_audit_log` com `event_type = 'TOKEN_REFRESHED'`.
2. **Caminho de deteção de reuso (`status == 'CONSUMED'` → 401 `ERR-AUTH-006`):**
   1. `UPDATE refresh_token SET status = 'REVOKED', revoked_at = COALESCE(revoked_at, now()) WHERE family_id = @family_id AND status IN ('ACTIVE', 'CONSUMED')`.
   2. `INSERT INTO auth_audit_log` com `event_type = 'TOKEN_REUSE_DETECTED'`.
   3. `INSERT INTO outbox_message` para o tópico `EV-04 sessao_comprometida`.
   4. Escrita no Redis: `SET denylist:user:{user_id} "<now_unix>" EX 900`.
3. **Caminho de titular ou contexto suspenso (403 `ERR-AUTH-004` ou 403 `ERR-CTX-002`):**
   1. `UPDATE refresh_token SET status = 'REVOKED', revoked_at = COALESCE(revoked_at, now()) WHERE family_id = @family_id AND status IN ('ACTIVE', 'CONSUMED')`.

**Transacionalidade:** Todos os comandos SQL de cada caminho são executados numa única transação ACID PostgreSQL antes do retorno HTTP.

---

#### EP-04 `POST /api/v1/auth/logout`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-05 |
| Descrição | Encerra a sessão ativa do utilizador, adicionando o `jti` do JWT atual à Denylist no Redis e revogando todos os `RefreshToken` da mesma `family_id` |
| Autenticação | JWT obrigatório |
| Autorização | Qualquer utilizador autenticado com token válido |
| Idempotência | Sim (se a família já estiver revogada, garante o `jti` na Denylist e responde 204) |
| RN aplicáveis | RN-05, RN-10 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT a revogar |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Pedido sem corpo (ou objeto JSON vazio `{}`) | Não se aplica |

```json
{}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 204 | Sessão encerrada e tokens revogados com sucesso | Sem corpo | Nenhum |
| 401 | Token JWT ausente, expirado, assinatura inválida ou `jti` já presente na Denylist Redis | Problem Details | `ERR-AUTH-002` |

```json
{}
```

**Headers de resposta:** `Cache-Control: no-store`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. Se o token contiver a claim `fid` (sessão normal): `UPDATE refresh_token SET status = 'REVOKED', revoked_at = COALESCE(revoked_at, now()) WHERE family_id = @fid AND status IN ('ACTIVE', 'CONSUMED')`.
2. `INSERT INTO auth_audit_log` com `event_type = 'LOGOUT'`, `user_id = @sub`, `actor_user_id = @act_sub`.
3. Escrita no Redis: `SET denylist:jti:{jti} "1" EX <max(1, exp - now_unix)>`.

**Transacionalidade:** Os passos 1 e 2 ocorrem na mesma transação ACID PostgreSQL; o passo 3 ocorre no Redis antes de devolver 204.

---

#### EP-05 `POST /api/v1/auth/impersonate`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-06 |
| Descrição | Emite um token de delegação RFC 8693 de 15 minutos (`view_mode = 'SINGLE'`, sem `RefreshToken`) para que um analista de suporte opere na visão de um cliente vinculando um código de chamado (`ticket_id`) |
| Autenticação | JWT obrigatório |
| Autorização | O chamador **DEVE** ter `platform_role IN ('SUPPORT_AGENT', 'PLATFORM_ADMIN')` e o token de entrada **NÃO DEVE** conter a claim `act` |
| Idempotência | Não (cada chamada gera um novo `jti` e um novo registo em `auth_audit_log`) |
| RN aplicáveis | RN-04, RN-05, RN-06, RN-07, RN-08, RN-13, RN-14 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT do analista de suporte |
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `target_user_id` | `string` | Sim | Formato `UUID` | Identificador do utilizador cliente a ser personificado |
| `target_tenant_id` | `string` | Sim | Formato `UUID` | Identificador do CNPJ onde a sessão personificada irá atuar |
| `target_scope` | `string` | Sim | Valores permitidos: `BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN` (`ENUM-03`) | Escopo operacional alvo |
| `ticket_id` | `string` | Sim | Regex `^[A-Z]{2,10}-[0-9]{1,10}$` (ex.: `SUP-4821`) | Código do chamado técnico de suporte |

```json
{
  "target_user_id": "0190a6e2-0000-7000-8000-000000000001",
  "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020",
  "target_scope": "BUYER",
  "ticket_id": "SUP-4821"
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Token de delegação emitido com sucesso | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado ou campo obrigatório ausente | Problem Details | `ERR-VAL-001` |
| 401 | Token JWT ausente, inválido, expirado ou na Denylist Redis | Problem Details | `ERR-AUTH-002` |
| 403 | Chamador não possui `platform_role IN ('SUPPORT_AGENT', 'PLATFORM_ADMIN')`, ou token já possui claim `act`, ou `target_user_id` possui `platform_role != 'NONE'`, ou `target_user_id == sub` | Problem Details | `ERR-AUTH-010` |
| 403 | Utilizador alvo suspenso (`status == 'SUSPENDED'`), bloqueado (`status == 'LOCKED'` com `now() < locked_until`) ou em `denylist:user:{target_user_id}` | Problem Details | `ERR-AUTH-004` |
| 403 | Utilizador alvo não é membro ativo de `target_tenant_id` | Problem Details | `ERR-CTX-001` |
| 403 | `target_tenant_id` ou respetivo grupo está suspenso ou consta em `denylist:tenant:{target_tenant_id}` | Problem Details | `ERR-CTX-002` |
| 403 | `target_scope` inativo no Tenant ou utilizador alvo sem papel nesse escopo | Problem Details | `ERR-CTX-003` |
| 404 | `target_user_id` ou `target_tenant_id` não encontrado na base de dados | Problem Details | `ERR-CTX-008` |
| 422 | `ticket_id` não cumpre a expressão regular `^[A-Z]{2,10}-[0-9]{1,10}$` | Problem Details | `ERR-VAL-004` |

```json
{
  "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6ImtleS0yMDI2LTAxIiwidHlwIjoiSldUIn0.eyJzdWIiOiIwMTkwYTZlMi0wMDAwLTcwMDAtODAwMC0wMDAwMDAwMDAwMDEiLCJ2aWV3X21vZGUiOiJTSU5HTEUifQ.aW1wZXJzb25hdGVfcnMyNTZfdmFsaWRfYmFzZTY0dXJs",
  "token_type": "Bearer",
  "expires_in": 900,
  "impersonation_context": {
    "sub": "0190a6e2-0000-7000-8000-000000000001",
    "group_id": "0190a6e2-0000-7000-8000-000000000010",
    "tenant_id": "0190a6e2-0000-7000-8000-000000000020",
    "allowed_tenants": [
      "0190a6e2-0000-7000-8000-000000000020"
    ],
    "view_mode": "SINGLE",
    "active_scope": "BUYER",
    "act": {
      "sub": "0190a6e2-0000-7000-8000-000000000090",
      "ticket_id": "SUP-4821"
    }
  }
}
```

**Headers de resposta:** `Cache-Control: no-store`, `Pragma: no-cache`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. `INSERT INTO auth_audit_log` com `event_type = 'IMPERSONATION_STARTED'`, `user_id = @target_user_id`, `actor_user_id = @caller_sub`, `tenant_id = @target_tenant_id`, `group_id = @tenant_group_id`, `ticket_id = @ticket_id`.

**Transacionalidade:** Escrita única em `auth_audit_log` em transação ACID antes de devolver o token.

---

#### EP-06 `POST /api/v1/tenants`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-07 |
| Descrição | Cadastra um novo `Tenant` (CNPJ) vinculado a um `EconomicGroup`, ativa os escopos operacionais informados e agenda a publicação do evento `EV-01 empresa_cadastrada` via Outbox |
| Autenticação | JWT obrigatório |
| Autorização | Utilizador com `platform_role == 'PLATFORM_ADMIN'` **OU** utilizador com `active_scope == 'TENANT_ADMIN'`, permissão `tenant:criar` e `group_id == request.group_id` |
| Idempotência | Não (chamada repetida com o mesmo CNPJ retorna `409 ERR-CTX-005`) |
| RN aplicáveis | RN-05, RN-07, RN-15, RN-16, RN-18 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT com permissão para criar Tenant no grupo |
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `group_id` | `string` | Sim | Formato `UUID`; deve existir em `economic_group` com `status = 'ACTIVE'` | Grupo económico ao qual o CNPJ pertence |
| `cnpj` | `string` | Sim | Exatamente 14 dígitos numéricos (`^[0-9]{14}$`) e dígitos verificadores módulo 11 válidos (`RN-15`) | CNPJ sem pontuação |
| `legal_name` | `string` | Sim | Entre 2 e 200 caracteres após `trim()` | Razão social da empresa |
| `branch_type` | `string` | Sim | `HQ` ou `BRANCH` (`ENUM-02`) | Matriz ou Filial |
| `geo_lat` | `number` | Sim | Decimal entre `-90.0` e `90.0` | Latitude geográfica |
| `geo_lng` | `number` | Sim | Decimal entre `-180.0` e `180.0` | Longitude geográfica |
| `scopes` | `array<string>` | Sim | Array não vazio (1 a 4 itens distintos) com valores de `ENUM-03` (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) | Escopos operacionais a habilitar no CNPJ |

```json
{
  "group_id": "0190a6e2-0000-7000-8000-000000000010",
  "cnpj": "11444777000161",
  "legal_name": "AgroBrasil Filial Cerrado Ltda",
  "branch_type": "BRANCH",
  "geo_lat": -16.686891,
  "geo_lng": -49.264794,
  "scopes": ["BUYER", "TENANT_ADMIN"]
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 201 | Tenant, escopos e mensagem Outbox criados com sucesso | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado, campo ausente ou escopo desconhecido | Problem Details | `ERR-VAL-001` |
| 401 | Token JWT ausente, inválido, expirado ou na Denylist Redis | Problem Details | `ERR-AUTH-002` |
| 403 | Chamador sem `platform_role == 'PLATFORM_ADMIN'` e sem (`active_scope == 'TENANT_ADMIN'` com permissão `tenant:criar` no mesmo `group_id`) | Problem Details | `ERR-CTX-007` |
| 403 | `economic_group` (`group_id`) encontra-se com `status = 'SUSPENDED'` | Problem Details | `ERR-CTX-002` |
| 404 | `group_id` não encontrado em `economic_group` | Problem Details | `ERR-CTX-008` |
| 409 | Já existe um `Tenant` com o mesmo `cnpj` | Problem Details | `ERR-CTX-005` |
| 422 | `cnpj` com dígitos verificadores inválidos ou coordenadas fora dos limites (`RN-15`) | Problem Details | `ERR-VAL-002` |

```json
{
  "id": "0190a6e2-0000-7000-8000-000000000025",
  "group_id": "0190a6e2-0000-7000-8000-000000000010",
  "cnpj": "11444777000161",
  "legal_name": "AgroBrasil Filial Cerrado Ltda",
  "branch_type": "BRANCH",
  "geo_lat": -16.686891,
  "geo_lng": -49.264794,
  "status": "ACTIVE",
  "scopes": ["BUYER", "TENANT_ADMIN"],
  "created_at": "2026-10-18T19:45:00Z"
}
```

**Headers de resposta:** `Location: /api/v1/tenants/0190a6e2-0000-7000-8000-000000000025`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. `INSERT INTO tenant` com `status = 'ACTIVE'`.
2. `INSERT INTO tenant_scope` para cada elemento de `scopes` com `is_active = true`.
3. `INSERT INTO auth_audit_log` com `event_type = 'TENANT_CREATED'`.
4. `INSERT INTO outbox_message` com `topic = 'empresa_cadastrada'` (`EV-01`), `partition_key = tenant.id`, cabeçalhos assinados com `context_jws` e payload do Tenant criado.

**Transacionalidade:** Os passos 1 a 4 ocorrem na mesma transação ACID PostgreSQL.

---

#### EP-07 `POST /api/v1/tenants/{tenant_id}/members`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-08 |
| Descrição | Vincula um `UserAccount` existente a um `Tenant` (`TenantMember`) e associa os respetivos papéis (`MemberRole`) |
| Autenticação | JWT obrigatório |
| Autorização | `active_scope == 'TENANT_ADMIN'` com `tenant_id` contido em `AuthContext.AllowedTenants` e permissão `membro:vincular` (ou `platform_role == 'PLATFORM_ADMIN'`) |
| Idempotência | Não (tentativa de vincular o mesmo `user_id` ao mesmo `tenant_id` retorna `409 ERR-CTX-005`) |
| RN aplicáveis | RN-05, RN-16, RN-17, RN-18 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT com escopo `TENANT_ADMIN` autorizado no `tenant_id` |
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| `tenant_id` | `path` | `UUID` | Sim | Formato UUID válido | Nenhum |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `user_id` | `string` | Sim | Formato `UUID` existente em `user_account` | Identificador da pessoa física a vincular |
| `is_owner` | `boolean` | Sim | `true` ou `false` | Define se o membro é proprietário do Tenant |
| `can_view_group` | `boolean` | Sim | `true` ou `false` | Autoriza a inclusão deste Tenant em operações `MACRO_GROUP` |
| `role_ids` | `array<string>` | Sim | Array de 1 a 10 UUIDs distintos existentes na tabela `role` | Lista de papéis atribuídos ao membro neste Tenant |

```json
{
  "user_id": "0190a6e2-0000-7000-8000-000000000002",
  "is_owner": false,
  "can_view_group": true,
  "role_ids": [
    "01900000-0000-7000-8000-000000000001"
  ]
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 201 | Membro e papéis vinculados com sucesso | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado ou `role_ids` vazio | Problem Details | `ERR-VAL-001` |
| 401 | Token JWT ausente, inválido, expirado ou na Denylist Redis | Problem Details | `ERR-AUTH-002` |
| 403 | Chamador não está em `active_scope == 'TENANT_ADMIN'` ou `tenant_id` não pertence a `AllowedTenants` (`RN-18`) | Problem Details | `ERR-CTX-007` |
| 404 | `tenant_id`, `user_id` ou algum `role_id` não encontrado | Problem Details | `ERR-CTX-008` |
| 409 | O `user_id` já possui registo em `tenant_member` para este `tenant_id` (`RN-16`) | Problem Details | `ERR-CTX-005` |
| 422 | Algum `role_id` pertence a um `scope` que não está ativo em `tenant_scope` para este `tenant_id` (`RN-17`) | Problem Details | `ERR-CTX-006` |

```json
{
  "member_id": "0190a6e2-0000-7000-8000-000000000050",
  "tenant_id": "0190a6e2-0000-7000-8000-000000000020",
  "user_id": "0190a6e2-0000-7000-8000-000000000002",
  "is_owner": false,
  "can_view_group": true,
  "status": "ACTIVE",
  "roles": ["BUYER_OPERATOR"],
  "created_at": "2026-10-18T19:50:00Z"
}
```

**Headers de resposta:** `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. `INSERT INTO tenant_member` com `status = 'ACTIVE'`.
2. `INSERT INTO member_role` para cada `role_id` informado, preenchendo `tenant_id`.
3. `INSERT INTO auth_audit_log` com `event_type = 'MEMBER_ADDED'`.

**Transacionalidade:** Os passos 1 a 3 ocorrem na mesma transação ACID PostgreSQL sob validação do `TenantSaveChangesInterceptor` e PostgreSQL RLS (`app.allowed_tenants`).

---

#### EP-08 `PATCH /api/v1/tenants/{tenant_id}/status`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-09 |
| Descrição | Suspende ou reativa um `Tenant` (`status = 'SUSPENDED'` \| `'ACTIVE'`), atualizando imediatamente a Denylist Redis (`denylist:tenant:{tenant_id}`) e publicando `EV-02 tenant_bloqueado` quando suspenso |
| Autenticação | JWT obrigatório |
| Autorização | `platform_role == 'PLATFORM_ADMIN'` **OU** (`active_scope == 'TENANT_ADMIN'` com `tenant_id` em `AllowedTenants` e `is_owner == true`) |
| Idempotência | Sim (atualiza o estado, o Redis e responde 200) |
| RN aplicáveis | RN-05, RN-07, RN-18 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT autorizado |
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| `tenant_id` | `path` | `UUID` | Sim | Formato UUID válido | Nenhum |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `status` | `string` | Sim | `ACTIVE` ou `SUSPENDED` (`ENUM-01`) | Novo estado do Tenant |
| `reason` | `string` | Sim | Entre 5 e 255 caracteres após `trim()` | Motivo da alteração de estado para auditoria |

```json
{
  "status": "SUSPENDED",
  "reason": "Inadimplência contratual verificada pelo financeiro"
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Estado do Tenant atualizado com sucesso | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado, `status` fora de `ENUM-01` ou `reason` fora dos limites | Problem Details | `ERR-VAL-001` |
| 401 | Token JWT ausente, inválido, expirado ou na Denylist Redis | Problem Details | `ERR-AUTH-002` |
| 403 | Chamador sem permissão de administração sobre o `tenant_id` | Problem Details | `ERR-CTX-007` |
| 404 | `tenant_id` não encontrado | Problem Details | `ERR-CTX-008` |

```json
{
  "tenant_id": "0190a6e2-0000-7000-8000-000000000020",
  "status": "SUSPENDED",
  "updated_at": "2026-10-18T19:55:00Z"
}
```

**Headers de resposta:** `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. `UPDATE tenant SET status = @status, updated_at = now() WHERE id = @tenant_id`.
2. Se `@status == 'SUSPENDED'`: `UPDATE refresh_token SET status = 'REVOKED', revoked_at = COALESCE(revoked_at, now()) WHERE status IN ('ACTIVE', 'CONSUMED') AND (active_tenant_id = @tenant_id OR allowed_tenants @> to_jsonb(@tenant_id::text))`.
3. `INSERT INTO auth_audit_log` com `event_type = 'TENANT_STATUS_CHANGED'` e `metadata = {"status": @status, "reason": @reason}`.
4. Se `@status == 'SUSPENDED'`: `INSERT INTO outbox_message` com `topic = 'tenant_bloqueado'` (`EV-02`).
5. No Redis: se `@status == 'SUSPENDED'`, executa `SET denylist:tenant:{tenant_id} "1" EX 86400`; se `@status == 'ACTIVE'`, executa `DEL denylist:tenant:{tenant_id}`.

**Transacionalidade:** Os passos 1 a 4 ocorrem na mesma transação ACID PostgreSQL; o passo 5 ocorre no Redis imediatamente após o commit.

---

#### EP-09 `PATCH /api/v1/users/{user_id}/status`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-10 |
| Descrição | Suspende ou reativa uma conta de utilizador (`status = 'SUSPENDED'` \| `'ACTIVE'`), revogando imediatamente todas as famílias de `RefreshToken`, gravando `denylist:user:{user_id}` no Redis e publicando `EV-03 usuario_bloqueado` quando suspenso |
| Autenticação | JWT obrigatório |
| Autorização | `platform_role == 'PLATFORM_ADMIN'` **OU** (`active_scope == 'TENANT_ADMIN'` com permissão `usuario:suspender` e o `user_id` alvo sendo membro de um Tenant pertencente ao mesmo `group_id` do chamador) |
| Idempotência | Sim (atualiza o estado, o Redis e responde 200) |
| RN aplicáveis | RN-04, RN-05, RN-18 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `Authorization` | Sim | `Bearer <access_token>` | Token JWT autorizado |
| `Content-Type` | Sim | `application/json` | Tipo de média do corpo do pedido |
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| `user_id` | `path` | `UUID` | Sim | Formato UUID válido | Nenhum |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| `status` | `string` | Sim | `ACTIVE` ou `SUSPENDED` | Novo estado administrativo do utilizador |
| `reason` | `string` | Sim | Entre 5 e 255 caracteres após `trim()` | Motivo da alteração de estado |

```json
{
  "status": "SUSPENDED",
  "reason": "Desligamento do colaborador da empresa"
}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Estado do utilizador atualizado com sucesso | Esquema de sucesso abaixo | Nenhum |
| 400 | JSON malformado, `status` inválido ou `reason` fora dos limites | Problem Details | `ERR-VAL-001` |
| 401 | Token JWT ausente, inválido, expirado ou na Denylist Redis | Problem Details | `ERR-AUTH-002` |
| 403 | Chamador não possui autorização sobre o `user_id` alvo (ou tentou suspender um `PLATFORM_ADMIN` sem ser `PLATFORM_ADMIN`) | Problem Details | `ERR-CTX-007` |
| 404 | `user_id` não encontrado | Problem Details | `ERR-CTX-008` |

```json
{
  "user_id": "0190a6e2-0000-7000-8000-000000000001",
  "status": "SUSPENDED",
  "updated_at": "2026-10-18T19:58:00Z"
}
```

**Headers de resposta:** `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. `UPDATE user_account SET status = @status, failed_login_attempts = 0, locked_until = NULL, updated_at = now() WHERE id = @user_id`.
2. Se `@status == 'SUSPENDED'`: `UPDATE refresh_token SET status = 'REVOKED', revoked_at = COALESCE(revoked_at, now()) WHERE user_id = @user_id AND status IN ('ACTIVE', 'CONSUMED')`.
3. `INSERT INTO auth_audit_log` com `event_type = 'USER_STATUS_CHANGED'`, `user_id = @user_id`, `actor_user_id = @caller_sub` e `metadata = {"status": @status, "reason": @reason}`.
4. Se `@status == 'SUSPENDED'`: `INSERT INTO outbox_message` com `topic = 'usuario_bloqueado'` (`EV-03`).
5. No Redis: se `@status == 'SUSPENDED'`, executa `SET denylist:user:{user_id} "<now_unix>" EX 900`; se `@status == 'ACTIVE'`, executa `DEL denylist:user:{user_id}`.

**Transacionalidade:** Os passos 1 a 4 ocorrem na mesma transação ACID PostgreSQL; o passo 5 ocorre no Redis imediatamente após o commit.

---

#### EP-10 `GET /.well-known/jwks.json`
| Campo | Valor |
|---|---|
| RF associado(s) | RF-11 |
| Descrição | Expõe o conjunto de chaves públicas RSA em formato JSON Web Key Set (JWKS, RFC 7517) para validação stateless de JWT e `context_jws` pelo API Gateway e microsserviços |
| Autenticação | Pública |
| Autorização | Nenhuma |
| Idempotência | Sim (leitura pura sem efeitos colaterais) |
| RN aplicáveis | RN-05 |

**Headers de request**
| Header | Obrigatório | Formato | Descrição |
|---|---|---|---|
| `X-Correlation-Id` | Não | `UUID` | Identificador de rastreio da requisição |

**Parâmetros de path / query**
| Nome | Local | Tipo | Obrigatório | Validação | Default |
|---|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

**Corpo do request**
| Campo | Tipo JSON | Obrigatório | Validação (formato, mín/máx, enum) | Descrição |
|---|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | Pedido GET sem corpo | Não se aplica |

```json
{}
```

**Respostas**
| Status | Condição | Corpo | Código de erro |
|---|---|---|---|
| 200 | Chaves públicas retornadas com sucesso | Esquema JWKS abaixo | Nenhum |
| 500 | Falha interna ao ler ou exportar o material criptográfico RSA | Problem Details | `ERR-SYS-001` |

```json
{
  "keys": [
    {
      "kty": "RSA",
      "use": "sig",
      "alg": "RS256",
      "kid": "key-2026-01",
      "n": "0vx7agoebGcQSuuPiLJXZptN9nndrQmbXEps2aiAFbWhM78LhWx4cbbfAAtVT86zwu1RK7aPFFxuhDR1L6tSoc_BJECPebWKRXjBZCiFV4n3oknjhMstn64tZ_2W-5JsGY4Hc5n9yBXArwl93lqt7_RN5w6Cf0h4QyQ5v-65YGjQR0_FDW2QvzqY368QQMicAtaSqzs8KJZgnYb9c7d0zgdAZHzu6qMQvRL5hajrn1n91CbOpbISD08qNLyrdkt-bFTWhAI4vMQFh6WeZu0fM4lFd2NcRwr3XPksINHaQ-G_xBniIqbw0Ls1jF44-csFCur-kEgU8awapJzKnqDKgw",
      "e": "AQAB"
    }
  ]
}
```

**Headers de resposta:** `Cache-Control: public, max-age=300`, `X-Correlation-Id: <uuid>`

**Efeitos colaterais (por ordem de execução):**
1. Nenhum (operação apenas de leitura).

**Transacionalidade:** Não se aplica (endpoint apenas de leitura).

---

### 6.3 Eventos Assíncronos

> **Estrutura Obrigatória de Cabeçalhos Kafka e `context_jws` (ADR-09 / `PRS-09`):** Todos os eventos `EV-01` a `EV-04` publicados pelo `Identity-Service` **DEVEM** conter os cabeçalhos UTF-8 `event_id` (UUID da `outbox_message`), `tenant_id`, `group_id`, `initiator_user_id`, `actor_user_id`, `acting_scope`, `correlation_id` e `context_jws` (JWS compacto `RS256` assinado com a chave privada ativa do serviço, contendo exatamente as claims `{ "event_id", "tenant_id", "group_id", "initiator_user_id", "actor_user_id", "acting_scope", "view_mode", "allowed_tenants", "correlation_id", "iat", "exp" }` com `exp = iat + 300`). Os valores de cada campo por gatilho seguem deterministicamente a tabela abaixo:

| Evento / Gatilho | `tenant_id` (Header / JWS) | `group_id` (Header / JWS) | `initiator_user_id` | `actor_user_id` | `acting_scope` | `view_mode` (JWS) | `allowed_tenants` (JWS) |
|---|---|---|---|---|---|---|
| `EV-01` em `EP-06` (criado por `TENANT_ADMIN` ou `PLATFORM_ADMIN`) | `<new_tenant.id>` / `"<new_tenant.id>"` | `<new_tenant.group_id>` / `"<new_tenant.group_id>"` | `caller.sub` | `caller.act.sub ?? ""` (JWS: `null` se vazio) | `caller.active_scope ?? "CONTROL_PLANE"` | `caller.view_mode` | `caller.allowed_tenants` |
| `EV-02` em `EP-08` (suspensão de Tenant) | `<target_tenant.id>` / `"<target_tenant.id>"` | `<target_tenant.group_id>` / `"<target_tenant.group_id>"` | `caller.sub` | `caller.act.sub ?? ""` (JWS: `null` se vazio) | `caller.active_scope ?? "CONTROL_PLANE"` | `caller.view_mode` | `caller.allowed_tenants` |
| `EV-03` em `EP-01` (5ª falha de login → `LOCKED`) | `""` / `null` | `""` / `null` | `<locked_user.id>` | `""` / `null` | `"CONTROL_PLANE"` | `"NONE"` | `[]` |
| `EV-03` em `EP-09` (suspensão administrativa → `SUSPENDED`) | `caller.tenant_id ?? ""` / `caller.tenant_id` | `caller.group_id ?? ""` / `caller.group_id` | `<target_user.id>` | `<caller.sub>` | `caller.active_scope ?? "CONTROL_PLANE"` | `caller.view_mode` | `caller.allowed_tenants` |
| `EV-04` em `EP-03` (deteção de reuso de `refresh_token`) | `token.active_tenant_id ?? ""` / `token.active_tenant_id` | `token.group_id ?? ""` / `token.group_id` | `<token.user_id>` | `""` / `null` | `token.active_scope ?? "CONTROL_PLANE"` | `token.view_mode` | `token.allowed_tenants` |

#### EV-01 `empresa_cadastrada`
| Campo | Valor |
|---|---|
| Direção | Publicado |
| Produtor / Consumidor(es) | Produtor: `Identity-Service` · Consumidores: `Produtos`, `Fornecimentos`, `Demanda`, `Mercado (Matching Engine)`, `Negociação/Leilão`, `Pedidos`, `Logística/Frete` |
| Gatilho | RF-07 (`EP-06` criação de novo `Tenant`) |
| Chave de partição | `tenant_id` |
| Semântica de entrega | *At-least-once* via tabela `outbox_message` (`ENT-10`) |
| Tratamento de duplicados | Consumidores deduplicam pelo header `event_id` (`outbox_message.id`) ou por `tenant_id` |

**Headers obrigatórios:** `event_id` (UUID), `tenant_id` (UUID), `group_id` (UUID), `initiator_user_id` (UUID), `actor_user_id` (UUID), `acting_scope` (string), `correlation_id` (UUID), `context_jws` (string JWS RS256)

**Payload**
| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `event_id` | `string (UUID)` | Sim | Identificador único do evento (`outbox_message.id`) |
| `tenant_id` | `string (UUID)` | Sim | Identificador do Tenant criado |
| `group_id` | `string (UUID)` | Sim | Identificador do Grupo Económico ao qual o Tenant pertence |
| `cnpj` | `string` | Sim | CNPJ de 14 dígitos numéricos |
| `legal_name` | `string` | Sim | Razão social da empresa |
| `branch_type` | `string` | Sim | `HQ` ou `BRANCH` |
| `geo_lat` | `number` | Sim | Latitude geográfica |
| `geo_lng` | `number` | Sim | Longitude geográfica |
| `status` | `string` | Sim | `ACTIVE` |
| `scopes` | `array<string>` | Sim | Escopos habilitados para o Tenant |
| `created_at` | `string (ISO-8601)` | Sim | Data e hora UTC do cadastro |

```json
{
  "event_id": "0190a6e2-0000-7000-8000-000000000101",
  "tenant_id": "0190a6e2-0000-7000-8000-000000000025",
  "group_id": "0190a6e2-0000-7000-8000-000000000010",
  "cnpj": "11444777000161",
  "legal_name": "AgroBrasil Filial Cerrado Ltda",
  "branch_type": "BRANCH",
  "geo_lat": -16.686891,
  "geo_lng": -49.264794,
  "status": "ACTIVE",
  "scopes": ["BUYER", "TENANT_ADMIN"],
  "created_at": "2026-10-18T19:45:00Z"
}
```

#### EV-02 `tenant_bloqueado`
| Campo | Valor |
|---|---|
| Direção | Publicado |
| Produtor / Consumidor(es) | Produtor: `Identity-Service` · Consumidores: `Mercado (Matching Engine)`, `Negociação/Leilão`, `Fornecimentos`, `Demanda`, `Logística/Frete` |
| Gatilho | RF-09 (`EP-08` quando `status` muda para `'SUSPENDED'`) |
| Chave de partição | `tenant_id` |
| Semântica de entrega | *At-least-once* via tabela `outbox_message` (`ENT-10`) |
| Tratamento de duplicados | Chave de idempotência `event_id` |

**Headers obrigatórios:** `event_id` (UUID), `tenant_id` (UUID), `group_id` (UUID), `initiator_user_id` (UUID), `actor_user_id` (UUID), `acting_scope` (string), `correlation_id` (UUID), `context_jws` (string JWS RS256)

**Payload**
| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `event_id` | `string (UUID)` | Sim | Identificador único do evento |
| `tenant_id` | `string (UUID)` | Sim | Identificador do Tenant suspenso |
| `group_id` | `string (UUID)` | Sim | Identificador do Grupo Económico |
| `cnpj` | `string` | Sim | CNPJ de 14 dígitos |
| `status` | `string` | Sim | Valor fixo `"SUSPENDED"` |
| `reason` | `string` | Sim | Motivo registado para a suspensão |
| `blocked_by_user_id` | `string (UUID)` | Sim | Identificador do operador que executou o bloqueio |
| `blocked_at` | `string (ISO-8601)` | Sim | Instante UTC do bloqueio |

```json
{
  "event_id": "0190a6e2-0000-7000-8000-000000000102",
  "tenant_id": "0190a6e2-0000-7000-8000-000000000020",
  "group_id": "0190a6e2-0000-7000-8000-000000000010",
  "cnpj": "11222333000181",
  "status": "SUSPENDED",
  "reason": "Inadimplência contratual verificada pelo financeiro",
  "blocked_by_user_id": "0190a6e2-0000-7000-8000-000000000090",
  "blocked_at": "2026-10-18T19:55:00Z"
}
```

#### EV-03 `usuario_bloqueado`
| Campo | Valor |
|---|---|
| Direção | Publicado |
| Produtor / Consumidor(es) | Produtor: `Identity-Service` · Consumidores: `Negociação/Leilão`, `Pedidos`, Sistemas de Auditoria e Segurança |
| Gatilho | RF-01 / RN-03 (5 falhas consecutivas de login → `LOCKED`) ou RF-10 (`EP-09` → `SUSPENDED`) |
| Chave de partição | `user_id` |
| Semântica de entrega | *At-least-once* via tabela `outbox_message` (`ENT-10`) |
| Tratamento de duplicados | Chave de idempotência `event_id` |

**Headers obrigatórios:** `event_id` (UUID), `tenant_id` (string), `group_id` (string), `initiator_user_id` (UUID), `actor_user_id` (string), `acting_scope` (string), `correlation_id` (UUID), `context_jws` (string JWS RS256)

**Payload**
| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `event_id` | `string (UUID)` | Sim | Identificador único do evento |
| `user_id` | `string (UUID)` | Sim | Identificador do utilizador bloqueado |
| `status` | `string` | Sim | `LOCKED` ou `SUSPENDED` |
| `locked_until` | `string (ISO-8601) \| null` | Sim | Timestamp UTC de fim do bloqueio (preenchido se `LOCKED`, `null` se `SUSPENDED`) |
| `reason` | `string` | Sim | Motivo do bloqueio |
| `blocked_at` | `string (ISO-8601)` | Sim | Instante UTC do bloqueio |

```json
{
  "event_id": "0190a6e2-0000-7000-8000-000000000103",
  "user_id": "0190a6e2-0000-7000-8000-000000000001",
  "status": "LOCKED",
  "locked_until": "2026-10-18T20:15:00Z",
  "reason": "Excesso de tentativas consecutivas de login com palavra-passe incorreta",
  "blocked_at": "2026-10-18T20:00:00Z"
}
```

#### EV-04 `sessao_comprometida`
| Campo | Valor |
|---|---|
| Direção | Publicado |
| Produtor / Consumidor(es) | Produtor: `Identity-Service` · Consumidores: SIEM / Monitorização de Segurança e Serviços de Notificação |
| Gatilho | RF-04 / RN-11 (apresentação de `RefreshToken` já marcado como `CONSUMED` em `EP-03`) |
| Chave de partição | `user_id` |
| Semântica de entrega | *At-least-once* via tabela `outbox_message` (`ENT-10`) |
| Tratamento de duplicados | Chave de idempotência `event_id` |

**Headers obrigatórios:** `event_id` (UUID), `tenant_id` (string), `group_id` (string), `initiator_user_id` (UUID), `actor_user_id` (string), `acting_scope` (string), `correlation_id` (UUID), `context_jws` (string JWS RS256)

**Payload**
| Campo | Tipo | Obrigatório | Descrição |
|---|---|---|---|
| `event_id` | `string (UUID)` | Sim | Identificador único do evento |
| `user_id` | `string (UUID)` | Sim | Utilizador titular da família de tokens comprometida |
| `family_id` | `string (UUID)` | Sim | Identificador da família de `RefreshToken` revogada |
| `reused_refresh_token_id` | `string (UUID)` | Sim | Identificador do registo `refresh_token` `CONSUMED` que foi reapresentado |
| `ip_address` | `string` | Sim | Endereço IP da requisição que reapresentou o token |
| `detected_at` | `string (ISO-8601)` | Sim | Instante UTC da deteção |

```json
{
  "event_id": "0190a6e2-0000-7000-8000-000000000104",
  "user_id": "0190a6e2-0000-7000-8000-000000000001",
  "family_id": "0190a6e2-0000-7000-8000-000000000077",
  "reused_refresh_token_id": "0190a6e2-0000-7000-8000-000000000081",
  "ip_address": "192.0.2.44",
  "detected_at": "2026-10-18T20:05:00Z"
}
```

---

## 7. Requisitos Funcionais

### RF-01 — Autenticar utilizador e descobrir contextos
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Operador B2B / Agente de Suporte / Administrador da Plataforma (`UserAccount`) |
| Gatilho | EP-01 (`POST /api/v1/auth/login`) |
| Pré-condições | Base de dados PostgreSQL e Redis operacionais; registo do utilizador existente em `user_account` (`ENT-04`) |
| RN aplicáveis | RN-01, RN-02, RN-03, RN-04 |

**Fluxo principal**
1. O sistema recebe o pedido em `EP-01` com `email` e `password` e valida a estrutura JSON e o formato conforme `RN-01`.
2. O sistema normaliza `email` (`trim().ToLowerInvariant()`) e consulta `user_account` sob `SET LOCAL app.auth_flow = 'true'`.
3. O sistema verifica o estado da conta conforme `RN-04` (`status != 'SUSPENDED'`) e `RN-03`: se `status == 'LOCKED'` e `now() >= locked_until`, o sistema repõe imediatamente `status = 'ACTIVE'`, `failed_login_attempts = 0` e `locked_until = NULL` antes de validar a palavra-passe.
4. O sistema valida `password` contra `user_account.password_hash` utilizando Argon2id (`RNF-01`) conforme `RN-02`.
5. O sistema atualiza `user_account`: `failed_login_attempts = 0`, `locked_until = NULL`, `status = 'ACTIVE'`, `updated_at = now()`.
6. O sistema consulta todos os vínculos `tenant_member` com `status = 'ACTIVE'` do utilizador cujo `tenant.status == 'ACTIVE'` e `economic_group.status == 'ACTIVE'`, carregando para cada `Tenant` os `tenant_scope` com `is_active == true` para os quais o membro possui pelo menos 1 `Role` atribuída em `member_role` (ou todos os `tenant_scope` com `is_active == true` se `tenant_member.is_owner == true`), bem como a lista de nomes de `Role` do membro nesses escopos ativos (ou todas as `Role` do catálogo §5.5 para os escopos ativos se `is_owner == true`), ordenadas alfabeticamente.
7. O sistema gera um `RefreshToken` opaco de 32 bytes aleatórios codificado em 43 caracteres `base64url`, calcula o seu hash SHA-256 (`token_hash`) e insere um registo em `refresh_token` (`ENT-08`) com nova `family_id`, `status = 'ACTIVE'`, `view_mode = 'NONE'`, `allowed_tenants = '[]'`, `active_scope = NULL` e `expires_at = now() + 604800 s`.
8. O sistema emite um `access_token` JWT (`RS256`, `exp = iat + 900 s`) contendo `sub = user.id`, `fid = family_id`, `platform_role = user.platform_role`, `view_mode = 'NONE'`, `group_id = null`, `tenant_id = null`, `allowed_tenants = []`, `active_scope = null`, `roles = []` e `permissions = []`.
9. O sistema insere um registo em `auth_audit_log` (`ENT-09`) com `event_type = 'LOGIN_SUCCESS'` e responde `200 OK` com o corpo definido em `EP-01`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-01.1 | 1 | Corpo JSON malformado, campo obrigatório ausente (`null` ou omitido) ou campo extra não especificado | `400 ERR-VAL-001` | Nada persistido na base de dados |
| EX-01.2 | 1 | `email` fora do formato RFC 5322 ou `password` fora dos critérios da `RN-01` | `422 ERR-VAL-003` | Nada persistido na base de dados |
| EX-01.3 | 2, 4 | `email` não encontrado ou `password` incorreta com `failed_login_attempts + 1 < 5` (incluindo 1ª falha após expiração de bloqueio onde `failed_login_attempts` foi reposto a `0` no passo 3) | `401 ERR-AUTH-001` | Incrementa `failed_login_attempts` em `+1` (se utilizador existir) e grava `LOGIN_FAILED` em `auth_audit_log` |
| EX-01.4 | 3, 4 | Conta com `status == 'LOCKED'` e `now() < locked_until`, ou 5ª falha consecutiva de palavra-passe atingida (`RN-03`) | `423 ERR-AUTH-003` | Na 5ª falha: define `status = 'LOCKED'`, `failed_login_attempts = 5`, `locked_until = now() + 900 s`, grava `denylist:user:{user_id}` no Redis (`TTL = 900 s`), insere `USER_LOCKED` em `auth_audit_log` e insere `EV-03 usuario_bloqueado` em `outbox_message`. Se já estava `LOCKED` com `now() < locked_until`, grava `LOGIN_FAILED` em `auth_audit_log` |
| EX-01.5 | 3 | Conta com `status == 'SUSPENDED'` (`RN-04`) | `403 ERR-AUTH-004` | Grava `LOGIN_FAILED` em `auth_audit_log`; nenhum token emitido |

**Pós-condições:** Existe um novo registo `ACTIVE` em `refresh_token`, `failed_login_attempts == 0` em `user_account` e um registo `LOGIN_SUCCESS` em `auth_audit_log`.

---

### RF-02 — Alternar para contexto operacional unitário (`SINGLE`)
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Operador B2B (`UserAccount`) |
| Gatilho | EP-02 (`POST /api/v1/auth/switch-context` com `view_mode = 'SINGLE'`) |
| Pré-condições | Utilizador autenticado com JWT válido (`RN-05`) sem claim `act` e com família de `refresh_token` ativa |
| RN aplicáveis | RN-04, RN-05, RN-06, RN-07, RN-08, RN-10, RN-18 |

**Fluxo principal**
1. O sistema valida o token JWT de entrada (`RN-05`), confirma que não contém a claim `act` (`RN-18`) e valida o corpo do pedido (`view_mode == 'SINGLE'`, `target_tenant_id` preenchido, `target_tenants == null`).
2. Sob `SET LOCAL app.auth_flow = 'true'`, o sistema verifica que o utilizador tem `status == 'ACTIVE'` e não consta em `denylist:user:{sub}` (`RN-04`).
3. O sistema verifica que `target_tenant_id` existe, que `tenant.status == 'ACTIVE'`, que o respetivo `economic_group.status == 'ACTIVE'` e que não existe a chave `denylist:tenant:{target_tenant_id}` no Redis (`RN-07`).
4. O sistema verifica que existe registo em `tenant_member` para `(target_tenant_id, sub)` com `status == 'ACTIVE'` (`RN-06`).
5. O sistema verifica que existe registo em `tenant_scope` para `(target_tenant_id, target_scope)` com `is_active == true` e obtém os papéis do membro em `member_role` → `role` onde `role.scope == target_scope` (ou concede todas as `Role` e `permissions` do catálogo §5.5 para o `target_scope` se `tenant_member.is_owner == true`) conforme `RN-08`.
6. O sistema atualiza o registo `refresh_token` `ACTIVE` da família `fid` com `view_mode = 'SINGLE'`, `group_id = tenant.group_id`, `active_tenant_id = target_tenant_id`, `allowed_tenants = [target_tenant_id]` e `active_scope = target_scope`.
7. O sistema insere `CONTEXT_SWITCHED` em `auth_audit_log` e grava no Redis `denylist:jti:{old_jti} = "1"` com `TTL = max(1, old_exp - now_unix)` (`RN-10`).
8. O sistema emite um novo `access_token` (`exp = iat + 900 s`) com `view_mode = 'SINGLE'`, `tenant_id = target_tenant_id`, `group_id = tenant.group_id`, `allowed_tenants = [target_tenant_id]`, `active_scope = target_scope`, `roles` e `permissions` ordenadas alfabeticamente, respondendo `200 OK`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-02.1 | 1 | `target_tenant_id` ausente em modo `SINGLE` ou `target_tenants` não nulo | `400 ERR-VAL-001` | Nada alterado; token anterior permanece válido |
| EX-02.2 | 1 | Token JWT ausente, assinatura inválida, expirado ou `old_jti` em `denylist:jti:{old_jti}` | `401 ERR-AUTH-002` | Nada alterado |
| EX-02.3 | 1 | Token de entrada contém a claim `act` (impersonation) | `403 ERR-CTX-007` | Nada alterado |
| EX-02.4 | 4 | Utilizador não possui vínculo `tenant_member` ativo no `target_tenant_id` | `403 ERR-CTX-001` | Nada alterado |
| EX-02.5 | 3 | `Tenant` ou `EconomicGroup` suspenso ou presente em `denylist:tenant:{target_tenant_id}` | `403 ERR-CTX-002` | Nada alterado |
| EX-02.6 | 5 | `target_scope` inativo em `tenant_scope` ou membro sem papel para o `target_scope` e `is_owner == false` | `403 ERR-CTX-003` | Nada alterado |
| EX-02.7 | 2 | Utilizador com `status == 'SUSPENDED'` ou presente em `denylist:user:{sub}` (`RN-04`) | `403 ERR-AUTH-004` | Nada alterado |

**Pós-condições:** O `jti` anterior consta na Denylist Redis, o `refresh_token` ativo da família reflete o contexto `SINGLE` e foi registado `CONTEXT_SWITCHED` em `auth_audit_log`.

---

### RF-03 — Alternar para visão macro de grupo (`MACRO_GROUP`)
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Operador B2B / Administrador Corporativo (`UserAccount`) |
| Gatilho | EP-02 (`POST /api/v1/auth/switch-context` com `view_mode = 'MACRO_GROUP'`) |
| Pré-condições | Utilizador autenticado com JWT válido sem claim `act` |
| RN aplicáveis | RN-04, RN-05, RN-07, RN-08, RN-09, RN-10, RN-18 |

**Fluxo principal**
1. O sistema valida o token JWT de entrada (`RN-05`), confirma a ausência da claim `act` (`RN-18`) e valida o corpo (`view_mode == 'MACRO_GROUP'`, `target_tenant_id == null`, `target_group_id` preenchido e `target_tenants` contendo entre 1 e 50 UUIDs distintos).
2. Sob `SET LOCAL app.auth_flow = 'true'`, o sistema verifica que o utilizador tem `status == 'ACTIVE'` e não consta em `denylist:user:{sub}` (`RN-04`), e que o `economic_group` (`target_group_id`) existe e tem `status == 'ACTIVE'`.
3. Para cada `t_id` em `target_tenants`, o sistema verifica a regra tudo-ou-nada (`RN-07`, `RN-08`, `RN-09`):
   - O `Tenant` existe, `tenant.group_id == target_group_id`, `tenant.status == 'ACTIVE'` e não consta em `denylist:tenant:{t_id}` (verificado via `MGET` no Redis);
   - Existe `tenant_scope` para `(t_id, target_scope)` com `is_active == true`;
   - Existe `tenant_member` para `(t_id, sub)` com `status == 'ACTIVE'` e `can_view_group == true` (obrigatório em todos os `target_tenants`, mesmo quando `is_owner == true`);
   - O membro detém pelo menos 1 `role` com `scope == target_scope` nesse `Tenant` (ou `is_owner == true`, que concede todas as `Role` e `permissions` do catálogo §5.5 para o `target_scope`).
4. O sistema calcula a lista de `permissions` do token como a **interseção** ordenada alfabeticamente dos conjuntos de permissões que o utilizador detém para `target_scope` em todos os `target_tenants`, e a lista `roles` como a **interseção** ordenada alfabeticamente dos nomes dos papéis detidos para `target_scope` nos `target_tenants` (considerando todas as `Role` do catálogo §5.5 do `target_scope` nos Tenants onde `is_owner == true`).
5. O sistema atualiza o registo `refresh_token` `ACTIVE` da família `fid` com `view_mode = 'MACRO_GROUP'`, `group_id = target_group_id`, `active_tenant_id = NULL`, `allowed_tenants = target_tenants` e `active_scope = target_scope`.
6. O sistema insere `CONTEXT_SWITCHED` em `auth_audit_log` e grava `denylist:jti:{old_jti} = "1"` no Redis com `TTL = max(1, old_exp - now_unix)` (`RN-10`).
7. O sistema emite um novo `access_token` (`exp = iat + 900 s`) com `view_mode = 'MACRO_GROUP'`, `tenant_id = null`, `group_id = target_group_id`, `allowed_tenants = target_tenants`, `active_scope = target_scope`, `roles` e `permissions`, respondendo `200 OK`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-03.1 | 1 | `target_group_id` ausente, `target_tenant_id` não nulo ou `target_tenants` nulo | `400 ERR-VAL-001` | Nada alterado |
| EX-03.2 | 1, 3 | `target_tenants` vazio, com mais de 50 itens, com UUIDs duplicados, com algum tenant fora do `target_group_id` ou onde o utilizador tenha `can_view_group == false` (mesmo que `is_owner == true`) ou não seja membro ativo (`RN-09`) | `403 ERR-CTX-004` | Nada alterado; falha tudo-ou-nada |
| EX-03.3 | 2, 3 | `target_group_id` suspenso ou algum tenant de `target_tenants` suspenso ou em `denylist:tenant:{id}` (`RN-07`) | `403 ERR-CTX-002` | Nada alterado |
| EX-03.4 | 3 | Algum tenant de `target_tenants` não possui `target_scope` ativo ou o utilizador não possui papel para esse escopo nesse tenant (`RN-08`) | `403 ERR-CTX-003` | Nada alterado |
| EX-03.5 | 1, 2 | Token JWT inválido/revogado (`401 ERR-AUTH-002`), token com claim `act` (`403 ERR-CTX-007`) ou utilizador suspenso/em `denylist:user` (`403 ERR-AUTH-004`) | `401 ERR-AUTH-002` / `403 ERR-CTX-007` / `403 ERR-AUTH-004` | Nada alterado |

**Pós-condições:** O `jti` anterior consta na Denylist Redis, o novo JWT possui `tenant_id = null` e `allowed_tenants = target_tenants`, e `refresh_token` guarda o contexto `MACRO_GROUP`.

---

### RF-04 — Renovar sessão por rotação de refresh token
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Operador B2B / Agente de Suporte (`UserAccount`) |
| Gatilho | EP-03 (`POST /api/v1/auth/refresh`) |
| Pré-condições | Posse de um `refresh_token` de 43 caracteres `base64url` |
| RN aplicáveis | RN-04, RN-07, RN-11, RN-12 |

**Fluxo principal**
1. O sistema valida o formato do campo `refresh_token` (`^[A-Za-z0-9_-]{43}$`) e calcula o seu hash SHA-256 em hexadecimal minúsculo (`token_hash`).
2. Sob `SET LOCAL app.auth_flow = 'true'`, o sistema procura o registo em `refresh_token` (`ENT-08`) por `token_hash` com bloqueio pessimista (`FOR UPDATE`). Se não existir, rejeita imediatamente com `401 ERR-AUTH-005` (`RN-12`).
3. O sistema verifica se `status == 'CONSUMED'`; em caso afirmativo, aciona imediatamente a deteção de reuso (`RN-11`, `EX-04.2`).
4. O sistema verifica que o `user_account` titular tem `status == 'ACTIVE'` e não está bloqueado em `denylist:user:{user_id}` (`RN-04`, `EX-04.4`).
5. Se `view_mode != 'NONE'`, o sistema revalida que o `group_id` e todos os CNPJs em `allowed_tenants` permanecem com `status == 'ACTIVE'` e fora de `denylist:tenant:{id}` (`RN-07`, `EX-04.5`).
6. O sistema verifica que `refresh_token.status == 'ACTIVE'` e que `expires_at > now()` (`RN-12`, `EX-04.3`).
7. O sistema gera um novo `RefreshToken` opaco (43 caracteres `base64url`), insere o novo registo `ACTIVE` na mesma `family_id` (copiando `view_mode`, `group_id`, `active_tenant_id`, `allowed_tenants`, `active_scope` e definindo `expires_at = now() + 604800 s`) e atualiza o registo anterior para `status = 'CONSUMED'`, `consumed_at = now()`, `replaced_by_id = new_token.id`.
8. O sistema emite um novo `access_token` (`exp = iat + 900 s`) com o contexto e permissões atualizadas da sessão, insere `TOKEN_REFRESHED` em `auth_audit_log` e responde `200 OK`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-04.1 | 1 | `refresh_token` ausente, nulo ou com formato diferente de 43 caracteres `base64url` | `400 ERR-VAL-001` | Nada persistido |
| EX-04.2 | 3 | Registo encontrado com `status == 'CONSUMED'` (`RN-11` — deteção de reuso) | `401 ERR-AUTH-006` | Atualiza **todos** os registos da `family_id` (`status IN ('ACTIVE', 'CONSUMED')`) para `status = 'REVOKED'` e `revoked_at = COALESCE(revoked_at, now())`; grava `denylist:user:{user_id} = "<now_unix>"` no Redis (`TTL = 900 s`); insere `TOKEN_REUSE_DETECTED` em `auth_audit_log`; insere `EV-04 sessao_comprometida` em `outbox_message` |
| EX-04.3 | 2, 6 | `token_hash` inexistente, ou (`status == 'REVOKED'` / `expires_at <= now()` com titular e contexto ativos) (`RN-12`) | `401 ERR-AUTH-005` | Nada alterado |
| EX-04.4 | 4 | Utilizador titular com `status == 'SUSPENDED'` ou em `denylist:user:{user_id}` (`RN-04`) | `403 ERR-AUTH-004` | Todos os registos da `family_id` marcados como `REVOKED` (`revoked_at = COALESCE(revoked_at, now())`) |
| EX-04.5 | 5 | Algum `Tenant` de `allowed_tenants` ou o `EconomicGroup` foi suspenso ou consta em `denylist:tenant:{id}` (`RN-07`) | `403 ERR-CTX-002` | Todos os registos da `family_id` marcados como `REVOKED` (`revoked_at = COALESCE(revoked_at, now())`) |

**Pós-condições:** O `refresh_token` anterior está `CONSUMED` com `replaced_by_id` preenchido, existe exatamente 1 novo registo `ACTIVE` na `family_id` e foi gravado `TOKEN_REFRESHED` em `auth_audit_log`.

---

### RF-05 — Encerrar sessão ativa no logout
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Operador B2B / Agente de Suporte (`UserAccount`) |
| Gatilho | EP-04 (`POST /api/v1/auth/logout`) |
| Pré-condições | Utilizador autenticado com JWT válido (`RN-05`) |
| RN aplicáveis | RN-05, RN-10 |

**Fluxo principal**
1. O sistema valida o JWT no header `Authorization` (`RN-05`) e extrai as claims `jti`, `sub`, `fid`, `exp` e `act`.
2. Se `fid` estiver presente no token, o sistema executa `UPDATE refresh_token SET status = 'REVOKED', revoked_at = COALESCE(revoked_at, now()) WHERE family_id = @fid AND status IN ('ACTIVE', 'CONSUMED')`.
3. O sistema insere um registo em `auth_audit_log` com `event_type = 'LOGOUT'`, `user_id = @sub` e `actor_user_id = @act.sub`.
4. O sistema grava no Redis a chave `denylist:jti:{jti} = "1"` com `TTL = max(1, exp - now_unix)` segundos (`RN-10`) e responde `204 No Content`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-05.1 | 1 | Header `Authorization` ausente, token malformado, expirado ou `jti` já presente em `denylist:jti:{jti}` | `401 ERR-AUTH-002` | Nada alterado |

**Pós-condições:** A chave `denylist:jti:{jti}` existe no Redis e todos os registos `refresh_token` da família `fid` possuem `status = 'REVOKED'`.

---

### RF-06 — Emitir token de delegação para suporte via impersonation
| Campo | Valor |
|---|---|
| Prioridade | SHOULD |
| Ator | Agente de Suporte / Administrador da Plataforma (`SUPPORT_AGENT` / `PLATFORM_ADMIN`) |
| Gatilho | EP-05 (`POST /api/v1/auth/impersonate`) |
| Pré-condições | Analista autenticado com JWT válido (`platform_role IN ('SUPPORT_AGENT', 'PLATFORM_ADMIN')`) e sem claim `act` |
| RN aplicáveis | RN-04, RN-05, RN-06, RN-07, RN-08, RN-13, RN-14 |

**Fluxo principal**
1. O sistema valida o JWT do analista (`RN-05`) e verifica que o chamador tem `platform_role IN ('SUPPORT_AGENT', 'PLATFORM_ADMIN')` e que o token atual **não** possui a claim `act` (`RN-13`).
2. O sistema valida os campos do pedido e confirma que `ticket_id` obedece à expressão regular `^[A-Z]{2,10}-[0-9]{1,10}$` (`RN-14`).
3. Sob `SET LOCAL app.auth_flow = 'true'`, o sistema verifica que `target_user_id` e `target_tenant_id` existem na base de dados (`EX-06.7`), que o utilizador alvo tem `platform_role == 'NONE'`, que `target_user_id != caller_sub` (`RN-13`) e que `status == 'ACTIVE'` e fora de `denylist:user:{target_user_id}` (`RN-04`).
4. O sistema verifica que `target_tenant_id` e o respetivo `EconomicGroup` têm `status == 'ACTIVE'` e não constam em `denylist:tenant:{target_tenant_id}` (`RN-07`).
5. O sistema verifica que `target_user_id` possui vínculo `tenant_member` com `status == 'ACTIVE'` em `target_tenant_id` (`RN-06`) e que `target_scope` está ativo em `tenant_scope` com pelo menos 1 papel atribuído ao membro (ou `is_owner == true`) (`RN-08`).
6. O sistema insere um registo em `auth_audit_log` com `event_type = 'IMPERSONATION_STARTED'`, `user_id = target_user_id`, `actor_user_id = caller_sub`, `tenant_id = target_tenant_id`, `group_id = tenant.group_id` e `ticket_id = ticket_id`.
7. O sistema emite um `access_token` (`exp = iat + 900 s`, sem `fid` e sem emitir `RefreshToken`) em modo `SINGLE`, com `sub = target_user_id`, `tenant_id = target_tenant_id`, `group_id = tenant.group_id`, `allowed_tenants = [target_tenant_id]`, `active_scope = target_scope`, `roles` e `permissions` do utilizador alvo, e a claim estruturada `act: { "sub": "<caller_sub>", "ticket_id": "<ticket_id>" }`, respondendo `200 OK`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-06.1 | 1, 3 | Chamador com `platform_role == 'NONE'`, ou token já contém claim `act`, ou `target_user_id` possui `platform_role != 'NONE'`, ou `target_user_id == caller_sub` (`RN-13`) | `403 ERR-AUTH-010` | Nada emitido |
| EX-06.2 | 2 | `ticket_id` não cumpre a regex `^[A-Z]{2,10}-[0-9]{1,10}$` (`RN-14`) | `422 ERR-VAL-004` | Nada emitido |
| EX-06.3 | 3 | Utilizador alvo com `status == 'SUSPENDED'`, `status == 'LOCKED'` (com `now() < locked_until`) ou presente em `denylist:user:{target_user_id}` (`RN-04`) | `403 ERR-AUTH-004` | Nada emitido |
| EX-06.4 | 5 | Utilizador alvo não possui vínculo `tenant_member` com `status == 'ACTIVE'` em `target_tenant_id` (`RN-06`) | `403 ERR-CTX-001` | Nada emitido |
| EX-06.5 | 4 | `target_tenant_id` ou respetivo `EconomicGroup` com `status == 'SUSPENDED'` ou em `denylist:tenant:{target_tenant_id}` (`RN-07`) | `403 ERR-CTX-002` | Nada emitido |
| EX-06.6 | 5 | `target_scope` inativo em `tenant_scope` ou utilizador alvo sem `Role` nesse escopo e `is_owner == false` (`RN-08`) | `403 ERR-CTX-003` | Nada emitido |
| EX-06.7 | 3 | `target_user_id` ou `target_tenant_id` não existe na base de dados | `404 ERR-CTX-008` | Nada emitido |

**Pós-condições:** Foi emitido um JWT com claim `act`, nenhum `RefreshToken` foi criado e existe 1 registo `IMPERSONATION_STARTED` em `auth_audit_log`.

---

### RF-07 — Cadastrar Tenant e escopos operacionais
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Administrador Corporativo (`TENANT_ADMIN`) / Administrador da Plataforma (`PLATFORM_ADMIN`) |
| Gatilho | EP-06 (`POST /api/v1/tenants`) |
| Pré-condições | Utilizador autenticado com `platform_role == 'PLATFORM_ADMIN'` ou (`active_scope == 'TENANT_ADMIN'`, permissão `tenant:criar` e `group_id == request.group_id`) |
| RN aplicáveis | RN-05, RN-07, RN-15, RN-16, RN-18 |

**Fluxo principal**
1. O sistema valida o JWT (`RN-05`) e verifica a autorização do chamador para o `group_id` informado (`RN-18`) **antes** de qualquer consulta de existência na base de dados.
2. O sistema valida o formato dos campos, o algoritmo módulo 11 dos dígitos verificadores do `cnpj` e os limites de `geo_lat` e `geo_lng` (`RN-15`).
3. O sistema verifica que o `economic_group` (`group_id`) existe (`EX-07.3`), tem `status == 'ACTIVE'` (`RN-07`, `EX-07.5`) e que não existe outro `Tenant` com o mesmo `cnpj` (`RN-16`, `EX-07.4`).
4. Numa única transação ACID, o sistema insere o registo em `tenant` (`ENT-02`) com `status = 'ACTIVE'`, insere um registo em `tenant_scope` (`ENT-03`) com `is_active = true` para cada item de `scopes`, insere `TENANT_CREATED` em `auth_audit_log` (`ENT-09`) e insere a mensagem `EV-01 empresa_cadastrada` em `outbox_message` (`ENT-10`) com cabeçalho `context_jws` assinado.
5. O sistema responde `201 Created` com os dados do `Tenant` criado.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-07.1 | 1 | Chamador não é `PLATFORM_ADMIN` e não possui `active_scope == 'TENANT_ADMIN'` com permissão `tenant:criar` no mesmo `group_id` (`RN-18`) | `403 ERR-CTX-007` | Nada persistido |
| EX-07.2 | 2 | `cnpj` com dígitos verificadores inválidos ou `geo_lat`/`geo_lng` fora dos intervalos permitidos (`RN-15`) | `422 ERR-VAL-002` | Nada persistido |
| EX-07.3 | 3 | `group_id` inexistente em `economic_group` | `404 ERR-CTX-008` | Nada persistido |
| EX-07.4 | 3 | `cnpj` já cadastrado em `tenant` (`RN-16`) | `409 ERR-CTX-005` | Nada persistido |
| EX-07.5 | 3 | `economic_group` (`group_id`) encontra-se com `status == 'SUSPENDED'` (`RN-07`) | `403 ERR-CTX-002` | Nada persistido |

**Pós-condições:** O novo `Tenant` e os seus `TenantScope` estão persistidos em PostgreSQL e existe 1 registo `PENDING` em `outbox_message` para o tópico `empresa_cadastrada`.

---

### RF-08 — Vincular membro e papéis a um Tenant
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Administrador Corporativo (`TENANT_ADMIN`) / Administrador da Plataforma (`PLATFORM_ADMIN`) |
| Gatilho | EP-07 (`POST /api/v1/tenants/{tenant_id}/members`) |
| Pré-condições | Chamador autenticado com `active_scope == 'TENANT_ADMIN'`, permissão `membro:vincular` e `tenant_id` contido em `AuthContext.AllowedTenants` (ou `platform_role == 'PLATFORM_ADMIN'`) |
| RN aplicáveis | RN-05, RN-16, RN-17, RN-18 |

**Fluxo principal**
1. O sistema valida o JWT (`RN-05`) e verifica que o chamador possui `active_scope == 'TENANT_ADMIN'`, permissão `membro:vincular` e que o `tenant_id` do path pertence a `AuthContext.AllowedTenants` (ou `platform_role == 'PLATFORM_ADMIN'`) (`RN-18`) **antes** de consultar a existência do `tenant_id`.
2. O sistema verifica a existência do `tenant_id` em `tenant`, do `user_id` em `user_account` e de todos os `role_ids` em `role` (`EX-08.2`).
3. O sistema verifica que não existe vínculo prévio `(tenant_id, user_id)` em `tenant_member` (`RN-16`, `EX-08.3`).
4. O sistema verifica que o `scope` de cada `Role` em `role_ids` corresponde a um `tenant_scope` com `is_active = true` no `tenant_id` alvo (`RN-17`, `EX-08.4`).
5. Numa única transação ACID protegida pelo `TenantSaveChangesInterceptor` e PostgreSQL RLS (`app.allowed_tenants`), o sistema insere o registo em `tenant_member` (`ENT-05`), insere os registos em `member_role` (`ENT-07`), insere `MEMBER_ADDED` em `auth_audit_log` (`ENT-09`) e responde `201 Created`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-08.1 | 1 | Chamador com `active_scope != 'TENANT_ADMIN'` ou `tenant_id` fora de `AuthContext.AllowedTenants` (`RN-18`) | `403 ERR-CTX-007` | Transação recusada pelo Guard / Interceptor / RLS; nada persistido |
| EX-08.2 | 2 | `tenant_id`, `user_id` ou algum `role_id` não encontrado | `404 ERR-CTX-008` | Nada persistido |
| EX-08.3 | 3 | `user_id` já vinculado ao `tenant_id` (`RN-16`) | `409 ERR-CTX-005` | Nada persistido |
| EX-08.4 | 4 | Algum `role_id` pertence a um escopo que não está ativo em `tenant_scope` para o `tenant_id` (`RN-17`) | `422 ERR-CTX-006` | Nada persistido |

**Pós-condições:** O vínculo `tenant_member` e os respetivos `member_role` estão persistidos e existe 1 registo `MEMBER_ADDED` em `auth_audit_log`.

---

### RF-09 — Suspender ou reativar Tenant com revogação imediata
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Administrador da Plataforma (`PLATFORM_ADMIN`) / Proprietário do Grupo (`TENANT_ADMIN` com `is_owner = true`) |
| Gatilho | EP-08 (`PATCH /api/v1/tenants/{tenant_id}/status`) |
| Pré-condições | `Tenant` existente em `ENT-02` |
| RN aplicáveis | RN-05, RN-07, RN-18 |

**Fluxo principal**
1. O sistema valida o JWT (`RN-05`) e verifica que o chamador é `PLATFORM_ADMIN` ou possui `active_scope == 'TENANT_ADMIN'` com `tenant_id` em `AllowedTenants` e `is_owner == true` (`RN-18`) **antes** de carregar o `Tenant`.
2. O sistema carrega o `Tenant` (`tenant_id`; se não existir, retorna `404 ERR-CTX-008`, `EX-09.2`) e atualiza `tenant.status = @status` e `updated_at = now()`.
3. Se `@status == 'SUSPENDED'`, na mesma transação ACID o sistema atualiza para `status = 'REVOKED'` (`revoked_at = COALESCE(revoked_at, now())`) todos os registos `refresh_token` (`status IN ('ACTIVE', 'CONSUMED')`) que contenham `tenant_id` em `active_tenant_id` ou em `allowed_tenants`, insere `TENANT_STATUS_CHANGED` em `auth_audit_log` (com `metadata = {"status": @status, "reason": @reason}`) e insere `EV-02 tenant_bloqueado` em `outbox_message`. Se `@status == 'ACTIVE'`, insere apenas `TENANT_STATUS_CHANGED` em `auth_audit_log`.
4. Imediatamente após o commit, se `@status == 'SUSPENDED'`, o sistema grava `denylist:tenant:{tenant_id} = "1"` no Redis com `TTL = 86400 s` (`RN-07`); se `@status == 'ACTIVE'`, remove a chave com `DEL denylist:tenant:{tenant_id}`.
5. O sistema responde `200 OK`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-09.1 | 1 | Chamador sem privilégio `PLATFORM_ADMIN` e sem ser `TENANT_ADMIN` proprietário (`is_owner == true`) do `tenant_id` (`RN-18`) | `403 ERR-CTX-007` | Nada alterado |
| EX-09.2 | 2 | `tenant_id` não encontrado | `404 ERR-CTX-008` | Nada alterado |

**Pós-condições:** Quando suspenso, `tenant.status == 'SUSPENDED'`, a chave `denylist:tenant:{tenant_id}` existe no Redis, todos os `refresh_token` associados estão `REVOKED` e o evento `EV-02` está na `outbox_message`.

---

### RF-10 — Suspender ou reativar utilizador com revogação imediata
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | Administrador Corporativo (`TENANT_ADMIN`) / Administrador da Plataforma (`PLATFORM_ADMIN`) |
| Gatilho | EP-09 (`PATCH /api/v1/users/{user_id}/status`) |
| Pré-condições | `UserAccount` existente em `ENT-04` |
| RN aplicáveis | RN-04, RN-05, RN-18 |

**Fluxo principal**
1. O sistema valida o JWT (`RN-05`), verifica a existência do `user_id` em `user_account` (`EX-10.2`) e verifica que o chamador é `PLATFORM_ADMIN` ou possui `active_scope == 'TENANT_ADMIN'`, permissão `usuario:suspender` e que o `user_id` alvo possui vínculo em pelo menos um `Tenant` do mesmo `group_id` do chamador (e tem `platform_role == 'NONE'`) (`RN-18`, `EX-10.1`).
2. O sistema atualiza `user_account`: `status = @status`, `failed_login_attempts = 0`, `locked_until = NULL`, `updated_at = now()`.
3. Se `@status == 'SUSPENDED'`, na mesma transação ACID o sistema atualiza todos os registos `refresh_token` (`status IN ('ACTIVE', 'CONSUMED')`) de `user_id` para `status = 'REVOKED'` (`revoked_at = COALESCE(revoked_at, now())`), insere `USER_STATUS_CHANGED` em `auth_audit_log` (com `metadata = {"status": @status, "reason": @reason}`) e insere `EV-03 usuario_bloqueado` em `outbox_message`. Se `@status == 'ACTIVE'`, insere apenas `USER_STATUS_CHANGED` em `auth_audit_log`.
4. Imediatamente após o commit, se `@status == 'SUSPENDED'`, o sistema grava `denylist:user:{user_id} = "<now_unix>"` no Redis com `TTL = 900 s` (`RN-04`); se `@status == 'ACTIVE'`, executa `DEL denylist:user:{user_id}`.
5. O sistema responde `200 OK`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-10.1 | 1 | Chamador não autorizado para o grupo do utilizador alvo ou tentativa de um `TENANT_ADMIN` suspender utilizador com `platform_role != 'NONE'` (`RN-18`) | `403 ERR-CTX-007` | Nada alterado |
| EX-10.2 | 1 | `user_id` não encontrado | `404 ERR-CTX-008` | Nada alterado |

**Pós-condições:** Quando suspenso, `user_account.status == 'SUSPENDED'`, a chave `denylist:user:{user_id}` existe no Redis, todos os `refresh_token` do utilizador estão `REVOKED` e o evento `EV-03` está na `outbox_message`.

---

### RF-11 — Expor conjunto de chaves públicas JWKS
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator | API Gateway / Microsserviços de Domínio B2B |
| Gatilho | EP-10 (`GET /.well-known/jwks.json`) |
| Pré-condições | Par de chaves RSA 2048 bits carregado em memória no serviço |
| RN aplicáveis | RN-05 |

**Fluxo principal**
1. O sistema recebe o pedido `GET /.well-known/jwks.json`.
2. O sistema exporta os parâmetros públicos (`kty = "RSA"`, `use = "sig"`, `alg = "RS256"`, `kid`, módulo `n` em `base64url` e expoente `e` em `base64url`) da chave RSA ativa.
3. O sistema responde `200 OK` com o documento JSON Web Key Set e cabeçalho `Cache-Control: public, max-age=300`.

**Fluxos de exceção**
| ID | No passo | Condição | Resposta (status + ERR) | Efeitos / Estado final |
|---|---|---|---|---|
| EX-11.1 | 2 | Erro interno inesperado ao ler ou exportar o material criptográfico RSA | `500 ERR-SYS-001` | Registo de erro no log estruturado e retorno de Problem Details sem stack trace |

**Pós-condições:** O cliente obtém a chave pública capaz de verificar a assinatura RS256 de qualquer `access_token` e `context_jws` emitido pelo serviço.

---

## 8. Regras de Negócio

| ID | Tipo | Regra (forma canónica) | Parâmetros | Erro se violada | RF afetados |
|---|---|---|---|---|---|
| RN-01 | Validação | **SE** `email` (após `trim()`) não corresponder ao formato de e-mail RFC 5322 com comprimento entre `MIN_EMAIL_LEN` e `MAX_EMAIL_LEN` OU `password` tiver comprimento `< MIN_PASS_LEN` OU `> MAX_PASS_LEN` OU não contiver pelo menos 1 letra maiúscula (`[A-Z]`), 1 letra minúscula (`[a-z]`), 1 dígito (`[0-9]`) e 1 carácter especial (`[^A-Za-z0-9]`) **ENTÃO** rejeitar o pedido com HTTP 422 `ERR-VAL-003` **SENÃO** prosseguir com a autenticação. | `MIN_EMAIL_LEN = 5 caracteres`; `MAX_EMAIL_LEN = 320 caracteres`; `MIN_PASS_LEN = 12 caracteres`; `MAX_PASS_LEN = 128 caracteres` | `ERR-VAL-003` | RF-01 |
| RN-02 | Validação | **SE** o `email` normalizado em minúsculas não existir em `user_account` OU a verificação Argon2id de `password` contra `user_account.password_hash` retornar falso **ENTÃO** incrementar `failed_login_attempts` em `+1` (se o utilizador existir), inserir registo `LOGIN_FAILED` em `auth_audit_log` e responder HTTP 401 `ERR-AUTH-001` (salvo se `failed_login_attempts` atingir `MAX_FAILED_ATTEMPTS` da `RN-03`) **SENÃO** definir `failed_login_attempts = 0`, `locked_until = NULL` e `status = 'ACTIVE'`. | `ARGON2_MEMORY = 19456 KiB`; `ARGON2_ITERATIONS = 2`; `ARGON2_PARALLELISM = 1`; `SALT_LEN = 16 bytes`; `HASH_LEN = 32 bytes` | `ERR-AUTH-001` | RF-01 |
| RN-03 | Temporal | **SE** no início de `EP-01` `user_account.status == 'LOCKED'` E `now() >= user_account.locked_until`, o sistema **DEVE** primeiro repor `status = 'ACTIVE'`, `failed_login_attempts = 0` e `locked_until = NULL`; em seguida, **SE** `user_account.status == 'LOCKED'` (com `now() < user_account.locked_until`) OU uma falha de palavra-passe fizer `user_account.failed_login_attempts` atingir `MAX_FAILED_ATTEMPTS` **ENTÃO** (na 5ª falha: definir `status = 'LOCKED'`, `failed_login_attempts = 5`, `locked_until = now() + LOCK_DURATION_SEC`, gravar `denylist:user:{user_id}` no Redis com `TTL = LOCK_DURATION_SEC`, inserir `USER_LOCKED` em `auth_audit_log` e inserir `EV-03 usuario_bloqueado` em `outbox_message`; se já bloqueado: inserir `LOGIN_FAILED` em `auth_audit_log`) e responder HTTP 423 `ERR-AUTH-003` **SENÃO** permitir a tentativa de autenticação. | `MAX_FAILED_ATTEMPTS = 5 tentativas`; `LOCK_DURATION_SEC = 900 segundos` | `ERR-AUTH-003` | RF-01 |
| RN-04 | Autorização | **SE** `user_account.status == 'SUSPENDED'` OU (em `EP-05` o alvo tiver `status == 'LOCKED'` com `now() < locked_until`) OU existir no Redis a chave `denylist:user:{user_id}` (para `sub`, `act.sub` ou `target_user_id`) cujo valor inteiro `revoked_at_unix` satisfaça `token.iat <= revoked_at_unix` (ou em operações sem token de entrada como `EP-03`) **ENTÃO** rejeitar a operação com HTTP 403 `ERR-AUTH-004` (marcando a `family_id` como `REVOKED` quando invocado em `EP-03`) **SENÃO** permitir a continuação do fluxo. | `USER_DENYLIST_TTL_SEC = 900 segundos` | `ERR-AUTH-004` | RF-01, RF-02, RF-03, RF-04, RF-06, RF-10 |
| RN-05 | Validação | **SE** em qualquer endpoint autenticado o header `Authorization: Bearer <token>` estiver ausente, malformado, com assinatura `RS256` inválida perante a chave pública de `EP-10`, com `exp <= now_unix` (`CLOCK_SKEW_SEC = 0`) OU existir a chave `denylist:jti:{jti}` no Redis **ENTÃO** rejeitar a requisição com HTTP 401 `ERR-AUTH-002` **SENÃO** autenticar a identidade do chamador. | `ACCESS_TOKEN_TTL_SEC = 900 segundos`; `CLOCK_SKEW_SEC = 0 segundos`; `RSA_KEY_BITS = 2048 bits` | `ERR-AUTH-002` | RF-02, RF-03, RF-05, RF-06, RF-07, RF-08, RF-09, RF-10, RF-11 |
| RN-06 | Autorização | **SE** o utilizador solicitar troca de contexto unitário (`SINGLE`) ou for alvo de impersonation para um `target_tenant_id` existente no qual não possua registo na tabela `tenant_member` com `status == 'ACTIVE'` **ENTÃO** rejeitar a operação com HTTP 403 `ERR-CTX-001` **SENÃO** prosseguir. | `REQUIRED_MEMBER_STATUS = 'ACTIVE'` | `ERR-CTX-001` | RF-02, RF-06 |
| RN-07 | Autorização | **SE** o `target_tenant_id` (em `SINGLE`), o `group_id` (em `EP-06`) ou qualquer elemento de `target_tenants` / `allowed_tenants` (em `MACRO_GROUP` ou `refresh`) tiver `tenant.status == 'SUSPENDED'`, pertencer a um `economic_group` com `status == 'SUSPENDED'` OU possuir chave ativa `denylist:tenant:{tenant_id}` no Redis **ENTÃO** rejeitar a operação com HTTP 403 `ERR-CTX-002` (marcando a `family_id` como `REVOKED` quando invocado em `EP-03`) **SENÃO** prosseguir. | `TENANT_DENYLIST_TTL_SEC = 86400 segundos` | `ERR-CTX-002` | RF-02, RF-03, RF-04, RF-06, RF-07, RF-09 |
| RN-08 | Autorização | **SE** para o `target_tenant_id` (ou para qualquer tenant em `target_tenants`) não existir registo em `tenant_scope` com `scope == target_scope` e `is_active == true` OU (o membro tiver `is_owner == false` E não possuir nenhuma `role` vinculada em `member_role` cujo `role.scope == target_scope`) **ENTÃO** rejeitar a operação com HTTP 403 `ERR-CTX-003` **SENÃO** resolver o conjunto de papéis e permissões do membro naquele Tenant para o `target_scope` (todas as `Role` e `permissions` do catálogo §5.5 para `target_scope` se `is_owner == true`, ou a união das `Role` e `permissions` atribuídas em `member_role` para `target_scope` se `is_owner == false`). | `OWNER_FULL_SCOPE_ACCESS = true` | `ERR-CTX-003` | RF-02, RF-03, RF-06 |
| RN-09 | Autorização | **SE** em `EP-02` com `view_mode == 'MACRO_GROUP'` a lista `target_tenants` tiver `< MIN_MACRO_TENANTS` ou `> MAX_MACRO_TENANTS` elementos, contiver UUIDs duplicados, contiver algum `tenant_id` cujo `tenant.group_id != target_group_id` OU contiver algum `tenant_id` no qual o utilizador não possua `tenant_member` com `status == 'ACTIVE'` e `can_view_group == true` (exigido inclusive quando `is_owner == true`) **ENTÃO** rejeitar toda a requisição (tudo-ou-nada) com HTTP 403 `ERR-CTX-004` **SENÃO** emitir JWT com `tenant_id = null`, `allowed_tenants = target_tenants`, `roles` igual à interseção ordenada dos papéis detidos para `target_scope` em todos os `target_tenants` e `permissions` igual à interseção ordenada das permissões detidas para `target_scope` em todos os `target_tenants`. | `MIN_MACRO_TENANTS = 1`; `MAX_MACRO_TENANTS = 50` | `ERR-CTX-004` | RF-03 |
| RN-10 | Temporal | **SE** uma execução de `EP-02` (`switch-context`) ou `EP-04` (`logout`) for concluída com sucesso **ENTÃO** gravar no Redis a chave `denylist:jti:{jti_atual} = "1"` com `TTL = max(MIN_JTI_TTL_SEC, token.exp - now_unix)` segundos e (no `logout`) atualizar todos os registos `refresh_token` (`status IN ('ACTIVE', 'CONSUMED')`) da família `fid` para `status = 'REVOKED'` **SENÃO** manter o estado anterior se a operação falhar antes do commit. | `MIN_JTI_TTL_SEC = 1 segundo`; `MAX_JTI_TTL_SEC = 900 segundos` | `ERR-AUTH-002` | RF-02, RF-03, RF-05 |
| RN-11 | Integridade | **SE** em `EP-03` (`refresh`) o `token_hash` SHA-256 do `refresh_token` enviado corresponder a um registo com `status == 'CONSUMED'` **ENTÃO** atualizar todos os registos de `refresh_token` com a mesma `family_id` (`status IN ('ACTIVE', 'CONSUMED')`) para `status = 'REVOKED'` e `revoked_at = COALESCE(revoked_at, now())`, gravar `denylist:user:{user_id} = "<now_unix>"` no Redis com `TTL = USER_DENYLIST_TTL_SEC`, inserir `TOKEN_REUSE_DETECTED` em `auth_audit_log`, inserir `EV-04 sessao_comprometida` em `outbox_message` e responder HTTP 401 `ERR-AUTH-006` **SENÃO** prosseguir para as verificações `RN-04`, `RN-07` e `RN-12`. | `USER_DENYLIST_TTL_SEC = 900 segundos`; `TOKEN_HASH_LEN = 64 caracteres hex` | `ERR-AUTH-006` | RF-04 |
| RN-12 | Temporal | **SE** em `EP-03` (`refresh`) o `token_hash` não existir em `refresh_token` OU (após validar que utilizador e contexto estão ativos conforme `RN-04` e `RN-07`) o registo tiver `status == 'REVOKED'` OU `expires_at <= now()` **ENTÃO** rejeitar com HTTP 401 `ERR-AUTH-005` **SENÃO** marcar o registo atual como `CONSUMED` (`consumed_at = now()`) e inserir um novo registo `ACTIVE` na mesma `family_id` com `expires_at = now() + REFRESH_TOKEN_TTL_SEC`. | `REFRESH_TOKEN_TTL_SEC = 604800 segundos` (7 dias); `RAW_TOKEN_LEN = 43 caracteres base64url` | `ERR-AUTH-005` | RF-04 |
| RN-13 | Autorização | **SE** em `EP-05` (`impersonate`) o chamador tiver `platform_role == 'NONE'` OU o token de chamada já contiver a claim `act` OU o utilizador alvo (`target_user_id`) tiver `platform_role != 'NONE'` OU `target_user_id == caller_sub` **ENTÃO** rejeitar com HTTP 403 `ERR-AUTH-010` **SENÃO** permitir a delegação. | `ALLOWED_CALLER_ROLES = {'SUPPORT_AGENT', 'PLATFORM_ADMIN'}`; `ALLOWED_TARGET_ROLE = 'NONE'` | `ERR-AUTH-010` | RF-06 |
| RN-14 | Validação | **SE** em `EP-05` (`impersonate`) o campo `ticket_id` não corresponder à expressão regular `^[A-Z]{2,10}-[0-9]{1,10}$` **ENTÃO** rejeitar com HTTP 422 `ERR-VAL-004` **SENÃO** emitir exclusivamente `access_token` em `view_mode = 'SINGLE'` com `exp = iat + IMPERSONATE_TTL_SEC` e claim `act: { "sub": caller_sub, "ticket_id": ticket_id }`, sem gerar `refresh_token`. | `IMPERSONATE_TTL_SEC = 900 segundos`; `TICKET_REGEX = ^[A-Z]{2,10}-[0-9]{1,10}$` | `ERR-VAL-004` | RF-06 |
| RN-15 | Validação | **SE** em `EP-06` o campo `cnpj` não contiver exatamente `CNPJ_LEN` dígitos numéricos, tiver todos os 14 dígitos iguais OU falhar no cálculo oficial módulo 11 dos 2 dígitos verificadores da Receita Federal, OU `geo_lat` estiver fora de `[LAT_MIN, LAT_MAX]` OU `geo_lng` estiver fora de `[LNG_MIN, LNG_MAX]` **ENTÃO** rejeitar com HTTP 422 `ERR-VAL-002` **SENÃO** aceitar os dados do `Tenant`. | `CNPJ_LEN = 14 dígitos`; `LAT_MIN = -90.0`; `LAT_MAX = 90.0`; `LNG_MIN = -180.0`; `LNG_MAX = 180.0` | `ERR-VAL-002` | RF-07 |
| RN-16 | Integridade | **SE** em `EP-06` já existir um `Tenant` com o mesmo `cnpj` OU em `EP-07` já existir um `TenantMember` com o mesmo par `(tenant_id, user_id)` **ENTÃO** rejeitar a criação com HTTP 409 `ERR-CTX-005` **SENÃO** persistir o novo registo. | `UNIQUE_CONSTRAINTS = {uq_tenant_cnpj, uq_tenant_member_tenant_user}` | `ERR-CTX-005` | RF-07, RF-08 |
| RN-17 | Integridade | **SE** em `EP-07` qualquer `role_id` informado em `role_ids` possuir `role.scope` para o qual não exista registo em `tenant_scope` com `tenant_id == path.tenant_id` e `is_active == true` **ENTÃO** rejeitar a vinculação com HTTP 422 `ERR-CTX-006` **SENÃO** vincular os papéis ao membro. | `REQUIRE_ACTIVE_TENANT_SCOPE = true` | `ERR-CTX-006` | RF-08 |
| RN-18 | Autorização | **SE** uma requisição a um endpoint de gestão (`EP-06`, `EP-07`, `EP-08`, `EP-09`) apresentar token com `view_mode == 'NONE'` (salvo quando `platform_role == 'PLATFORM_ADMIN'`) OU não satisfizer o papel/escopo exigido (`TENANT_ADMIN` com permissão específica e `tenant_id` contido em `AuthContext.AllowedTenants` ou `group_id == AuthContext.GroupId`, avaliado antes de consultas `404` de domínio, salvo `PLATFORM_ADMIN`), OU uma mutação EF Core / sessão RLS tentar ler ou escrever em `ITenantScoped` fora de `AllowedTenants`, OU uma requisição a `EP-02` apresentar token com claim `act`, OU um evento recebido pelo `KafkaConsumerFilter` tiver assinatura `context_jws` inválida ou expirada (`exp <= now_unix`, `MAX_EVENT_AGE_SEC = 300`) **ENTÃO** abortar e rejeitar com HTTP 403 `ERR-CTX-007` **SENÃO** executar a operação. | `MAX_EVENT_AGE_SEC = 300 segundos` | `ERR-CTX-007` | RF-02, RF-03, RF-07, RF-08, RF-09, RF-10 |

---

## 9. Catálogo de Erros

| Código | HTTP | `title` (texto fixo) | Condição de disparo | Origem (RN/EX) |
|---|---|---|---|---|
| `ERR-VAL-001` | 400 | `Pedido inválido ou malformado` | JSON sintaticamente inválido, campo obrigatório ausente ou `null`, tipo de dados incorreto, valor fora de enum de sintaxe ou campo desconhecido não especificado no contrato | EX-01.1, EX-02.1, EX-03.1, EX-04.1, §12.2 |
| `ERR-VAL-002` | 422 | `Dados de CNPJ ou geolocalização inválidos` | `cnpj` sem 14 dígitos numéricos, com dígitos repetidos ou dígitos verificadores módulo 11 inválidos, ou coordenadas `geo_lat`/`geo_lng` fora dos limites | RN-15, EX-07.2 |
| `ERR-VAL-003` | 422 | `Formato de e-mail ou política de palavra-passe violada` | `email` fora do formato RFC 5322 (5..320 caracteres) ou `password` fora das regras de complexidade (12..128 caracteres, maiúscula, minúscula, dígito e especial) | RN-01, EX-01.2 |
| `ERR-VAL-004` | 422 | `Código de chamado técnico inválido` | O campo `ticket_id` em `EP-05` não respeita a expressão regular `^[A-Z]{2,10}-[0-9]{1,10}$` | RN-14, EX-06.2 |
| `ERR-AUTH-001` | 401 | `Credenciais inválidas` | E-mail não encontrado em `user_account` ou palavra-passe não corresponde ao `password_hash` Argon2id | RN-02, EX-01.3 |
| `ERR-AUTH-002` | 401 | `Token de acesso ausente, inválido, expirado ou revogado` | Header `Authorization` ausente, assinatura `RS256` inválida, `exp <= now_unix` ou `jti` presente em `denylist:jti:{jti}` no Redis | RN-05, RN-10, EX-02.2, EX-03.5, EX-05.1 |
| `ERR-AUTH-003` | 423 | `Conta de utilizador temporariamente bloqueada` | Utilizador atingiu `5` falhas consecutivas de login ou tentou autenticar-se enquanto `status == 'LOCKED'` e `now() < locked_until` | RN-03, EX-01.4 |
| `ERR-AUTH-004` | 403 | `Utilizador suspenso ou revogado globalmente` | `user_account.status == 'SUSPENDED'`, utilizador alvo `LOCKED` em `EP-05` ou utilizador presente em `denylist:user:{user_id}` com `token.iat <= valor_redis` | RN-04, EX-01.5, EX-02.7, EX-03.5, EX-04.4, EX-06.3 |
| `ERR-AUTH-005` | 401 | `Refresh token inválido, revogado ou expirado` | `token_hash` não encontrado na base de dados, ou (`status == 'REVOKED'` / `expires_at <= now()` com utilizador e contexto ativos) | RN-12, EX-04.3 |
| `ERR-AUTH-006` | 401 | `Reuso de refresh token detetado e sessão revogada` | Apresentação em `EP-03` de um `RefreshToken` cujo `status` já era `'CONSUMED'`, provocando revogação imediata de toda a `family_id` | RN-11, EX-04.2 |
| `ERR-AUTH-010` | 403 | `Operação de impersonation não autorizada` | Chamador sem `platform_role IN ('SUPPORT_AGENT', 'PLATFORM_ADMIN')`, token de chamada já contém claim `act`, utilizador alvo pertence ao Control Plane (`platform_role != 'NONE'`) ou auto-impersonation | RN-13, EX-06.1 |
| `ERR-CTX-001` | 403 | `Utilizador sem vínculo ativo no Tenant solicitado` | Não existe registo em `tenant_member` com `status == 'ACTIVE'` para o par `(target_tenant_id, user_id)` | RN-06, EX-02.4, EX-06.4 |
| `ERR-CTX-002` | 403 | `Tenant ou Grupo Económico suspenso` | O `Tenant` ou o seu `EconomicGroup` possui `status == 'SUSPENDED'` ou consta em `denylist:tenant:{tenant_id}` no Redis | RN-07, EX-02.5, EX-03.3, EX-04.5, EX-06.5, EX-07.5 |
| `ERR-CTX-003` | 403 | `Escopo operacional inativo ou papel não atribuído` | O `target_scope` não está ativo em `tenant_scope` para o `Tenant` ou o membro não detém nenhuma `Role` daquele escopo (e `is_owner == false`) | RN-08, EX-02.6, EX-03.4, EX-06.6 |
| `ERR-CTX-004` | 403 | `Violação das regras de agregação em visão macro` | Em `MACRO_GROUP`, `target_tenants` está fora do limite `1..50`, contém duplicados, inclui Tenant de outro `group_id` ou inclui Tenant onde o membro tem `can_view_group == false` | RN-09, EX-03.2 |
| `ERR-CTX-005` | 409 | `Conflito de unicidade de registo` | Tentativa de criar `Tenant` com `cnpj` já existente ou vincular `TenantMember` já existente para o mesmo `(tenant_id, user_id)` | RN-16, EX-07.4, EX-08.3 |
| `ERR-CTX-006` | 422 | `Papel incompatível com os escopos ativos do Tenant` | Tentativa de atribuir em `EP-07` uma `Role` cujo `scope` não está habilitado com `is_active == true` em `tenant_scope` do `Tenant` | RN-17, EX-08.4 |
| `ERR-CTX-007` | 403 | `Violação de isolamento de contexto ou permissão insuficiente` | Acesso a rota com `view_mode == 'NONE'`, falta de permissão `TENANT_ADMIN`/`PLATFORM_ADMIN`, tentativa de leitura/escrita fora de `AllowedTenants` (Guard, Interceptor ou RLS), uso de token `act` em `switch-context` ou `context_jws` inválido | RN-18, EX-02.3, EX-03.5, EX-07.1, EX-08.1, EX-09.1, EX-10.1 |
| `ERR-CTX-008` | 404 | `Recurso de domínio não encontrado` | `group_id`, `tenant_id`, `user_id` ou `role_id` referenciado no pedido não existe na base de dados | EX-06.7, EX-07.3, EX-08.2, EX-09.2, EX-10.2 |
| `ERR-SYS-001` | 500 | `Erro interno do servidor` | Exceção inesperada de infraestrutura ou falha ao ler material criptográfico sem exposição de stack trace | EX-11.1, §12.2 |

---

## 10. Critérios de Aceitação (Gherkin)

```gherkin
# language: pt
Funcionalidade: RF-01 — Autenticar utilizador e descobrir contextos

  Contexto:
    Dado que existe o grupo económico "0190a6e2-0000-7000-8000-000000000010" com name "Holding AgroBrasil" e status "ACTIVE"
    E que existe o tenant "0190a6e2-0000-7000-8000-000000000020" no grupo "0190a6e2-0000-7000-8000-000000000010" com cnpj "11222333000181" e status "ACTIVE"
    E que o tenant "0190a6e2-0000-7000-8000-000000000020" possui os escopos ativos "BUYER", "SUPPLIER" e "TENANT_ADMIN"
    E que existe o utilizador "0190a6e2-0000-7000-8000-000000000001" com email "operador@agrobrasil.com.br", password "Agro@2026!SecurePassword", status "ACTIVE" e failed_login_attempts 2
    E que o utilizador "0190a6e2-0000-7000-8000-000000000001" é membro "ACTIVE" do tenant "0190a6e2-0000-7000-8000-000000000020" com is_owner true e can_view_group true

  @CA-01.1 @RF-01 @RN-01 @RN-02 @MUST
  Cenário: Login válido emite token inicial sem contexto ativo e retorna contextos disponíveis
    Quando é enviado POST "/api/v1/auth/login" com {"email": "  OPERADOR@agrobrasil.com.br ", "password": "Agro@2026!SecurePassword"}
    Então o status da resposta é 200
    E o campo "token_type" da resposta é "Bearer"
    E o campo "expires_in" da resposta é 900
    E o campo "refresh_expires_in" da resposta é 604800
    E o JWT em "access_token" contém as claims "sub" igual a "0190a6e2-0000-7000-8000-000000000001", "view_mode" igual a "NONE", "tenant_id" igual a null e "active_scope" igual a null
    E o campo "available_contexts[0].tenant_id" da resposta é "0190a6e2-0000-7000-8000-000000000020"
    E o registo em "user_account" com id "0190a6e2-0000-7000-8000-000000000001" tem failed_login_attempts 0
    E existe 1 registo em "refresh_token" para user_id "0190a6e2-0000-7000-8000-000000000001" com status "ACTIVE" e view_mode "NONE"
    E existe 1 registo em "auth_audit_log" com event_type "LOGIN_SUCCESS" e user_id "0190a6e2-0000-7000-8000-000000000001"

  @CA-01.2 @RF-01 @EX-01.1 @MUST
  Cenário: Pedido de login sem campo obrigatório ou com propriedade desconhecida é rejeitado com 400
    Quando é enviado POST "/api/v1/auth/login" com {"email": "operador@agrobrasil.com.br", "password": "Agro@2026!SecurePassword", "campo_extra": "invalido"}
    Então o status da resposta é 400
    E o campo "code" da resposta é "ERR-VAL-001"

  @CA-01.3 @RF-01 @RN-01 @EX-01.2 @MUST
  Esquema do Cenário: Formato de e-mail inválido ou palavra-passe fora da política de complexidade retorna 422
    Quando é enviado POST "/api/v1/auth/login" com {"email": "<email>", "password": "<password>"}
    Então o status da resposta é 422
    E o campo "code" da resposta é "ERR-VAL-003"

    Exemplos:
      | email                      | password                 |
      | email-sem-arroba           | Agro@2026!SecurePassword |
      | operador@agrobrasil.com.br | curta1!A                 |
      | operador@agrobrasil.com.br | semmaiuscula@2026!       |
      | operador@agrobrasil.com.br | SEM_MINUSCULA_2026!      |
      | operador@agrobrasil.com.br | SemDigitoAlgum@!         |
      | operador@agrobrasil.com.br | SemCaracterEspecial2026  |

  @CA-01.4 @RF-01 @RN-02 @EX-01.3 @MUST @critico
  Cenário: Palavra-passe incorreta incrementa contador de falhas e regista auditoria
    Quando é enviado POST "/api/v1/auth/login" com {"email": "operador@agrobrasil.com.br", "password": "Errada@2026!Password"}
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-001"
    E o registo em "user_account" com id "0190a6e2-0000-7000-8000-000000000001" tem failed_login_attempts 3
    E existe 1 registo em "auth_audit_log" com event_type "LOGIN_FAILED" e user_id "0190a6e2-0000-7000-8000-000000000001"

  @CA-01.5 @RF-01 @RN-03 @EX-01.4 @MUST @critico
  Cenário: Quinta falha consecutiva de login bloqueia a conta por 900 segundos, grava na Denylist Redis e publica evento
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" possui failed_login_attempts 4
    Quando é enviado POST "/api/v1/auth/login" com {"email": "operador@agrobrasil.com.br", "password": "Errada@2026!Password"}
    Então o status da resposta é 423
    E o campo "code" da resposta é "ERR-AUTH-003"
    E o registo em "user_account" com id "0190a6e2-0000-7000-8000-000000000001" tem status "LOCKED" e failed_login_attempts 5
    E existe a chave Redis "denylist:user:0190a6e2-0000-7000-8000-000000000001"
    E existe 1 registo em "auth_audit_log" com event_type "USER_LOCKED" e user_id "0190a6e2-0000-7000-8000-000000000001"
    E existe 1 registo em "outbox_message" com topic "usuario_bloqueado" e partition_key "0190a6e2-0000-7000-8000-000000000001"

  @CA-01.6 @RF-01 @RN-04 @EX-01.5 @MUST @critico
  Cenário: Utilizador suspenso é impedido de realizar login com 403
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" possui status "SUSPENDED"
    Quando é enviado POST "/api/v1/auth/login" com {"email": "operador@agrobrasil.com.br", "password": "Agro@2026!SecurePassword"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-AUTH-004"
    E a resposta não contém o campo "access_token"

  @CA-01.7 @RF-01 @RN-02 @RN-03 @EX-01.3 @MUST @critico
  Cenário: Conta LOCKED com janela de bloqueio já expirada destranca antes da verificação de senha e reinicia contagem em 1 se a senha falhar
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" possui status "LOCKED", failed_login_attempts 5 e locked_until no passado
    Quando é enviado POST "/api/v1/auth/login" com {"email": "operador@agrobrasil.com.br", "password": "Errada@2026!Password"}
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-001"
    E o registo em "user_account" com id "0190a6e2-0000-7000-8000-000000000001" tem status "ACTIVE", locked_until igual a null e failed_login_attempts 1
```

```gherkin
# language: pt
Funcionalidade: RF-02 — Alternar para contexto operacional unitário (SINGLE)

  Contexto:
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" realizou login obtendo o JWT com jti "0190a6e2-0000-7000-8000-000000000500" e family_id "0190a6e2-0000-7000-8000-000000000600"
    E que o utilizador é membro "ACTIVE" do tenant "0190a6e2-0000-7000-8000-000000000020" (grupo "0190a6e2-0000-7000-8000-000000000010") com a role "SUPPLIER_SALES" no escopo ativo "SUPPLIER"
    E que existe outro tenant "0190a6e2-0000-7000-8000-000000000099" ao qual o utilizador não pertence

  @CA-02.1 @RF-02 @RN-06 @RN-08 @RN-10 @MUST @critico
  Cenário: Troca de contexto para modo SINGLE emite novo JWT com escopo SUPPLIER e revoga jti anterior no Redis
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com o JWT "0190a6e2-0000-7000-8000-000000000500" e corpo {"view_mode": "SINGLE", "target_scope": "SUPPLIER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 200
    E o campo "context.view_mode" da resposta é "SINGLE"
    E o campo "context.tenant_id" da resposta é "0190a6e2-0000-7000-8000-000000000020"
    E o campo "context.active_scope" da resposta é "SUPPLIER"
    E o campo "context.allowed_tenants" da resposta contém exatamente ["0190a6e2-0000-7000-8000-000000000020"]
    E o campo "context.permissions" da resposta contém "fornecimento:publicar" e "leilao:lance"
    E existe a chave Redis "denylist:jti:0190a6e2-0000-7000-8000-000000000500" com valor "1"
    E o registo "ACTIVE" em "refresh_token" da family_id "0190a6e2-0000-7000-8000-000000000600" tem view_mode "SINGLE" e active_tenant_id "0190a6e2-0000-7000-8000-000000000020"
    E existe 1 registo em "auth_audit_log" com event_type "CONTEXT_SWITCHED"

  @CA-02.2 @RF-02 @EX-02.1 @MUST
  Cenário: Pedido SINGLE sem target_tenant_id é rejeitado com 400
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "SINGLE", "target_scope": "SUPPLIER", "target_tenant_id": null, "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 400
    E o campo "code" da resposta é "ERR-VAL-001"

  @CA-02.3 @RF-02 @RN-05 @RN-10 @EX-02.2 @MUST @critico
  Cenário: Requisição com jti presente na Denylist Redis é recusada com 401
    Dado que existe a chave Redis "denylist:jti:0190a6e2-0000-7000-8000-000000000500" com valor "1"
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com o JWT "0190a6e2-0000-7000-8000-000000000500" e corpo {"view_mode": "SINGLE", "target_scope": "SUPPLIER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-002"

  @CA-02.4 @RF-02 @RN-18 @EX-02.3 @MUST @critico
  Cenário: Token de impersonation (com claim act) é bloqueado ao tentar trocar de contexto
    Dado que o pedido utiliza um JWT contendo a claim "act" com sub "0190a6e2-0000-7000-8000-000000000090" e ticket_id "SUP-4821"
    Quando é enviado POST "/api/v1/auth/switch-context" com {"view_mode": "SINGLE", "target_scope": "BUYER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-007"

  @CA-02.5 @RF-02 @RN-06 @EX-02.4 @MUST @critico
  Cenário: Troca de contexto para Tenant no qual o utilizador não é membro ativo retorna 403
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "SINGLE", "target_scope": "SUPPLIER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000099", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-001"
    E a resposta não contém o campo "access_token"

  @CA-02.6 @RF-02 @RN-07 @EX-02.5 @MUST @critico
  Cenário: Troca de contexto para Tenant bloqueado na Denylist Redis retorna 403
    Dado que existe a chave Redis "denylist:tenant:0190a6e2-0000-7000-8000-000000000020" com valor "1"
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "SINGLE", "target_scope": "SUPPLIER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-002"

  @CA-02.7 @RF-02 @RN-08 @EX-02.6 @MUST @critico
  Cenário: Troca de contexto para escopo CARRIER não habilitado no Tenant retorna 403
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "SINGLE", "target_scope": "CARRIER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-003"

  @CA-02.8 @RF-02 @RN-04 @EX-02.7 @MUST @critico
  Cenário: Utilizador bloqueado na Denylist Redis com iat anterior ao bloqueio tem troca de contexto recusada com 403
    Dado que existe a chave Redis "denylist:user:0190a6e2-0000-7000-8000-000000000001" com timestamp igual ou superior ao "iat" do JWT "0190a6e2-0000-7000-8000-000000000500"
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com o JWT "0190a6e2-0000-7000-8000-000000000500" e corpo {"view_mode": "SINGLE", "target_scope": "SUPPLIER", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": null}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-AUTH-004"
```

```gherkin
# language: pt
Funcionalidade: RF-03 — Alternar para visão macro de grupo (MACRO_GROUP)

  Contexto:
    Dado que existe o grupo económico "0190a6e2-0000-7000-8000-000000000010" com status "ACTIVE"
    E que existem os tenants "0190a6e2-0000-7000-8000-000000000020" e "0190a6e2-0000-7000-8000-000000000021" no grupo "0190a6e2-0000-7000-8000-000000000010" com escopo "TENANT_ADMIN" ativo
    E que o utilizador "0190a6e2-0000-7000-8000-000000000001" é membro "ACTIVE" com is_owner true e can_view_group true no tenant "0190a6e2-0000-7000-8000-000000000020" e membro "ACTIVE" com is_owner false, can_view_group true e role "GROUP_MANAGER" no tenant "0190a6e2-0000-7000-8000-000000000021"
    E que existe o tenant "0190a6e2-0000-7000-8000-000000000022" no mesmo grupo onde o utilizador tem can_view_group false

  @CA-03.1 @RF-03 @RN-09 @RN-10 @MUST @critico
  Cenário: Troca de contexto em modo MACRO_GROUP emite JWT com tenant_id nulo e calcula interseção de roles e permissions entre membro is_owner e membro não-owner
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "MACRO_GROUP", "target_scope": "TENANT_ADMIN", "target_tenant_id": null, "target_group_id": "0190a6e2-0000-7000-8000-000000000010", "target_tenants": ["0190a6e2-0000-7000-8000-000000000020", "0190a6e2-0000-7000-8000-000000000021"]}
    Então o status da resposta é 200
    E o campo "context.view_mode" da resposta é "MACRO_GROUP"
    E o campo "context.tenant_id" da resposta é null
    E o campo "context.group_id" da resposta é "0190a6e2-0000-7000-8000-000000000010"
    E o campo "context.allowed_tenants" da resposta contém exatamente ["0190a6e2-0000-7000-8000-000000000020", "0190a6e2-0000-7000-8000-000000000021"]
    E o campo "context.active_scope" da resposta é "TENANT_ADMIN"
    E o campo "context.roles" da resposta contém exatamente ["GROUP_MANAGER"]
    E o campo "context.permissions" da resposta contém exatamente ["grupo:relatorio_consolidado", "membro:listar", "tenant:criar", "tenant:editar"]

  @CA-03.2 @RF-03 @EX-03.1 @MUST
  Cenário: Pedido MACRO_GROUP sem target_group_id ou com target_tenant_id preenchido retorna 400
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "MACRO_GROUP", "target_scope": "TENANT_ADMIN", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_group_id": null, "target_tenants": ["0190a6e2-0000-7000-8000-000000000020"]}
    Então o status da resposta é 400
    E o campo "code" da resposta é "ERR-VAL-001"

  @CA-03.3 @RF-03 @RN-09 @EX-03.2 @MUST @critico
  Cenário: Inclusão de Tenant onde o utilizador tem can_view_group false faz falhar toda a requisição MACRO_GROUP com 403
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "MACRO_GROUP", "target_scope": "TENANT_ADMIN", "target_tenant_id": null, "target_group_id": "0190a6e2-0000-7000-8000-000000000010", "target_tenants": ["0190a6e2-0000-7000-8000-000000000020", "0190a6e2-0000-7000-8000-000000000022"]}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-004"
    E a resposta não contém o campo "access_token"

  @CA-03.4 @RF-03 @RN-07 @EX-03.3 @MUST @critico
  Cenário: Inclusão de Tenant suspenso em target_tenants recusa a requisição MACRO_GROUP com 403
    Dado que o tenant "0190a6e2-0000-7000-8000-000000000021" possui status "SUSPENDED"
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "MACRO_GROUP", "target_scope": "TENANT_ADMIN", "target_tenant_id": null, "target_group_id": "0190a6e2-0000-7000-8000-000000000010", "target_tenants": ["0190a6e2-0000-7000-8000-000000000020", "0190a6e2-0000-7000-8000-000000000021"]}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-002"

  @CA-03.5 @RF-03 @RN-08 @EX-03.4 @MUST @critico
  Cenário: Falta do escopo solicitado em um dos Tenants da lista MACRO_GROUP retorna 403
    Dado que o tenant "0190a6e2-0000-7000-8000-000000000021" não possui o escopo "SUPPLIER" ativo em "tenant_scope"
    Quando é enviado POST "/api/v1/auth/switch-context" autenticado com corpo {"view_mode": "MACRO_GROUP", "target_scope": "SUPPLIER", "target_tenant_id": null, "target_group_id": "0190a6e2-0000-7000-8000-000000000010", "target_tenants": ["0190a6e2-0000-7000-8000-000000000020", "0190a6e2-0000-7000-8000-000000000021"]}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-003"

  @CA-03.6 @RF-03 @RN-04 @RN-05 @RN-18 @EX-03.5 @MUST @critico
  Esquema do Cenário: Requisição MACRO_GROUP com token revogado, token de impersonation ou utilizador suspenso é rejeitada
    Dado que a condição de autenticação do chamador é "<condicao_auth>"
    Quando é enviado POST "/api/v1/auth/switch-context" com corpo {"view_mode": "MACRO_GROUP", "target_scope": "TENANT_ADMIN", "target_tenant_id": null, "target_group_id": "0190a6e2-0000-7000-8000-000000000010", "target_tenants": ["0190a6e2-0000-7000-8000-000000000020", "0190a6e2-0000-7000-8000-000000000021"]}
    Então o status da resposta é <status_http>
    E o campo "code" da resposta é "<codigo_erro>"

    Exemplos:
      | condicao_auth           | status_http | codigo_erro  |
      | jti_na_denylist_redis   | 401         | ERR-AUTH-002 |
      | token_com_claim_act     | 403         | ERR-CTX-007  |
      | utilizador_suspenso_bd  | 403         | ERR-AUTH-004 |
```

```gherkin
# language: pt
Funcionalidade: RF-04 — Renovar sessão por rotação de refresh token

  Contexto:
    Dado que existe o utilizador "0190a6e2-0000-7000-8000-000000000001" com status "ACTIVE"
    E que na family_id "0190a6e2-0000-7000-8000-000000000077" existe o refresh token "RT_CONSUMED_43_CHARS_BASE64URL_000000000001" com id "0190a6e2-0000-7000-8000-000000000081" e status "CONSUMED"
    E que na mesma family_id "0190a6e2-0000-7000-8000-000000000077" existe o refresh token "RT_ACTIVE___43_CHARS_BASE64URL_000000000002" com id "0190a6e2-0000-7000-8000-000000000082", status "ACTIVE", view_mode "SINGLE", active_tenant_id "0190a6e2-0000-7000-8000-000000000020" e active_scope "BUYER"

  @CA-04.1 @RF-04 @RN-12 @MUST @critico
  Cenário: Rotação válida de refresh token marca o anterior como CONSUMED e cria novo token ACTIVE na família
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT_ACTIVE___43_CHARS_BASE64URL_000000000002"}
    Então o status da resposta é 200
    E a resposta contém um novo "access_token" e um novo "refresh_token" diferente de "RT_ACTIVE___43_CHARS_BASE64URL_000000000002"
    E o registo em "refresh_token" com id "0190a6e2-0000-7000-8000-000000000082" tem status "CONSUMED" e replaced_by_id preenchido
    E existe 1 novo registo em "refresh_token" na family_id "0190a6e2-0000-7000-8000-000000000077" com status "ACTIVE" e active_scope "BUYER"
    E existe 1 registo em "auth_audit_log" com event_type "TOKEN_REFRESHED"

  @CA-04.2 @RF-04 @EX-04.1 @MUST
  Esquema do Cenário: Formato inválido de refresh token retorna 400
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "<valor>"}
    Então o status da resposta é 400
    E o campo "code" da resposta é "ERR-VAL-001"

    Exemplos:
      | valor      |
      |            |
      | token-curt |
      | invalido!@#43caracteres==================== |

  @CA-04.3 @RF-04 @RN-11 @EX-04.2 @MUST @critico
  Cenário: Reuso de refresh token já CONSUMED revoga toda a família, grava na Denylist Redis e publica evento
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT_CONSUMED_43_CHARS_BASE64URL_000000000001"}
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-006"
    E todos os registos de "refresh_token" com family_id "0190a6e2-0000-7000-8000-000000000077" têm status "REVOKED"
    E existe a chave Redis "denylist:user:0190a6e2-0000-7000-8000-000000000001"
    E existe 1 registo em "auth_audit_log" com event_type "TOKEN_REUSE_DETECTED"
    E existe 1 registo em "outbox_message" com topic "sessao_comprometida" e partition_key "0190a6e2-0000-7000-8000-000000000001"

  @CA-04.4 @RF-04 @RN-12 @EX-04.3 @MUST
  Cenário: Refresh token inexistente, REVOKED ou expirado retorna 401 ERR-AUTH-005
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT_UNKNOWN__43_CHARS_BASE64URL_999999999999"}
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-005"

  @CA-04.5 @RF-04 @RN-07 @EX-04.5 @MUST @critico
  Cenário: Tentativa de refresh com Tenant do contexto ativo suspenso revoga o token e retorna 403
    Dado que o tenant "0190a6e2-0000-7000-8000-000000000020" possui status "SUSPENDED"
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT_ACTIVE___43_CHARS_BASE64URL_000000000002"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-002"
    E o registo em "refresh_token" com id "0190a6e2-0000-7000-8000-000000000082" tem status "REVOKED"

  @CA-04.6 @RF-04 @RN-04 @EX-04.4 @MUST @critico
  Cenário: Tentativa de refresh com utilizador suspenso revoga o token e retorna 403 ERR-AUTH-004
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" possui status "SUSPENDED"
    Quando é enviado POST "/api/v1/auth/refresh" com {"refresh_token": "RT_ACTIVE___43_CHARS_BASE64URL_000000000002"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-AUTH-004"
    E o registo em "refresh_token" com id "0190a6e2-0000-7000-8000-000000000082" tem status "REVOKED"
```

```gherkin
# language: pt
Funcionalidade: RF-05 — Encerrar sessão ativa no logout

  Contexto:
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" possui um JWT ativo com jti "0190a6e2-0000-7000-8000-000000000555" e fid "0190a6e2-0000-7000-8000-000000000077"
    E que existe 1 registo "ACTIVE" em "refresh_token" para a family_id "0190a6e2-0000-7000-8000-000000000077"

  @CA-05.1 @RF-05 @RN-05 @RN-10 @MUST @critico
  Cenário: Logout adiciona jti à Denylist Redis, revoga a família de refresh tokens e responde 204
    Quando é enviado POST "/api/v1/auth/logout" autenticado com o JWT "0190a6e2-0000-7000-8000-000000000555"
    Então o status da resposta é 204
    E existe a chave Redis "denylist:jti:0190a6e2-0000-7000-8000-000000000555" com valor "1"
    E todos os registos de "refresh_token" com family_id "0190a6e2-0000-7000-8000-000000000077" têm status "REVOKED"
    E existe 1 registo em "auth_audit_log" com event_type "LOGOUT"

  @CA-05.2 @RF-05 @RN-05 @RN-10 @EX-05.1 @MUST @critico
  Cenário: Pedido de logout sem JWT ou com JWT já revogado no Redis retorna 401
    Dado que existe a chave Redis "denylist:jti:0190a6e2-0000-7000-8000-000000000555" com valor "1"
    Quando é enviado POST "/api/v1/auth/logout" autenticado com o JWT "0190a6e2-0000-7000-8000-000000000555"
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-002"
```

```gherkin
# language: pt
Funcionalidade: RF-06 — Emitir token de delegação para suporte via impersonation

  Contexto:
    Dado que existe o analista de suporte "0190a6e2-0000-7000-8000-000000000090" com platform_role "SUPPORT_AGENT" e status "ACTIVE"
    E que existe o utilizador cliente "0190a6e2-0000-7000-8000-000000000001" com platform_role "NONE", status "ACTIVE" e vínculo "ACTIVE" no tenant "0190a6e2-0000-7000-8000-000000000020" para o escopo "BUYER"

  @CA-06.1 @RF-06 @RN-13 @RN-14 @SHOULD @critico
  Cenário: Emissão de token de impersonation RFC 8693 inclui claim act e regista auditoria com ticket_id
    Quando é enviado POST "/api/v1/auth/impersonate" autenticado pelo analista "0190a6e2-0000-7000-8000-000000000090" com {"target_user_id": "0190a6e2-0000-7000-8000-000000000001", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_scope": "BUYER", "ticket_id": "SUP-4821"}
    Então o status da resposta é 200
    E a resposta não contém o campo "refresh_token"
    E o campo "impersonation_context.sub" da resposta é "0190a6e2-0000-7000-8000-000000000001"
    E o campo "impersonation_context.act.sub" da resposta é "0190a6e2-0000-7000-8000-000000000090"
    E o campo "impersonation_context.act.ticket_id" da resposta é "SUP-4821"
    E existe 1 registo em "auth_audit_log" com event_type "IMPERSONATION_STARTED", user_id "0190a6e2-0000-7000-8000-000000000001", actor_user_id "0190a6e2-0000-7000-8000-000000000090" e ticket_id "SUP-4821"

  @CA-06.2 @RF-06 @RN-05 @RN-13 @EX-06.1 @SHOULD @critico
  Cenário: Operador de cliente B2B sem platform_role de suporte é impedido de realizar impersonation com 403
    Quando é enviado POST "/api/v1/auth/impersonate" autenticado pelo utilizador "0190a6e2-0000-7000-8000-000000000001" com {"target_user_id": "0190a6e2-0000-7000-8000-000000000090", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_scope": "BUYER", "ticket_id": "SUP-4821"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-AUTH-010"

  @CA-06.3 @RF-06 @RN-14 @EX-06.2 @SHOULD
  Cenário: Código de chamado ticket_id fora do padrão exigido retorna 422
    Quando é enviado POST "/api/v1/auth/impersonate" autenticado pelo analista "0190a6e2-0000-7000-8000-000000000090" com {"target_user_id": "0190a6e2-0000-7000-8000-000000000001", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_scope": "BUYER", "ticket_id": "chamado_invalido"}
    Então o status da resposta é 422
    E o campo "code" da resposta é "ERR-VAL-004"

  @CA-06.4 @RF-06 @RN-04 @EX-06.3 @SHOULD @critico
  Cenário: Impersonation para utilizador suspenso é recusada com 403 ERR-AUTH-004
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000001" possui status "SUSPENDED"
    Quando é enviado POST "/api/v1/auth/impersonate" autenticado pelo analista "0190a6e2-0000-7000-8000-000000000090" com {"target_user_id": "0190a6e2-0000-7000-8000-000000000001", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_scope": "BUYER", "ticket_id": "SUP-4821"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-AUTH-004"

  @CA-06.5 @RF-06 @RN-06 @RN-07 @RN-08 @EX-06.4 @EX-06.5 @EX-06.6 @EX-06.7 @SHOULD @critico
  Esquema do Cenário: Impersonation falha quando o vínculo, o Tenant, o escopo ou a existência do alvo é inválido
    Dado que a condição do alvo de impersonation é "<condicao_alvo>"
    Quando é enviado POST "/api/v1/auth/impersonate" autenticado pelo analista "0190a6e2-0000-7000-8000-000000000090" com {"target_user_id": "0190a6e2-0000-7000-8000-000000000001", "target_tenant_id": "0190a6e2-0000-7000-8000-000000000020", "target_scope": "BUYER", "ticket_id": "SUP-4821"}
    Então o status da resposta é <status_http>
    E o campo "code" da resposta é "<codigo_erro>"

    Exemplos:
      | condicao_alvo                   | status_http | codigo_erro |
      | alvo_sem_vinculo_no_tenant      | 403         | ERR-CTX-001 |
      | tenant_alvo_suspenso            | 403         | ERR-CTX-002 |
      | escopo_alvo_inativo_ou_sem_role | 403         | ERR-CTX-003 |
      | target_user_ou_tenant_inexiste  | 404         | ERR-CTX-008 |
```

```gherkin
# language: pt
Funcionalidade: RF-07 — Cadastrar Tenant e escopos operacionais

  Contexto:
    Dado que existe o grupo económico "0190a6e2-0000-7000-8000-000000000010" com status "ACTIVE"
    E que o administrador "0190a6e2-0000-7000-8000-000000000001" está autenticado com active_scope "TENANT_ADMIN", permissão "tenant:criar" e group_id "0190a6e2-0000-7000-8000-000000000010"

  @CA-07.1 @RF-07 @RN-15 @RN-16 @RN-18 @MUST
  Cenário: Cadastro de novo Tenant válido persiste Tenant, TenantScope e grava evento empresa_cadastrada na Outbox
    Quando é enviado POST "/api/v1/tenants" autenticado com {"group_id": "0190a6e2-0000-7000-8000-000000000010", "cnpj": "11444777000161", "legal_name": "AgroBrasil Filial Cerrado Ltda", "branch_type": "BRANCH", "geo_lat": -16.686891, "geo_lng": -49.264794, "scopes": ["BUYER", "TENANT_ADMIN"]}
    Então o status da resposta é 201
    E o campo "cnpj" da resposta é "11444777000161"
    E o campo "status" da resposta é "ACTIVE"
    E existem 2 registos em "tenant_scope" para o novo Tenant com scopes "BUYER" e "TENANT_ADMIN"
    E existe 1 registo em "outbox_message" com topic "empresa_cadastrada" e cabeçalho "context_jws" preenchido
    E existe 1 registo em "auth_audit_log" com event_type "TENANT_CREATED"

  @CA-07.2 @RF-07 @RN-05 @RN-18 @EX-07.1 @MUST @critico
  Cenário: Tentativa de criar Tenant para outro grupo económico diferente do AuthContext retorna 403
    Quando é enviado POST "/api/v1/tenants" autenticado com {"group_id": "0190a6e2-0000-7000-8000-000000000099", "cnpj": "11444777000161", "legal_name": "Outra Holding Ltda", "branch_type": "HQ", "geo_lat": -16.686891, "geo_lng": -49.264794, "scopes": ["BUYER"]}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-007"

  @CA-07.3 @RF-07 @RN-15 @EX-07.2 @MUST
  Cenário: Cadastro de Tenant com dígitos verificadores de CNPJ inválidos retorna 422
    Quando é enviado POST "/api/v1/tenants" autenticado com {"group_id": "0190a6e2-0000-7000-8000-000000000010", "cnpj": "11444777000199", "legal_name": "CNPJ Invalido Ltda", "branch_type": "BRANCH", "geo_lat": -16.686891, "geo_lng": -49.264794, "scopes": ["BUYER"]}
    Então o status da resposta é 422
    E o campo "code" da resposta é "ERR-VAL-002"

  @CA-07.4 @RF-07 @RN-16 @EX-07.4 @MUST
  Cenário: Cadastro de Tenant com CNPJ já existente retorna 409 ERR-CTX-005
    Dado que já existe um registo em "tenant" com cnpj "11222333000181"
    Quando é enviado POST "/api/v1/tenants" autenticado com {"group_id": "0190a6e2-0000-7000-8000-000000000010", "cnpj": "11222333000181", "legal_name": "Duplicada Ltda", "branch_type": "BRANCH", "geo_lat": -16.686891, "geo_lng": -49.264794, "scopes": ["BUYER"]}
    Então o status da resposta é 409
    E o campo "code" da resposta é "ERR-CTX-005"

  @CA-07.5 @RF-07 @RN-07 @EX-07.3 @EX-07.5 @MUST
  Esquema do Cenário: Cadastro de Tenant falha quando o grupo económico não existe ou está suspenso
    Dado que o administrador está autenticado com platform_role "PLATFORM_ADMIN"
    E que a condição do grupo económico alvo é "<condicao_grupo>"
    Quando é enviado POST "/api/v1/tenants" autenticado com {"group_id": "<group_id>", "cnpj": "11444777000161", "legal_name": "AgroBrasil Filial Ltda", "branch_type": "BRANCH", "geo_lat": -16.686891, "geo_lng": -49.264794, "scopes": ["BUYER"]}
    Então o status da resposta é <status_http>
    E o campo "code" da resposta é "<codigo_erro>"

    Exemplos:
      | condicao_grupo         | group_id                             | status_http | codigo_erro |
      | grupo_inexistente_bd   | 0190a6e2-0000-7000-8000-000000000999 | 404         | ERR-CTX-008 |
      | grupo_status_suspended | 0190a6e2-0000-7000-8000-000000000010 | 403         | ERR-CTX-002 |
```

```gherkin
# language: pt
Funcionalidade: RF-08 — Vincular membro e papéis a um Tenant

  Contexto:
    Dado que o tenant "0190a6e2-0000-7000-8000-000000000020" possui apenas os escopos "BUYER" e "TENANT_ADMIN" ativos
    E que existe a role "01900000-0000-7000-8000-000000000001" ("BUYER_OPERATOR", escopo "BUYER") e a role "01900000-0000-7000-8000-000000000003" ("CARRIER_DISPATCHER", escopo "CARRIER")
    E que existe o utilizador "0190a6e2-0000-7000-8000-000000000002" sem vínculo no tenant "0190a6e2-0000-7000-8000-000000000020"
    E que o administrador está autenticado em modo SINGLE no tenant "0190a6e2-0000-7000-8000-000000000020" com active_scope "TENANT_ADMIN" e permissão "membro:vincular"

  @CA-08.1 @RF-08 @RN-16 @RN-17 @RN-18 @MUST
  Cenário: Vinculação válida de membro e papel compatível com o escopo ativo do Tenant retorna 201
    Quando é enviado POST "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000020/members" autenticado com {"user_id": "0190a6e2-0000-7000-8000-000000000002", "is_owner": false, "can_view_group": true, "role_ids": ["01900000-0000-7000-8000-000000000001"]}
    Então o status da resposta é 201
    E o campo "user_id" da resposta é "0190a6e2-0000-7000-8000-000000000002"
    E o campo "roles" da resposta contém "BUYER_OPERATOR"
    E existe 1 registo em "tenant_member" para tenant_id "0190a6e2-0000-7000-8000-000000000020" e user_id "0190a6e2-0000-7000-8000-000000000002"
    E existe 1 registo em "auth_audit_log" com event_type "MEMBER_ADDED"

  @CA-08.2 @RF-08 @RN-05 @RN-18 @EX-08.1 @MUST @critico
  Cenário: Tentativa de vincular membro num Tenant fora de AllowedTenants é bloqueada com 403
    Quando é enviado POST "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000099/members" autenticado com {"user_id": "0190a6e2-0000-7000-8000-000000000002", "is_owner": false, "can_view_group": false, "role_ids": ["01900000-0000-7000-8000-000000000001"]}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-007"
    E não existe registo em "tenant_member" para tenant_id "0190a6e2-0000-7000-8000-000000000099" e user_id "0190a6e2-0000-7000-8000-000000000002"

  @CA-08.3 @RF-08 @RN-16 @EX-08.3 @MUST
  Cenário: Tentativa de vincular utilizador já membro do mesmo Tenant retorna 409 ERR-CTX-005
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000002" já possui registo em "tenant_member" no tenant "0190a6e2-0000-7000-8000-000000000020"
    Quando é enviado POST "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000020/members" autenticado com {"user_id": "0190a6e2-0000-7000-8000-000000000002", "is_owner": false, "can_view_group": true, "role_ids": ["01900000-0000-7000-8000-000000000001"]}
    Então o status da resposta é 409
    E o campo "code" da resposta é "ERR-CTX-005"

  @CA-08.4 @RF-08 @RN-17 @EX-08.4 @MUST
  Cenário: Atribuição de Role de escopo CARRIER não habilitado no Tenant retorna 422 ERR-CTX-006
    Quando é enviado POST "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000020/members" autenticado com {"user_id": "0190a6e2-0000-7000-8000-000000000002", "is_owner": false, "can_view_group": false, "role_ids": ["01900000-0000-7000-8000-000000000003"]}
    Então o status da resposta é 422
    E o campo "code" da resposta é "ERR-CTX-006"

  @CA-08.5 @RF-08 @EX-08.2 @MUST
  Cenário: Tentativa de vincular user_id ou role_id inexistente retorna 404 ERR-CTX-008
    Quando é enviado POST "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000020/members" autenticado com {"user_id": "0190a6e2-0000-7000-8000-000000000999", "is_owner": false, "can_view_group": false, "role_ids": ["01900000-0000-7000-8000-000000000001"]}
    Então o status da resposta é 404
    E o campo "code" da resposta é "ERR-CTX-008"
```

```gherkin
# language: pt
Funcionalidade: RF-09 — Suspender ou reativar Tenant com revogação imediata

  Contexto:
    Dado que o tenant "0190a6e2-0000-7000-8000-000000000020" possui status "ACTIVE"
    E que existe um refresh_token "ACTIVE" com active_tenant_id "0190a6e2-0000-7000-8000-000000000020"
    E que o administrador está autenticado com platform_role "PLATFORM_ADMIN"

  @CA-09.1 @RF-09 @RN-07 @RN-18 @MUST @critico
  Cenário: Suspensão de Tenant atualiza base de dados, revoga refresh tokens, grava na Denylist Redis e publica evento
    Quando é enviado PATCH "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000020/status" autenticado com {"status": "SUSPENDED", "reason": "Inadimplência contratual verificada pelo financeiro"}
    Então o status da resposta é 200
    E o campo "status" da resposta é "SUSPENDED"
    E o registo em "tenant" com id "0190a6e2-0000-7000-8000-000000000020" tem status "SUSPENDED"
    E existe a chave Redis "denylist:tenant:0190a6e2-0000-7000-8000-000000000020" com valor "1"
    E todos os registos de "refresh_token" com active_tenant_id "0190a6e2-0000-7000-8000-000000000020" têm status "REVOKED"
    E existe 1 registo em "outbox_message" com topic "tenant_bloqueado" e partition_key "0190a6e2-0000-7000-8000-000000000020"

  @CA-09.2 @RF-09 @RN-05 @RN-18 @EX-09.1 @MUST @critico
  Cenário: Operador sem privilégio de proprietário nem PLATFORM_ADMIN é impedido de suspender Tenant com 403
    Dado que o operador está autenticado no escopo "BUYER" com platform_role "NONE"
    Quando é enviado PATCH "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000020/status" com {"status": "SUSPENDED", "reason": "Tentativa não autorizada"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-007"

  @CA-09.3 @RF-09 @EX-09.2 @MUST
  Cenário: Tentativa de alterar estado de Tenant inexistente retorna 404 ERR-CTX-008
    Quando é enviado PATCH "/api/v1/tenants/0190a6e2-0000-7000-8000-000000000999/status" autenticado com {"status": "SUSPENDED", "reason": "Inadimplência contratual verificada pelo financeiro"}
    Então o status da resposta é 404
    E o campo "code" da resposta é "ERR-CTX-008"
```

```gherkin
# language: pt
Funcionalidade: RF-10 — Suspender ou reativar utilizador com revogação imediata

  Contexto:
    Dado que o utilizador "0190a6e2-0000-7000-8000-000000000002" possui status "ACTIVE" e é membro de um Tenant do grupo "0190a6e2-0000-7000-8000-000000000010"
    E que existe 1 registo "ACTIVE" em "refresh_token" para o utilizador "0190a6e2-0000-7000-8000-000000000002"
    E que o administrador está autenticado com active_scope "TENANT_ADMIN", permissão "usuario:suspender" e group_id "0190a6e2-0000-7000-8000-000000000010"

  @CA-10.1 @RF-10 @RN-04 @RN-18 @MUST @critico
  Cenário: Suspensão de utilizador revoga refresh tokens, grava timestamp na Denylist Redis e publica usuario_bloqueado
    Quando é enviado PATCH "/api/v1/users/0190a6e2-0000-7000-8000-000000000002/status" autenticado com {"status": "SUSPENDED", "reason": "Desligamento do colaborador da empresa"}
    Então o status da resposta é 200
    E o campo "status" da resposta é "SUSPENDED"
    E o registo em "user_account" com id "0190a6e2-0000-7000-8000-000000000002" tem status "SUSPENDED"
    E todos os registos de "refresh_token" do user_id "0190a6e2-0000-7000-8000-000000000002" têm status "REVOKED"
    E existe a chave Redis "denylist:user:0190a6e2-0000-7000-8000-000000000002"
    E existe 1 registo em "outbox_message" com topic "usuario_bloqueado" e partition_key "0190a6e2-0000-7000-8000-000000000002"

  @CA-10.2 @RF-10 @RN-05 @RN-18 @EX-10.1 @MUST @critico
  Cenário: Tentativa de TENANT_ADMIN suspender utilizador de outro grupo económico retorna 403
    Dado que existe o utilizador "0190a6e2-0000-7000-8000-000000000088" pertencente apenas ao grupo "0190a6e2-0000-7000-8000-000000000099"
    Quando é enviado PATCH "/api/v1/users/0190a6e2-0000-7000-8000-000000000088/status" autenticado com {"status": "SUSPENDED", "reason": "Tentativa cruzada entre grupos"}
    Então o status da resposta é 403
    E o campo "code" da resposta é "ERR-CTX-007"

  @CA-10.3 @RF-10 @EX-10.2 @MUST
  Cenário: Tentativa de alterar estado de utilizador inexistente retorna 404 ERR-CTX-008
    Dado que o administrador está autenticado com platform_role "PLATFORM_ADMIN"
    Quando é enviado PATCH "/api/v1/users/0190a6e2-0000-7000-8000-000000000999/status" autenticado com {"status": "SUSPENDED", "reason": "Utilizador inexistente na base"}
    Então o status da resposta é 404
    E o campo "code" da resposta é "ERR-CTX-008"
```

```gherkin
# language: pt
Funcionalidade: RF-11 — Expor conjunto de chaves públicas JWKS

  @CA-11.1 @RF-11 @RN-05 @MUST @critico
  Cenário: Endpoint JWKS retorna chave pública RSA que valida a assinatura RS256 dos tokens emitidos
    Quando é enviado GET "/.well-known/jwks.json"
    Então o status da resposta é 200
    E o campo "keys[0].kty" da resposta é "RSA"
    E o campo "keys[0].alg" da resposta é "RS256"
    E o campo "keys[0].use" da resposta é "sig"
    E a chave pública retornada valida criptograficamente a assinatura RS256 de um "access_token" emitido por EP-01

  @CA-11.2 @RF-11 @RN-05 @MUST @critico
  Cenário: Token JWT assinado com chave RSA diferente da publicada no JWKS é rejeitado com 401
    Quando é enviado POST "/api/v1/auth/logout" com um token Bearer assinado por uma chave RSA externa desconhecida
    Então o status da resposta é 401
    E o campo "code" da resposta é "ERR-AUTH-002"

  @CA-11.3 @RF-11 @EX-11.1 @MUST
  Cenário: Falha interna ao ler material criptográfico no endpoint JWKS retorna 500 ERR-SYS-001 sem expor detalhes internos
    Dado que o provedor de chaves RSA encontra-se indisponível para leitura
    Quando é enviado GET "/.well-known/jwks.json"
    Então o status da resposta é 500
    E o campo "code" da resposta é "ERR-SYS-001"
```

---

## 11. Matriz de Rastreabilidade

| RF | Prioridade | RN | EP / EV | ENT | EX | CA | Teste automatizado |
|---|---|---|---|---|---|---|---|
| RF-01 | MUST | RN-01, RN-02, RN-03, RN-04 | EP-01, EV-03 | ENT-01, ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENT-08, ENT-09, ENT-10 | EX-01.1, EX-01.2, EX-01.3, EX-01.4, EX-01.5 | CA-01.1, CA-01.2, CA-01.3, CA-01.4, CA-01.5, CA-01.6, CA-01.7 | `CA_01_1_login_valido_emite_token_inicial`, `CA_01_2_login_malformado_400`, `CA_01_3_login_formato_invalido_422`, `CA_01_4_login_senha_incorreta_401`, `CA_01_5_login_quinta_falha_bloqueia_423`, `CA_01_6_login_usuario_suspenso_403`, `CA_01_7_login_locked_expirado_reinicia_contagem_401` |
| RF-02 | MUST | RN-04, RN-05, RN-06, RN-07, RN-08, RN-10, RN-18 | EP-02 | ENT-01, ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENT-08, ENT-09 | EX-02.1, EX-02.2, EX-02.3, EX-02.4, EX-02.5, EX-02.6, EX-02.7 | CA-02.1, CA-02.2, CA-02.3, CA-02.4, CA-02.5, CA-02.6, CA-02.7, CA-02.8 | `CA_02_1_switch_context_single_sucesso`, `CA_02_2_switch_context_single_invalido_400`, `CA_02_3_switch_context_jti_denylist_401`, `CA_02_4_switch_context_impersonation_bloqueado_403`, `CA_02_5_switch_context_tenant_nao_membro_403`, `CA_02_6_switch_context_tenant_suspenso_403`, `CA_02_7_switch_context_escopo_inativo_403`, `CA_02_8_switch_context_usuario_denylist_403` |
| RF-03 | MUST | RN-04, RN-05, RN-07, RN-08, RN-09, RN-10, RN-18 | EP-02 | ENT-01, ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENT-08, ENT-09 | EX-03.1, EX-03.2, EX-03.3, EX-03.4, EX-03.5 | CA-03.1, CA-03.2, CA-03.3, CA-03.4, CA-03.5, CA-03.6 | `CA_03_1_switch_context_macro_group_sucesso`, `CA_03_2_switch_context_macro_invalido_400`, `CA_03_3_switch_context_macro_sem_permissao_grupo_403`, `CA_03_4_switch_context_macro_tenant_suspenso_403`, `CA_03_5_switch_context_macro_escopo_inativo_403`, `CA_03_6_switch_context_macro_auth_invalida` |
| RF-04 | MUST | RN-04, RN-07, RN-11, RN-12 | EP-03, EV-04 | ENT-01, ENT-02, ENT-04, ENT-08, ENT-09, ENT-10 | EX-04.1, EX-04.2, EX-04.3, EX-04.4, EX-04.5 | CA-04.1, CA-04.2, CA-04.3, CA-04.4, CA-04.5, CA-04.6 | `CA_04_1_refresh_token_rotacao_sucesso`, `CA_04_2_refresh_token_formato_invalido_400`, `CA_04_3_refresh_reutilizado_revoga_familia_401`, `CA_04_4_refresh_token_inexistente_ou_revogado_401`, `CA_04_5_refresh_token_tenant_suspenso_403`, `CA_04_6_refresh_token_usuario_suspenso_403` |
| RF-05 | MUST | RN-05, RN-10 | EP-04 | ENT-08, ENT-09 | EX-05.1 | CA-05.1, CA-05.2 | `CA_05_1_logout_revoga_jti_e_familia_204`, `CA_05_2_logout_token_ja_revogado_401` |
| RF-06 | SHOULD | RN-04, RN-05, RN-06, RN-07, RN-08, RN-13, RN-14 | EP-05 | ENT-01, ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENT-09 | EX-06.1, EX-06.2, EX-06.3, EX-06.4, EX-06.5, EX-06.6, EX-06.7 | CA-06.1, CA-06.2, CA-06.3, CA-06.4, CA-06.5 | `CA_06_1_impersonate_suporte_emite_token_act`, `CA_06_2_impersonate_nao_autorizado_403`, `CA_06_3_impersonate_ticket_invalido_422`, `CA_06_4_impersonate_alvo_suspenso_403`, `CA_06_5_impersonate_vinculo_tenant_escopo_ou_inexistente` |
| RF-07 | MUST | RN-05, RN-07, RN-15, RN-16, RN-18 | EP-06, EV-01 | ENT-01, ENT-02, ENT-03, ENT-09, ENT-10 | EX-07.1, EX-07.2, EX-07.3, EX-07.4, EX-07.5 | CA-07.1, CA-07.2, CA-07.3, CA-07.4, CA-07.5 | `CA_07_1_criar_tenant_publica_outbox_201`, `CA_07_2_criar_tenant_outro_grupo_403`, `CA_07_3_criar_tenant_cnpj_invalido_422`, `CA_07_4_criar_tenant_cnpj_duplicado_409`, `CA_07_5_criar_tenant_grupo_inexistente_ou_suspenso` |
| RF-08 | MUST | RN-05, RN-16, RN-17, RN-18 | EP-07 | ENT-02, ENT-03, ENT-04, ENT-05, ENT-06, ENT-07, ENT-09 | EX-08.1, EX-08.2, EX-08.3, EX-08.4 | CA-08.1, CA-08.2, CA-08.3, CA-08.4, CA-08.5 | `CA_08_1_vincular_membro_sucesso_201`, `CA_08_2_vincular_membro_isolamento_tenant_403`, `CA_08_3_vincular_membro_duplicado_409`, `CA_08_4_vincular_membro_role_escopo_inativo_422`, `CA_08_5_vincular_membro_recurso_inexistente_404` |
| RF-09 | MUST | RN-05, RN-07, RN-18 | EP-08, EV-02 | ENT-02, ENT-08, ENT-09, ENT-10 | EX-09.1, EX-09.2 | CA-09.1, CA-09.2, CA-09.3 | `CA_09_1_suspender_tenant_denylist_e_outbox_200`, `CA_09_2_suspender_tenant_sem_permissao_403`, `CA_09_3_suspender_tenant_inexistente_404` |
| RF-10 | MUST | RN-04, RN-05, RN-18 | EP-09, EV-03 | ENT-04, ENT-05, ENT-08, ENT-09, ENT-10 | EX-10.1, EX-10.2 | CA-10.1, CA-10.2, CA-10.3 | `CA_10_1_suspender_usuario_denylist_e_outbox_200`, `CA_10_2_suspender_usuario_outro_grupo_403`, `CA_10_3_suspender_usuario_inexistente_404` |
| RF-11 | MUST | RN-05 | EP-10 | ENT-09 | EX-11.1 | CA-11.1, CA-11.2, CA-11.3 | `CA_11_1_jwks_retorna_chave_publica_rsa_200`, `CA_11_2_token_assinatura_externa_rejeitado_401`, `CA_11_3_jwks_erro_interno_chave_500` |

**Totais:** RF = 11 · RN = 18 · EP = 10 · EV = 4 · ENT = 10 · CA = 53 (das quais `@critico` = 34)

---

## 12. Pressupostos, Regras por Omissão e Exclusões

### 12.1 Pressupostos e Decisões de Fecho
| ID | Pressuposto / Decisão | Justificação |
|---|---|---|
| PRS-01 | **Emissão no Login e Token Pré-Contexto (`view_mode = 'NONE'`):** `EP-01` (`POST /api/v1/auth/login`) **NÃO** seleciona arbitrariamente um `Tenant` nem um escopo de negócio; emite um `access_token` inicial com `view_mode = 'NONE'`, `active_scope = null`, `tenant_id = null`, `allowed_tenants = []` e `fid = <family_id>`, juntamente com o `refresh_token` inicial e a lista `available_contexts`. | Elimina a ambiguidade de seleção automática quando um operador pertence a múltiplos CNPJs/escopos e força a troca explícita de contexto (`ADR-04`). Validado e aprovado pelo PO (Matheus). |
| PRS-02 | **Persistência do Contexto Ativo na Família do Refresh Token:** A tabela `refresh_token` (`ENT-08`) persiste o último contexto ativo da sessão (`view_mode`, `group_id`, `active_tenant_id`, `allowed_tenants`, `active_scope`). `EP-02` (`switch-context`) atualiza esse contexto no registo `ACTIVE` da família `fid`, e `EP-03` (`refresh`) preserva e revalida esse contexto na rotação. | Sem persistir o contexto no `refresh_token`, uma renovação em `EP-03` após 15 minutos perderia o contexto selecionado pelo utilizador em `EP-02`. |
| PRS-03 | **Modelação de Roles, Claims e Permissões Granulares (Alinhamento `Ideia Base` vs. `Actuar-Projects`):** Em vez do bitmask hexadecimal (`BitArray PermissionsBit`) acoplado aos módulos de ginásio do `userservice-api`, adota-se RBAC por escopo operacional (`ENT-06 role` com `scope` em `ENUM-03` e array JSONB `permissions` de strings `dominio:acao`). Do `userservice-api` preserva-se a regra de que `tenant_member.is_owner = true` concede automaticamente todas as `Role`s (`role.name`) e todas as `permissions` do catálogo (`§5.5`) para os escopos ativos do `Tenant`, independentemente de linhas em `member_role`. | Cumpre literalmente o formato de claims da `Ideia Base` (§5.2 `"permissions": ["fornecimento:publicar", "leilao:lance"]`), mantendo legibilidade e determinismo entre microsserviços. |
| PRS-04 | **Padrão de Token — JWT Assinado RS256 + JWKS + Denylist Redis (Descarte de Phantom Token):** Conforme verificado no `userservice-api` e `accountservice-api` de `Actuar-Projects` (que emitem JWT assinado diretamente ao cliente sem tradução de token opaco no Gateway) e no `ADR-06`/`ADR-09` da `Ideia Base`, o `Identity-Service` emite JWTs assinados assimetricamente com `RS256` (`kid` no header, chave pública em `EP-10 GET /.well-known/jwks.json`) combinados com revogação em `< 1 ms` via Denylist no Redis. | Evita estado de introspeção obrigatória no API Gateway e permite que qualquer microsserviço B2B valide tanto o `access_token` quanto o `context_jws` (Kafka) localmente. |
| PRS-05 | **Validação Tudo-ou-Nada e Interseção de Permissões em `MACRO_GROUP`:** Em `EP-02` com `view_mode = 'MACRO_GROUP'`, `target_tenants` contém entre 1 e 50 UUIDs distintos. Se qualquer CNPJ falhar em pertencer ao `target_group_id`, estar ativo, ter o `target_scope` ativo ou ter `tenant_member.can_view_group == true` (mesmo que `is_owner == true`), a requisição inteira é rejeitada (403). Os arrays `roles` e `permissions` no JWT emitido correspondem à **interseção** dos conjuntos efetivos detidos pelo utilizador para `target_scope` nos `target_tenants` (podendo `roles` ser `[]` desde que a interseção de `permissions` seja não vazia). | Impede escalação de privilégio horizontal onde uma permissão detida apenas na Filial A seria exercida sobre a Filial B durante a visão macro consolidada. Validado e aprovado pelo PO (Matheus). |
| PRS-06 | **Semântica da Denylist Redis em `SINGLE` e `MACRO_GROUP`:** `denylist:jti:{jti}` guarda `"1"` (`TTL = exp - now`); `denylist:user:{user_id}` guarda `<now_unix>` (`TTL = 900 s`), rejeitando qualquer token com `iat <= valor_redis` tanto para `sub` quanto para `act.sub`; `denylist:tenant:{tenant_id}` guarda `"1"` (`TTL = 86400 s`), sendo consultada via `MGET` para todos os elementos de `allowed_tenants`. | Garante que o bloqueio de um CNPJ no Redis interrompa imediatamente tanto sessões unitárias (`SINGLE`) quanto sessões de visão macro (`MACRO_GROUP`) que incluam esse CNPJ. |
| PRS-07 | **Classificação de Isolamento das Tabelas do Próprio `Identity-Service`:** As tabelas `economic_group`, `tenant`, `user_account`, `role`, `refresh_token`, `auth_audit_log` e `outbox_message` são tabelas globais/transversais e **NÃO** implementam `ITenantScoped`. As tabelas `tenant_scope`, `tenant_member` e `member_role` implementam `ITenantScoped` (filtradas pelas 4 camadas do `AuthContext` nos endpoints de gestão `EP-07`), mas nos fluxos internos de autenticação (`EP-01` a `EP-05`) a transação define `SET LOCAL app.auth_flow = 'true'` e utiliza `IgnoreQueryFilters()` restrito ao `user_id` autenticado. | Resolve o paradoxo de bootstrap: no login e no `switch-context` o sistema **DEVE** consultar e validar vínculos noutros Tenants antes de o novo `AuthContext` existir. |
| PRS-08 | **Regras Estritas de Impersonation (RFC 8693):** A coluna `user_account.platform_role` (`NONE`, `SUPPORT_AGENT`, `PLATFORM_ADMIN`) materializa o Control Plane (`ADR-01`). Apenas `SUPPORT_AGENT` e `PLATFORM_ADMIN` podem chamar `EP-05`, apenas contra alvos com `platform_role == 'NONE'`, exclusivamente em `view_mode = 'SINGLE'`, exigindo `ticket_id` (`^[A-Z]{2,10}-[0-9]{1,10}$`), sem emitir `refresh_token` e proibindo delegação em cadeia ou uso em `EP-02`. | Impede escalação de privilégio no Control Plane, garante rastreabilidade completa (`ActorUserId` + `ticket_id`) e limita a janela temporal da delegação a 900 segundos. Validado e aprovado pelo PO (Matheus). |
| PRS-09 | **Estrutura Canónica de `context_jws` para Eventos Kafka:** Todo evento publicado pelo `Identity-Service` carrega o cabeçalho `context_jws` assinado com `RS256` (mesma chave ativa do JWKS) contendo as claims `{ "event_id", "tenant_id", "group_id", "initiator_user_id", "actor_user_id", "acting_scope", "view_mode", "allowed_tenants", "correlation_id", "iat", "exp" }` com `exp = iat + 300` segundos. | Materializa o `ADR-09` de forma verificável pelos consumidores Kafka através do `KafkaConsumerFilter`. |
| PRS-10 | **Fronteira da Trava de *Self-Dealing* (§8 da `Ideia Base` e análise `Actuar-Projects`):** Nos projetos `userservice-api` e `accountservice-api` (`Actuar-Projects`) não existe trava de *Self-Dealing* (o domínio é de gestão de ginásios). No ecossistema B2B, a aplicação das travas de Compra vs. Fornecimento (recomendada Opção C: `Matching Engine` + `Negociação/Leilão`), restrição por `group_id` (Regra 1/3) e filtragem de cotação de frete pertencem aos microsserviços de domínio a jusante (fora do âmbito do `Identity-Service`). A obrigação do `Identity-Service` é garantir a presença criptográfica de `tenant_id`, `group_id`, `allowed_tenants` e `active_scope` em todos os tokens e eventos. | Mantém a separação de responsabilidades entre o serviço de identidade e os motores de negociação comercial. Validado e aprovado pelo PO (Matheus). |
| PRS-11 | **Normalização de E-mail e CNPJ:** O campo `email` é sempre convertido para minúsculas sem espaços laterais (`trim().ToLowerInvariant()`) antes de qualquer consulta ou persistência. O campo `cnpj` aceita apenas os 14 algarismos numéricos sem pontuação (`^[0-9]{14}$`). | Evita duplicidade lógica de contas por diferença de caixa e elimina variações de formatação de CNPJ. |

### 12.2 Regras por Omissão (aplicam-se quando a especificação é omissa)
| Situação | Comportamento obrigatório |
|---|---|
| Campo de request JSON não listado no contrato do endpoint | Rejeitar o pedido imediatamente com HTTP `400 ERR-VAL-001` (`JsonUnmappedMemberHandling.Disallow`) |
| Campo obrigatório ausente, `null` (quando não permitido) ou tipo JSON incompatível | Rejeitar o pedido com HTTP `400 ERR-VAL-001` |
| String vazia `""` ou apenas espaços em campos com regra semântica (`email`, `password`, `cnpj`, `ticket_id`, `legal_name`, `reason`) vs. `refresh_token` | Para `email`/`password` responder `422 ERR-VAL-003`; para `cnpj` responder `422 ERR-VAL-002`; para `ticket_id` responder `422 ERR-VAL-004`; para `refresh_token` (`EP-03`), `legal_name` (`EP-06`) e `reason` (`EP-08`/`EP-09`) responder `400 ERR-VAL-001` |
| Endpoint chamado com método HTTP não especificado | Responder HTTP `405 Method Not Allowed` |
| Rota HTTP inexistente | Responder HTTP `404 ERR-CTX-008` no formato RFC 9457 Problem Details |
| Utilizador sem permissão explícita para a operação | Recusar por omissão (*deny by default*) com HTTP `403 ERR-CTX-007` |
| Exceção não tratada de base de dados, rede ou runtime | Registar o erro no log estruturado com `correlation_id` e responder HTTP `500 ERR-SYS-001` em formato Problem Details **sem** incluir stack trace nem nomes de tabelas/classes internas |

### 12.3 Explicitamente Proibido
- **NÃO DEVE** registar em logs, métricas, respostas de erro ou tabelas de auditoria palavras-passe em claro (`password`), hashes (`password_hash`, `token_hash`), `refresh_token` em claro, `access_token` completo ou chaves privadas RSA.
- **NÃO DEVE** persistir o `refresh_token` em texto claro na base de dados; apenas o hash SHA-256 (`token_hash`) é armazenado em `ENT-08`.
- **NÃO DEVE** confiar em cabeçalhos HTTP de identidade em texto puro (`X-Tenant-Id`, `X-User-Id`, `X-Group-Id`) para hidratar o `AuthContext`; a hidratação HTTP **DEVE** provir exclusivamente das claims verificadas do JWT `RS256`.
- **NÃO DEVE** emitir `refresh_token` em operações de impersonation (`EP-05`).
- **NÃO DEVE** permitir que um token contendo a claim `act` invoque `EP-02` (`switch-context`) ou `EP-05` (`impersonate`).
- **NÃO DEVE** utilizar algoritmos simétricos (`HS256`) ou chaves RSA inferiores a 2048 bits para assinar `access_token` ou `context_jws`.

---

## 13. Formato de Entrega Esperado da LLM

1. **Árvore de ficheiros** completa e, a seguir, o conteúdo integral de cada ficheiro (sem omissões ou comentários de truncamento).
2. **Migrações** de BD que criam exatamente o modelo da §5 (incluindo constraints `CHECK`, `UNIQUE`, `FOREIGN KEY` com `ON DELETE`, políticas PostgreSQL RLS nas tabelas `ITenantScoped` e o seed da §5.5).
3. **Testes**: um por CA (`CA-01.1` a `CA-11.3`), com o ID no nome do método de teste conforme a coluna "Teste automatizado" da §11.
4. **README** com os comandos exatos para compilar (`dotnet build`), migrar (`dotnet ef database update`), executar (`dotnet run`) e testar (`dotnet test`).
5. **Relatório de entrega** com:
   - tabela `CA → teste → ficheiro`;
   - **Suposições:** lista de decisões tomadas por falta de especificação (o valor esperado é "nenhuma").

---

## 14. Questões em Aberto

Nenhuma.

| ID | Questão | Item afetado | Responsável | Prazo |
|---|---|---|---|---|
| Nenhuma | Não se aplica | Não se aplica | Não se aplica | Não se aplica |

---

## Anexo A — Folha de Auditoria de Conformidade

**Execução:** Não se aplica (preenchido pelo AUD na Fase 5) · **Baseline:** v1.0-baseline · **Modelo:** claude-opus-5-5 · **Data:** Não se aplica · **Medição:** Não se aplica

| Gate | Resultado (Passou/Falhou) | Evidência |
|---|---|---|
| G1 — Compila, arranca, migra | Não se aplica (Fase 5) | Não se aplica |
| G2 — Todos os `@critico` passam | Não se aplica (Fase 5) | Não se aplica |

| CA | RF | Prioridade | Peso $w$ | `@critico` | Resultado $s$ (0/1) | Evidência | Causa se 0 (E/L/T) |
|---|---|---|---|---|---|---|---|
| CA-01.1 a CA-11.3 | RF-01 a RF-11 | MUST (48) / SHOULD (5) | 3 / 2 | Sim (34) / Não (19) | Não se aplica (Fase 5) | Não se aplica | Não se aplica |

| Métrica | Valor |
|---|---|
| $\sum w \cdot s$ / $\sum w$ | Não se aplica / 154 |
| $C_w$ | Não se aplica |
| $C_u$ | Não se aplica |
| $C_{RF}$ | Não se aplica |
| Elementos alucinados $H$ / TA | Não se aplica |
| **Veredito** | Não se aplica (Fase 5) |

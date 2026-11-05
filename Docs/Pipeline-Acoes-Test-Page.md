# Especificação Técnica do Pipeline de Testes, Ações e Cenários Executáveis
## Test & Homologation Playground — `pages-identity.matheus-alves.dev`
### Identity-Service B2B Multi-Tenant (.NET 10, RS256, RLS, Redis Denylist, RFC 9457)

> **Documento Oficial de Engenharia de QA, Testes Automatizados e Homologação**  
> **Serviço Central:** `Identity-Service` (Autoridade de Identidade, Autenticação B2B, Emissão JWT e Governança Multi-Tenant)  
> **Destinatários:** Engenheiros de Microsserviços (Equipes 2, 8, 9 e parceiros de domínio), Engenheiros de QA, Arquitetos de Software e DevOps  
> **Data de Emissão:** 2026-11-05 · **Versão:** 1.0.0 · **Status:** Homologado para Execução  

---

## 1. Visão Geral da Arquitetura & Topologia de Ambientes

O **Identity-Service** é o componente de missão crítica responsável pela autenticação corporativa B2B, emissão e renovação de tokens JWT assimétricos (RS256 com suporte a JWKS), governança multi-tenant e controle de acesso baseado em escopo ativo (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`).

A **Página de Testes e Homologação (`pages-identity.matheus-alves.dev`)** foi concebida como uma aplicação web *developer-first* interativa que permite aos desenvolvedores e analistas de QA:
1. Executar e auditar chamadas a todos os endpoints da API com feedback visual imediato.
2. Inspecionar e validar criptograficamente tokens JWT RS256 contra as chaves públicas expostas em JWKS.
3. Simular alternâncias contextuais entre empresas de um mesmo grupo econômico e escopos distintos sem refazer login.
4. Executar uma **Suíte de Testes Automatizada em 1 Clique (One-Click Test Pipeline)** com medição precisa de latência, asserção de contratos RFC 9457 e exportação de relatórios de conformidade.

```mermaid
flowchart TB
    subgraph ClientLayer["Camada de Testes do Usuário (Navegador)"]
        UI["Página de Testes: pages-identity.matheus-alves.dev"]
        State["Gerenciador de Estado de Sessão\n(tempToken, bearerToken, refreshToken)"]
        Crypto["Validador Criptográfico Local\n(Web Crypto API - RS256 / JWKS)"]
        Runner["Runner Automatizado de Suíte de Testes\n(Orquestrador Sequencial J-01 a J-08)"]
        UI <--> State
        UI <--> Crypto
        UI <--> Runner
    end

    subgraph EnvironmentSelector["Matriz de Ambientes Selecionável"]
        PRD["Produção\nhttps://identity.matheus-alves.dev"]
        STG["Stage / Homologação\nhttps://stage-identity.matheus-alves.dev"]
        DEV["Desenvolvimento\nhttps://dev-identity.matheus-alves.dev"]
        LOC["Localhost\nhttp://localhost:8080"]
    end

    subgraph BackendCore["Identity-Service Core (.NET 10 Web API)"]
        Kestrel["Kestrel HTTP Engine\n(Encoding Latin-1 / Headers RFC)"]
        AuthMiddleware["Auth & Denylist Middleware\n(Redis Fail-Closed)"]
        Modules["Módulos Verticais de Negócio\n(M01 a M14)"]
        PG[(PostgreSQL 17\nFORCE RLS / Schema usuarios)]
        RD[(Redis 7.4.6\nDenylist / Sessions)]
        KF[[Apache Kafka 3.9\nKRaft Event Bus]]
    end

    Runner -->|HTTP/HTTPS REST| EnvironmentSelector
    EnvironmentSelector --> Kestrel
    Kestrel --> AuthMiddleware
    AuthMiddleware --> Modules
    Modules --> PG
    Modules --> RD
    Modules --> KF
```

### 1.1 Matriz de Ambientes e Configuração de Conectividade

A página de testes provê um seletor no cabeçalho (*topbar*) que altera instantaneamente o alvo das requisições e a resolução das chaves JWKS:

| Ambiente | URL Base da API (`baseUrl`) | Endpoint JWKS Público | Propósito & SLA | Política de Dados |
|:---|:---|:---|:---|:---|
| **Produção (`PRD`)** | `https://identity.matheus-alves.dev` | `https://identity.matheus-alves.dev/.well-known/jwks.json` | Ambiente real de produção em VPS Privada com TLS Caddy (Let's Encrypt / ZeroSSL). SLA 99.9%. | Dados reais + Seed Corporativo `AgroBrasil`. Proteção rigorosa contra escrita destrutiva. |
| **Stage (`STG`)** | `https://stage-identity.matheus-alves.dev` | `https://stage-identity.matheus-alves.dev/.well-known/jwks.json` | Homologação pré-release, testes de carga, simulação de falhas e integração com times parceiros. | Espelho sanitizado com seed completo redefinível via rotinas de CI/CD. |
| **Dev (`DEV`)** | `https://dev-identity.matheus-alves.dev` | `https://dev-identity.matheus-alves.dev/.well-known/jwks.json` | Desenvolvimento contínuo, validação de novas features e migrações experimentais. | Base volátil de desenvolvimento diário. |
| **Localhost (`LOC`)** | `http://localhost:8080` | `http://localhost:8080/.well-known/jwks.json` | Desenvolvimento local com Docker Compose (`portal-b2b-kit-infra`) na máquina do desenvolvedor. | Seed local gerado via script SQL (`seed-demo.sql`). |

---

## 2. Catálogo Exaustivo de Ações da API

Cada ação disponível na interface é catalogada com método HTTP, rota, cabeçalhos, corpo da requisição (*payload*), códigos de status esperados e formato da resposta.

```mermaid
classDiagram
    class InfraApi {
        +GET /health/live
        +GET /health/ready
        +GET /.well-known/jwks.json
    }
    class AuthApi {
        +POST /api/v1/auth/login
        +POST /api/v1/auth/switch-context
        +POST /api/v1/auth/refresh
        +POST /api/v1/auth/logout
        +POST /api/v1/auth/impersonate
    }
    class TenantApi {
        +POST /api/v1/tenants
        +PATCH /api/v1/tenants/{id}/status
        +GET /api/v1/tenants/{id}
        +POST /api/v1/tenants/{id}/members
    }
    class UserApi {
        +POST /api/v1/users
        +PATCH /api/v1/users/{id}/status
        +GET /api/v1/users/{id}
    }
    class MockServiceApi {
        +GET /api/v1/produtos
        +POST /api/v1/pedidos
    }
```

---

### 2.1 Camada de Infraestrutura, Observabilidade & Criptografia

#### A01 — `GET /health/live` (Liveness Probe)
- **Objetivo:** Verifica se o processo do runtime .NET Kestrel está ativo e respondendo a requisições de rede.
- **Autenticação:** Nenhuma (Rota pública).
- **Cabeçalhos da Requisição:**
  ```http
  Accept: application/json
  ```
- **Códigos de Resposta:** `200 OK`.
- **Exemplo de Resposta (200 OK):**
  ```json
  {
    "status": "UP"
  }
  ```

#### A02 — `GET /health/ready` (Readiness Probe)
- **Objetivo:** Audita a conectividade com os serviços de suporte (*backing services*): PostgreSQL 17, Redis 7.4 e infraestrutura de mensageria Kafka.
- **Autenticação:** Nenhuma (Rota pública).
- **Cabeçalhos da Requisição:**
  ```http
  Accept: application/json
  ```
- **Códigos de Resposta:** `200 OK` (se todos os componentes estiverem operacionais) ou `503 Service Unavailable` (se houver degradação).
- **Exemplo de Resposta Saudável (200 OK):**
  ```json
  {
    "status": "UP",
    "checks": {
      "database": "UP",
      "redis": "UP"
    }
  }
  ```
- **Exemplo de Resposta Degradada (503 Service Unavailable):**
  ```json
  {
    "status": "DOWN",
    "checks": {
      "database": "UP",
      "redis": "DOWN: Connection refused (127.0.0.1:6379)"
    }
  }
  ```

#### A03 — `GET /.well-known/jwks.json` (Exposição de Chaves Públicas RSA)
- **Objetivo:** Retorna o conjunto de chaves públicas RSA no formato JSON Web Key Set (JWKS), permitindo a validação de assinatura *stateless* e desacoplada em todos os microsserviços.
- **Autenticação:** Nenhuma (Rota pública com cache HTTP).
- **Cabeçalhos de Resposta Mandatórios:**
  ```http
  Content-Type: application/json; charset=utf-8
  Cache-Control: public, max-age=300
  ```
- **Códigos de Resposta:** `200 OK`.
- **Exemplo de Resposta (200 OK):**
  ```json
  {
    "keys": [
      {
        "kty": "RSA",
        "use": "sig",
        "alg": "RS256",
        "kid": "key-2026-01",
        "n": "v9X8a...[módulo RSA em base64url]...",
        "e": "AQAB"
      }
    ]
  }
  ```

---

### 2.2 Camada de Autenticação, Sessão B2B e Controle de Acesso

#### A04 — `POST /api/v1/auth/login` (Autenticação Primária & Descoberta de Vínculos)
- **Objetivo:** Autentica credenciais de e-mail e senha com hashing Argon2id ($m=19456, t=2, p=1$). Retorna um token temporário (`tempToken`) com `view_mode: NONE` e a relação completa de empresas (`available_contexts`) vinculadas ao operador.
- **Autenticação:** Nenhuma.
- **Cabeçalhos da Requisição:**
  ```http
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "email": "admin@agrobrasil.com.br",
    "password": "SenhaForte@2026!"
  }
  ```
- **Códigos de Resposta:**
  - `200 OK`: Autenticação bem-sucedida.
  - `401 Unauthorized`: Credenciais incorretas ou conta bloqueada (`ERR-AUTH-001`).
  - `403 Forbidden`: Usuário suspenso globalmente (`ERR-AUTH-004`).
  - `422 Unprocessable Content`: Formato de e-mail ou campos obrigatórios inválidos (`ERR-VAL-003`).
- **Exemplo de Resposta (200 OK):**
  ```json
  {
    "access_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6ImtleS0yMDI2LTAxIn0...",
    "token_type": "Bearer",
    "expires_in": 900,
    "refresh_token": "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6q7r8s9t0u1v",
    "view_mode": "NONE",
    "available_contexts": [
      {
        "tenant_id": "01900000-0000-7000-8000-000000000010",
        "group_id": "01900000-0000-7000-8000-000000000001",
        "cnpj": "11222333000181",
        "legal_name": "AgroBrasil Matriz e Exportadora S.A.",
        "scopes": ["BUYER", "SUPPLIER", "TENANT_ADMIN"],
        "is_owner": true,
        "can_view_group": true
      }
    ]
  }
  ```

#### A05 — `POST /api/v1/auth/switch-context` (Comutação Dinâmica de Contexto Operacional)
- **Objetivo:** Transiciona a sessão de um estado neutro (`NONE`) ou de outro contexto existente para um contexto operacional específico de tenant e escopo (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`), suportando tanto visão pontual (`SINGLE`) quanto visão holding agregada (`MACRO_GROUP`).
- **Autenticação:** Mandatória (`Authorization: Bearer <tempToken | bearerToken>`).
- **Cabeçalhos da Requisição:**
  ```http
  Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "target_tenant_id": "01900000-0000-7000-8000-000000000010",
    "target_scope": "TENANT_ADMIN",
    "view_mode": "SINGLE"
  }
  ```
- **Códigos de Resposta:**
  - `200 OK`: Contexto alternado com sucesso.
  - `401 Unauthorized`: Token de acesso ausente, inválido ou expirado (`ERR-AUTH-002`).
  - `403 Forbidden`: Vínculo ausente com o tenant (`ERR-CTX-001`), tenant suspenso (`ERR-CTX-002`) ou escopo não atribuído (`ERR-CTX-003`).
- **Exemplo de Resposta (200 OK):**
  ```json
  {
    "access_token": "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6ImtleS0yMDI2LTAxIn0...",
    "token_type": "Bearer",
    "expires_in": 900,
    "refresh_token": "z9y8x7w6v5u4t3s2r1q0p9o8n7m6l5k4j3i2h1g0f1e",
    "tenant_id": "01900000-0000-7000-8000-000000000010",
    "active_scope": "TENANT_ADMIN",
    "view_mode": "SINGLE",
    "roles": ["GROUP_MANAGER"],
    "permissions": ["tenant:criar", "tenant:atualizar", "usuario:suspender", "acesso:auditar"]
  }
  ```

#### A06 — `POST /api/v1/auth/refresh` (Rotação Estrita de Refresh Token - Single-Use)
- **Objetivo:** Emite um novo par de tokens (`access_token` contextualizado + novo `refresh_token`), invalidando imediatamente o refresh token que acabou de ser utilizado (*Single-Use Rotation*).
- **Autenticação:** Nenhuma no header (validação realizada pelo segredo do refresh token).
- **Cabeçalhos da Requisição:**
  ```http
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "refresh_token": "z9y8x7w6v5u4t3s2r1q0p9o8n7m6l5k4j3i2h1g0f1e"
  }
  ```
- **Códigos de Resposta:**
  - `200 OK`: Token rotacionado com sucesso.
  - `401 Unauthorized`: Token inexistente/expirado (`ERR-AUTH-005`) ou **reúso indevido detectado com anulação da família de sessões** (`ERR-AUTH-006`).
  - `403 Forbidden`: Tenant suspenso desde a última emissão (`ERR-CTX-002`) ou usuário suspenso (`ERR-AUTH-004`).
- **Exemplo de Resposta (200 OK):**
  ```json
  {
    "access_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6ImtleS0yMDI2LTAxIn0...",
    "token_type": "Bearer",
    "expires_in": 900,
    "refresh_token": "novo_refresh_token_rotacionado_43_caracteres_123"
  }
  ```

#### A07 — `POST /api/v1/auth/logout` (Encerramento de Sessão & Denylist Redis)
- **Objetivo:** Revoga imediatamente a família de refresh tokens no PostgreSQL e grava o identificador único do JWT (`jti`) na denylist do Redis (`denylist:jti:{jti}`) com TTL igual à expiração do token, garantindo que o token seja sumariamente rejeitado por qualquer microsserviço.
- **Autenticação:** Mandatória (`Authorization: Bearer <bearerToken>`).
- **Cabeçalhos da Requisição:**
  ```http
  Authorization: Bearer eyJhbGciOiJSUzI1NiIs...
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição:** Vazio (`{}` ou 0 bytes).
- **Códigos de Resposta:**
  - `204 No Content`: Sessão encerrada e token adicionado à denylist com sucesso.
  - `401 Unauthorized`: Token ausente ou já invalidado (`ERR-AUTH-002`).

---

### 2.3 Camada de Governança de Tenants & Membros

#### A08 — `POST /api/v1/tenants` (Criação de Organização / Filial)
- **Objetivo:** Registra uma nova unidade operacional (CNPJ) sob um grupo econômico existente, define os escopos operacionais ativos e dispara o evento assíncrono Kafka `empresa_cadastrada` com assinatura JWS de contexto.
- **Autenticação:** Requer `TENANT_ADMIN` com permissão `tenant:criar` ou `PLATFORM_ADMIN`.
- **Cabeçalhos da Requisição:**
  ```http
  Authorization: Bearer <bearerToken>
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "group_id": "01900000-0000-7000-8000-000000000001",
    "cnpj": "99000002000186",
    "legal_name": "AgroBrasil Armazéns Gerais Ltda",
    "branch_type": "BRANCH",
    "geo_lat": -16.686891,
    "geo_lng": -49.264789,
    "scopes": ["BUYER", "SUPPLIER"]
  }
  ```
- **Códigos de Resposta:**
  - `201 Created`: Tenant criado com sucesso. Cabeçalho `Location: /api/v1/tenants/{id}` retornado.
  - `400 Bad Request`: Formato malformado ou campos inválidos (`ERR-VAL-001`).
  - `403 Forbidden`: Permissão insuficiente para cadastrar empresas (`ERR-CTX-007`).
  - `409 Conflict`: CNPJ já cadastrado no sistema (`ERR-CTX-005`).
  - `422 Unprocessable Content`: Dígito verificador de CNPJ inválido (`ERR-VAL-002`).

#### A09 — `PATCH /api/v1/tenants/{id}/status` (Governança de Estado do Tenant)
- **Objetivo:** Altera o status operacional da organização (`ACTIVE` ou `SUSPENDED`). A suspensão invalida todas as sessões ativas do tenant na denylist do Redis (`denylist:tenant:{id}`) e publica o evento Kafka `tenant_bloqueado`.
- **Autenticação:** Requer `TENANT_ADMIN` (proprietário) ou `PLATFORM_ADMIN`.
- **Cabeçalhos da Requisição:**
  ```http
  Authorization: Bearer <bearerToken>
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "status": "SUSPENDED",
    "reason": "Auditoria fiscal em andamento conforme processo administrativo nº 402/2026."
  }
  ```
- **Códigos de Resposta:**
  - `200 OK`: Status atualizado.
  - `403 Forbidden`: Usuário não é proprietário do tenant (`ERR-CTX-007`).
  - `404 Not Found`: Tenant inexistente (`ERR-CTX-008`).

#### A10 — `POST /api/v1/tenants/{id}/members` (Vinculação de Membros e Papéis)
- **Objetivo:** Adiciona um usuário ao tenant e associa seus papéis de negócio (`roles`).
- **Autenticação:** Requer `TENANT_ADMIN` com permissão `tenant:atualizar`.
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "user_id": "01900000-0000-7000-8000-000000000103",
    "roles": ["01900000-0000-7000-8000-000000000001"],
    "is_owner": false,
    "can_view_group": true
  }
  ```
- **Códigos de Resposta:** `201 Created` ou `409 Conflict` (membro já vinculado).

---

### 2.4 Camada de Governança de Usuários

#### A11 — `PATCH /api/v1/users/{id}/status` (Bloqueio / Suspensão de Usuário)
- **Objetivo:** Modifica o estado da conta de um usuário (`ACTIVE`, `SUSPENDED`, `LOCKED`). Suspender um usuário adiciona seu ID à denylist de usuários no Redis (`denylist:user:{id}`), desconectando imediatamente todas as suas sessões ativas no ecossistema e publicando o evento Kafka `usuario_bloqueado`.
- **Autenticação:** Requer permissão `usuario:suspender` em nível de tenant ou `PLATFORM_ADMIN`.
- **Cabeçalhos da Requisição:**
  ```http
  Authorization: Bearer <bearerToken>
  Content-Type: application/json
  X-Correlation-Id: <uuid-v7>
  ```
- **Corpo da Requisição (Payload JSON):**
  ```json
  {
    "status": "SUSPENDED",
    "reason": "Desligamento institucional e revogação preventiva de acessos."
  }
  ```
- **Códigos de Resposta:**
  - `200 OK`: Usuário suspenso com sucesso.
  - `403 Forbidden`: Tentativa de suspender a si próprio ou membro de outro grupo (`ERR-CTX-007`).
  - `404 Not Found`: Usuário não localizado (`ERR-CTX-008`).

---

### 2.5 Validação em Microsserviço de Domínio Simulado

#### A12 — `GET /api/v1/produtos` ou `POST /api/v1/pedidos` (Simulador de Gateway e Microsserviços)
- **Objetivo:** A página de testes inclui um módulo de validação de chamada simulada para microsserviços de negócio (ex: `fornecimentos-service` do Grupo 2 ou `pedidos-service`), permitindo aos desenvolvedores verificar como o Bearer token é aceito e como as claims de `tenant_id` e `active_scope` exercem autorização e isolamento em serviços externos.
- **Mecanismo:** A página pode disparar a chamada para um endpoint mock interno ou para a rota do serviço de domínio configurada:
  ```http
  GET /api/v1/produtos HTTP/1.1
  Host: fornecimentos.matheus-alves.dev
  Authorization: Bearer <bearerToken>
  X-Correlation-Id: <uuid-v7>
  ```
- **Asserções:**
  * O serviço externo baixa o JWKS de `https://identity.matheus-alves.dev/.well-known/jwks.json`.
  * Valida a assinatura RS256 e data de validade (`exp`).
  * Extrai `tenant_id` e `active_scope`. Se o endpoint exige `SUPPLIER` e o token possui `active_scope: "BUYER"`, retorna `403 Forbidden` (`ERR-AUTH-010`). Se possui `SUPPLIER`, retorna `200 OK` com os produtos isolados via RLS para aquele tenant.

---

## 3. Pipeline de Cenários de Teste (Jornadas Interativas J-01 a J-08)

O coração do Test Playground é o seu **Pipeline de Execução Sequencial de Cenários**, cobrindo tanto os caminhos de sucesso corporativo (*Happy Paths*) quanto testes de resiliência, criptografia e mitigação de ataques (*Security & Chaos Tests*).

```mermaid
sequenceDiagram
    autonumber
    actor QA as QA / Desenvolvedor
    participant App as Test Page (Browser)
    participant API as Identity-Service (.NET 10)
    participant Redis as Redis 7.4 (Denylist)
    participant PG as PostgreSQL 17 (RLS)

    Note over QA,PG: J-01: Health & Cryptography Discovery
    QA->>App: Clica em "Executar Descoberta Criptográfica"
    App->>API: GET /health/ready & GET /.well-known/jwks.json
    API-->>App: 200 OK (Componentes UP + Chave RSA kid=key-2026-01)
    App->>App: Decodifica e valida expoente e módulo RSA via Web Crypto API

    Note over QA,PG: J-02: Fluxo Completo de Autenticação B2B
    QA->>App: Seleciona Persona "👑 Administrador"
    App->>API: POST /api/v1/auth/login (admin@agrobrasil.com.br)
    API-->>App: 200 OK (tempToken + available_contexts)
    App->>API: POST /api/v1/auth/switch-context (TENANT_ADMIN, Matriz)
    API-->>App: 200 OK (Bearer Token Contextualizado + Refresh Token)
    App->>App: Renderiza painel de claims decodificadas

    Note over QA,PG: J-03 & J-04: Rotação & Detecção de Fraude de Refresh Token
    App->>API: POST /api/v1/auth/refresh (refreshToken_v1)
    API-->>App: 200 OK (Par novo: Bearer_v2 + refreshToken_v2)
    App->>API: POST /api/v1/auth/refresh (refreshToken_v1 reutilizado!)
    API->>Redis: Invalida família de sessões do usuário
    API-->>App: 401 Unauthorized (ERR-AUTH-006: Reúso Detectado)

    Note over QA,PG: J-05: Troca Dinâmica Cross-Tenant
    App->>API: POST /api/v1/auth/switch-context (Filial, BUYER)
    API-->>App: 200 OK (Novo Bearer com tenant_id da Filial)

    Note over QA,PG: J-06: Logout e Validação de Denylist Redis
    App->>API: POST /api/v1/auth/logout (Bearer atual)
    API->>Redis: SET denylist:jti:{jti} EX 900
    API-->>App: 204 No Content
    App->>API: POST /api/v1/auth/switch-context (Com mesmo Bearer revogado)
    API-->>App: 401 Unauthorized (ERR-AUTH-002: Token Revogado)

    Note over QA,PG: J-07: Proteção contra Força Bruta & Lockout
    loop 5x Requisições com Senha Incorreta
        App->>API: POST /api/v1/auth/login (senha inválida)
        API-->>App: 401 Unauthorized (ERR-AUTH-001)
    end
    API->>Redis: SET denylist:user:{id} (Lockout por 900s)
```

---

### 3.1 J-01: Health & Cryptography Discovery (Descoberta e Inspeção Criptográfica)

- **Objetivo do Teste:** Validar a integridade física do serviço e certificar que a chave pública RSA exposta no JWKS corresponde às normas RS256 exigidas pelo ecossistema.
- **Passo a Passo de Execução:**
  1. O cliente efetua requisição `GET /health/live` e valida status `200 OK` e `status: "UP"`.
  2. O cliente efetua requisição `GET /health/ready` e valida que `database` e `redis` respondem com `UP`.
  3. O cliente efetua requisição `GET /.well-known/jwks.json`.
  4. O script da página realiza a validação estrutural do JWKS:
     - Deve conter a propriedade `keys` (array com pelo menos 1 item).
     - A chave deve possuir `kty: "RSA"`, `use: "sig"`, `alg: "RS256"`.
     - O campo `kid` deve ser preenchido (ex: `key-2026-01`).
     - O expoente público `e` deve ser `AQAB` (representação Base64url de 65537).
     - O módulo `n` deve possuir comprimento correspondente a RSA-2048 (mínimo de 342 caracteres base64url).
- **Critérios de Aceite (Asserções de QA):**
  * `response.status === 200` em todos os endpoints de descoberta.
  * `response.headers.get("Cache-Control")` no JWKS contém diretiva `public, max-age=300`.
  * Validador da Web Crypto API importa a chave pública com sucesso via `crypto.subtle.importKey("jwk", key, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, true, ["verify"])`.

---

### 3.2 J-02: Fluxo Completo de Autenticação B2B (Happy Path)

- **Objetivo do Teste:** Executar o ciclo oficial de login em duas fases (Autenticação Primária $\rightarrow$ Seleção de Contexto Ativo), inspecionando visualmente o payload e as claims do token operacional.
- **Passo a Passo de Execução:**
  1. Usuário seleciona a persona **👑 Administrador AgroBrasil** (`admin@agrobrasil.com.br`).
  2. Dispara `POST /api/v1/auth/login`.
  3. Asserção da Fase 1:
     - Resposta `200 OK`.
     - Guarda `tempToken` e `refreshToken` no `sessionStorage`.
     - Confirma que o `tempToken` decodificado tem claim `view_mode: "NONE"`.
     - Valida que `available_contexts` contém a Matriz AgroBrasil (`11222333000181`) com escopos `["BUYER", "SUPPLIER", "TENANT_ADMIN"]`.
  4. Dispara `POST /api/v1/auth/switch-context` enviando:
     ```json
     {
       "target_tenant_id": "01900000-0000-7000-8000-000000000010",
       "target_scope": "TENANT_ADMIN",
       "view_mode": "SINGLE"
     }
     ```
  5. Asserção da Fase 2:
     - Resposta `200 OK`.
     - Guarda `bearerToken` operacional e atualiza `refreshToken`.
     - O console exibe o inspetor de JWT com as claims validadas:
       * `sub`: UUID do usuário.
       * `tenant_id`: `01900000-0000-7000-8000-000000000010`.
       * `active_scope`: `TENANT_ADMIN`.
       * `roles`: `["GROUP_MANAGER"]`.
       * `permissions`: Lista contendo `tenant:criar`, `usuario:suspender`, etc.
       * `exp - iat`: Exatamente 900 segundos (15 minutos).
- **Critérios de Aceite (Asserções de QA):**
  * Transição de estado de autenticação de `UNAUTHENTICATED` para `NEUTRAL_SESSION` e finalmente `OPERATIONAL_SESSION`.
  * Verificação criptográfica da assinatura do token via Web Crypto API local resulta em `true`.

---

### 3.3 J-03: Rotação de Refresh Token (Single-Use Token Rotation)

- **Objetivo do Teste:** Demonstrar que os refresh tokens do Identity-Service são estritamente de uso único (*single-use*), emitindo novos pares de tokens a cada renovação.
- **Passo a Passo de Execução:**
  1. A partir de uma sessão ativa obtida em J-02, recupera-se o `refreshToken` atual ($RT_1$).
  2. Submete requisição `POST /api/v1/auth/refresh` com payload `{"refresh_token": RT_1}`.
  3. Valida resposta `200 OK`.
  4. Extrai novo token de acesso ($AT_2$) e novo refresh token ($RT_2$).
  5. Atualiza o estado da aplicação com os novos tokens.
- **Critérios de Aceite (Asserções de QA):**
  * `response.status === 200`.
  * O novo refresh token ($RT_2$) é diferente do refresh token original ($RT_1 \ne RT_2$).
  * O novo token de acesso ($AT_2$) possui um novo `jti` e novo timestamp `iat`.

---

### 3.4 J-04: Detecção de Fraude / Reúso de Refresh Token (Security Chaos Test)

- **Objetivo do Teste:** Comprovar a resiliência do Identity-Service contra ataques de replay e furto de sessão: ao tentar submeter novamente um refresh token já consumido ($RT_1$), o serviço detecta o comprometimento, bloqueia a sessão imediatamente e anula em cascata toda a família de tokens.
- **Passo a Passo de Execução:**
  1. Toma como entrada o refresh token $RT_1$ que foi expressamente consumido no cenário J-03.
  2. Dispara requisição `POST /api/v1/auth/refresh` com payload `{"refresh_token": RT_1}`.
  3. O backend intercepta o status `CONSUMED` do token no PostgreSQL, aciona `HandleReuseAsync`, revoga a família no Redis e no banco, emite log de auditoria e evento Kafka `sessao_comprometida`.
  4. A API responde com `HTTP 401 Unauthorized` e código de erro canônico `ERR-AUTH-006`.
  5. Em seguida, a página submete uma requisição de refresh com o token novo $RT_2$ (que pertencia à mesma família).
  6. Comprova que $RT_2$ também é rejeitado com `401 Unauthorized` devido à invalidação em cascata da família comprometida.
- **Critérios de Aceite (Asserções de QA):**
  * `response.status === 401`.
  * `response.json().code === "ERR-AUTH-006"`.
  * `response.json().title` contém `"Reuso de refresh token detetado e sessão revogada"`.
  * Todos os tokens da família passam a retornar erro de sessão inválida.

---

### 3.5 J-05: Troca Dinâmica de Contexto (Cross-Tenant Switching)

- **Objetivo do Teste:** Demonstrar a flexibilidade multi-tenant corporativa: um operador corporativo com vínculos múltiplos (ex: Comprador AgroBrasil) pode alternar entre empresas do grupo e perfis operacionais sem refazer login e sem desconectar da plataforma.
- **Passo a Passo de Execução:**
  1. Realiza login com o usuário **🛒 Comprador AgroBrasil** (`comprador@agrobrasil.com.br`).
  2. O retorno `available_contexts` demonstra vínculos com a Matriz (`01900000-0000-7000-8000-000000000010`) e a Filial Sul (`01900000-0000-7000-8000-000000000020`).
  3. Executa `switch-context` para a Matriz com escopo `BUYER`:
     - Valida `tenant_id` da Matriz e `active_scope: "BUYER"`.
  4. Sem efetuar novo login, executa novo `switch-context` utilizando o Bearer token ativo apontando para a Filial Sul:
     ```json
     {
       "target_tenant_id": "01900000-0000-7000-8000-000000000020",
       "target_scope": "BUYER",
       "view_mode": "SINGLE"
     }
     ```
  5. Valida emissão imediata de novo token com `tenant_id` da Filial Sul.
  6. Opcionalmente, executa `switch-context` com `view_mode: "MACRO_GROUP"`, obtendo visão consolidada de todas as filiais do grupo.
- **Critérios de Aceite (Asserções de QA):**
  * Respostas `200 OK` em todas as alternâncias.
  * Validação no console de que a claim `tenant_id` alternou corretamente entre os UUIDs dos tenants.
  * O `jti` anterior é revogado e substituído com sucesso.

---

### 3.6 J-06: Logout e Validação de Denylist (Blacklist Redis)

- **Objetivo do Teste:** Provar a eficácia da revogação imediata distribuída (*Token Denylist* em Redis): mesmo sendo o JWT um token assimétrico sem estado, após a chamada de logout ele deve ser sumariamente recusado em qualquer chamada subsequente dentro de milissegundos.
- **Passo a Passo de Execução:**
  1. Obtém um token operacional Bearer válido via login e switch-context.
  2. Dispara requisição `POST /api/v1/auth/logout` com cabeçalho `Authorization: Bearer <token>` e corpo vazio.
  3. Valida resposta `204 No Content`.
  4. O backend registra a chave `denylist:jti:{jti}` no Redis com o tempo restante de vida do token.
  5. Imediatamente após (latência de teste $< 100$ms), o cliente tenta invocar um endpoint protegido (ex: `POST /api/v1/auth/switch-context` ou rota de microsserviço) apresentando exatamente o mesmo token que acabou de ser deslogado.
  6. O middleware intercepta a presença do `jti` na denylist do Redis e rejeita sumariamente a requisição com `401 Unauthorized` e código `ERR-AUTH-002` ("Token de acesso ausente, inválido, expirado ou revogado").
- **Critérios de Aceite (Asserções de QA):**
  * Requisição de logout responde estritamente com `204 No Content`.
  * Requisição seguinte com o mesmo token falha com `401 Unauthorized` e `code: "ERR-AUTH-002"`.
  * Tempo de rejeição no Redis inferior a 5ms.

---

### 3.7 J-07: Proteção contra Força Bruta & Lockout de Conta

- **Objetivo do Teste:** Validar as salvaguardas de cibersegurança e integridade de senhas (mitigação de ataques de dicionário e força bruta com Argon2id).
- **Passo a Passo de Execução:**
  1. A página seleciona o usuário de teste e dispara em loop sequencial 5 requisições de login com senha intencionalmente incorreta (`SenhaInvalida@999`).
  2. Da 1ª à 4ª tentativa:
     - O backend incrementa o contador `failed_login_attempts`.
     - Responde com `401 Unauthorized` e código de erro genérico `ERR-AUTH-001` (para prevenir enumeração de usuários).
  3. Na 5ª tentativa consecutiva:
     - O backend atinge o limiar de lockout preventivo.
     - Atualiza o status do usuário para `LOCKED` com `locked_until = now + 900s` (15 minutos de bloqueio).
     - Bloqueia o ID do usuário na denylist distribuída do Redis (`denylist:user:{id}`).
     - Registra log de auditoria `USER_LOCKED` e enfileira evento Kafka `usuario_bloqueado`.
     - Responde com `401 Unauthorized` (`ERR-AUTH-001`).
  4. Uma 6ª requisição de login é submetida (mesmo com a senha correta `SenhaForte@2026!`):
     - A conta está em lockout ativo; o sistema rejeita a autenticação sem sequer reprocessar o hash pesado do Argon2id, preservando os recursos da CPU.
- **Critérios de Aceite (Asserções de QA):**
  * Todas as 5 requisições iniciais retornam `401 Unauthorized`.
  * O estado da conta passa para `LOCKED` e a denylist do Redis protege contra acessos durante a janela de 15 minutos.

---

### 3.8 J-08: Runner Automatizado de Suíte de Testes (One-Click Test Pipeline)

- **Objetivo do Teste:** Executar sequencialmente, sem intervenção humana, toda a suíte de testes J-01 a J-07 diretamente na página, computando estatísticas em tempo real, validando contratos JSON RFC 9457 e gerando uma **Matriz Visual de Resultados** com tempos de latência e exportação de evidências.
- **Estrutura de Execução do Pipeline:**
  1. **Step 1 (Pre-flight):** Verifica ambiente selecionado (`baseUrl`) e pinga `/health/live`.
  2. **Step 2 (Crypto Audit):** Faz fetch do JWKS, valida chaves RSA e inicializa cache de chaves públicas na Web Crypto API.
  3. **Step 3 (Authentication Flow):** Realiza login com Administrador, obtém `tempToken`, seleciona contexto `TENANT_ADMIN` na Matriz e obtém `bearerToken`.
  4. **Step 4 (Token Rotation):** Executa refresh do token e armazena o par rotacionado.
  5. **Step 5 (Fraud & Replay Detection):** Reutiliza o refresh token antigo e valida retorno estrito de `401 ERR-AUTH-006`.
  6. **Step 6 (Dynamic Cross-Tenant):** Autentica com Comprador e comuta contexto da Matriz para a Filial Sul.
  7. **Step 7 (Revocation & Denylist):** Executa logout e valida retorno `401 ERR-AUTH-002` ao reutilizar o Bearer token.
  8. **Step 8 (Summary & Score):** Consolida os resultados, calcula tempo total de execução, latência média por endpoint e emite o parecer final: `PASS (7/7)` ou `FAIL (X/7)`.

```mermaid
flowchart TD
    Start([Início: Clique em 'Executar Suíte Completa']) --> S1[Passo 1: Liveness & Readiness Probes]
    S1 -->|200 OK| S2[Passo 2: Fetch & Validação JWKS RS256]
    S2 -->|RS256 Válido| S3[Passo 3: Login B2B & Troca de Contexto]
    S3 -->|Bearer Emitido| S4[Passo 4: Rotação de Refresh Token]
    S4 -->|Novo Par OK| S5[Passo 5: Simulação de Reúso de Refresh Token]
    S5 -->|401 ERR-AUTH-006| S6[Passo 6: Comutação Cross-Tenant Comprador]
    S6 -->|Contexto Alterado| S7[Passo 7: Logout e Verificação Denylist Redis]
    S7 -->|401 ERR-AUTH-002| S8[Passo 8: Consolidação de Métricas e Relatório]

    S1 -.->|Falha| Fail[Interrupção do Pipeline / Relatório com Erro]
    S2 -.->|Falha| Fail
    S3 -.->|Falha| Fail
    S4 -.->|Falha| Fail
    S5 -.->|Falha| Fail
    S6 -.->|Falha| Fail
    S7 -.->|Falha| Fail

    S8 --> Success([Status Final: PASS / 100% Conforme])
```

---

## 4. Matriz Canônica de Erros HTTP (RFC 9457 Problem Details)

O **Identity-Service** adota estritamente a norma **RFC 9457** (*Problem Details for HTTP APIs*), transmitindo todos os erros com o cabeçalho `Content-Type: application/problem+json` e envelope estruturado contendo exatamente 6 campos canônicos:
- `type`: URI de referência técnica do erro.
- `title`: Descrição resumida da classe de erro.
- `status`: Código numérico de status HTTP (ex: 400, 401, 403, 404, 409, 422, 500, 503).
- `code`: Identificador alfanumérico padronizado da falha para consumo programático.
- `detail`: Descrição detalhada e contextualizada da falha.
- `correlation_id`: Identificador único de correlação (UUID v7 canônico com hífens) para auditoria cruzada em logs e traces.

### 4.1 Tabela Geral de Códigos de Erro do Catálogo Oficial

| Código | Status HTTP | Título Canônico (`title`) | Cenário de Ocorrência |
|:---|:---:|:---|:---|
| **`ERR-VAL-001`** | `400` | Pedido inválido ou malformado | JSON corrompido, campos desconhecidos ou payload vazio quando proibido. |
| **`ERR-VAL-002`** | `422` | Dados de CNPJ ou geolocalização inválidos | CNPJ com tamanho incompatível, dígitos verificadores incorretos ou coordenadas inválidas. |
| **`ERR-VAL-003`** | `422` | Formato de e-mail ou política de palavra-passe violada | E-mail inválido ou senha que não atende aos requisitos mínimos de complexidade. |
| **`ERR-VAL-004`** | `422` | Código de chamado técnico inválido | Chamado de suporte de impersonation fora do padrão de auditoria. |
| **`ERR-REQ-001`** | `404` | Rota inexistente | Endpoint solicitado não mapeado na API. |
| **`ERR-REQ-002`** | `405` | Método não permitido | Verbo HTTP inadequado para o recurso (ex: `GET` em rota exclusivamente `POST`). |
| **`ERR-REQ-003`** | `413` | Corpo do pedido demasiado grande | Payload excedeu o limite máximo configurado no Kestrel. |
| **`ERR-REQ-004`** | `415` | Tipo de conteúdo não suportado | Content-Type diferente de `application/json`. |
| **`ERR-AUTH-001`** | `401` | Credenciais inválidas | Combinação de e-mail e senha incorreta no login ou conta em lockout. |
| **`ERR-AUTH-002`** | `401` | Token de acesso ausente, inválido, expirado ou revogado | Bearer token expirado, corrompido, com assinatura não reconhecida ou presente na denylist do Redis. |
| **`ERR-AUTH-004`** | `403` | Utilizador suspenso ou revogado globalmente | Usuário com status `SUSPENDED` no banco ou na denylist de usuários do Redis. |
| **`ERR-AUTH-005`** | `401` | Refresh token inválido, revogado ou expirado | Refresh token inexistente, fora do formato Regex de 43 caracteres ou com tempo expirado. |
| **`ERR-AUTH-006`** | `401` | Reuso de refresh token detetado e sessão revogada | Tentativa de utilizar um refresh token já consumido. Família inteira é revogada. |
| **`ERR-AUTH-010`** | `403` | Operação de impersonation não autorizada / Escopo inativo | Falha de permissão para assumir sessão ou escopo não autorizado em microsserviço. |
| **`ERR-CTX-001`** | `403` | Utilizador sem vínculo ativo no Tenant solicitado | Tentativa de trocar contexto para uma organização onde o operador não é membro. |
| **`ERR-CTX-002`** | `403` | Tenant ou Grupo Económico suspenso | Tentativa de operar em um tenant com status `SUSPENDED` no banco ou Redis. |
| **`ERR-CTX-003`** | `403` | Escopo operacional inativo ou papel não atribuído | Usuário não tem permissão para atuar no escopo requisitado (`BUYER`, `SUPPLIER`, etc.). |
| **`ERR-CTX-004`** | `403` | Violação das regras de agregação em visão macro | Operador tentou visão `MACRO_GROUP` sem flag `can_view_group` autorizada. |
| **`ERR-CTX-005`** | `409` | Conflito de unicidade de registo | Violação de chave única (ex: CNPJ duplicado ou membro já cadastrado no tenant). |
| **`ERR-CTX-006`** | `422` | Papel incompatível com os escopos ativos do Tenant | Associação de papel com domínio incompatível com a empresa (ex: papel de fornecedor em tenant estritamente comprador). |
| **`ERR-CTX-007`** | `403` | Violação de isolamento de contexto ou permissão insuficiente | Falha de autorização RBAC ou RLS para o recurso solicitado. |
| **`ERR-CTX-008`** | `404` | Recurso de domínio não encontrado | Tenant, grupo econômico ou usuário especificado na URL não existe no banco de dados. |
| **`ERR-SYS-001`** | `500` | Erro interno do servidor | Exceção não tratada no pipeline .NET Kestrel. |
| **`ERR-SYS-002`** | `503` | Dependência indisponível | Falha de comunicação ou timeout ao acessar PostgreSQL, Redis ou Kafka. |

---

### 4.2 Exemplos Canônicos de Respostas RFC 9457

#### Exemplo 1: Falha de Credenciais de Acesso (401 Unauthorized)
```http
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01900000-0000-7000-9000-000000000001

{
  "type": "https://identity.matheus-alves.dev/errors/ERR-AUTH-001",
  "title": "Credenciais inválidas",
  "status": 401,
  "code": "ERR-AUTH-001",
  "detail": "Credenciais inválidas",
  "correlation_id": "01900000-0000-7000-9000-000000000001"
}
```

#### Exemplo 2: Reúso de Refresh Token Detectado (401 Unauthorized)
```http
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01900000-0000-7000-9000-000000000002

{
  "type": "https://identity.matheus-alves.dev/errors/ERR-AUTH-006",
  "title": "Reuso de refresh token detetado e sessão revogada",
  "status": 401,
  "code": "ERR-AUTH-006",
  "detail": "Reuso de refresh token detetado e sessão revogada",
  "correlation_id": "01900000-0000-7000-9000-000000000002"
}
```

#### Exemplo 3: Token Revogado em Denylist Redis (401 Unauthorized)
```http
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01900000-0000-7000-9000-000000000003

{
  "type": "https://identity.matheus-alves.dev/errors/ERR-AUTH-002",
  "title": "Token de acesso ausente, inválido, expirado ou revogado",
  "status": 401,
  "code": "ERR-AUTH-002",
  "detail": "Token de acesso ausente, inválido, expirado ou revogado",
  "correlation_id": "01900000-0000-7000-9000-000000000003"
}
```

#### Exemplo 4: Conflito de CNPJ Duplicado (409 Conflict)
```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json; charset=utf-8
X-Correlation-Id: 01900000-0000-7000-9000-000000000004

{
  "type": "https://identity.matheus-alves.dev/errors/ERR-CTX-005",
  "title": "Conflito de unicidade de registo",
  "status": 409,
  "code": "ERR-CTX-005",
  "detail": "Conflito de unicidade de registo",
  "correlation_id": "01900000-0000-7000-9000-000000000004"
}
```

---

## 5. Personas e Credenciais de Seed Prontas para Uso

Para garantir que desenvolvedores de microsserviços e analistas de QA possam validar instantaneamente todos os papéis de negócio, o banco de dados de desenvolvimento, stage e produção conta com o **Seed Corporativo do Grupo AgroBrasil** provisionado:

> **Senha Padrão para Todas as Personas de Teste:** `SenhaForte@2026!`  
> **Hash Argon2id Cadastrado:** `$argon2id$v=19$m=19456,t=2,p=1$7m2IQEyIfIzFPA0zM1SDWg==$7Z8NHKBD22ZikghTgYpEIHOR2kfHNYnJTr6wniv5InM=`

### 5.1 Matriz de Personas de Teste

| Persona | E-mail / Credencial | Papel Corporativo | Vínculos de Organização (Tenants) | Escopos Permitidos | Capacidades de Teste no Playground |
|:---|:---|:---|:---|:---|:---|
| **👑 Administrador** | `admin@agrobrasil.com.br`<br>`SenhaForte@2026!` | `GROUP_MANAGER` (Proprietário) | **Matriz AgroBrasil**<br>`11222333000181` | `TENANT_ADMIN`, `BUYER`, `SUPPLIER` | Governança completa de empresas, criação de filiais, suspensão de usuários, logout e visualização macro. |
| **🌾 Fornecedor** | `fornecedor@agrobrasil.com.br`<br>`SenhaForte@2026!` | `SUPPLIER_SALES` (Vendedor) | **Matriz AgroBrasil**<br>`11222333000181` | `SUPPLIER` | Emissão de ofertas de grãos/insumos, negociação B2B, validação em microsserviço de fornecimentos. |
| **🛒 Comprador** | `comprador@agrobrasil.com.br`<br>`SenhaForte@2026!` | `BUYER_OPERATOR` (Comprador) | **Matriz AgroBrasil** e<br>**Filial Sul** (`44555666000192`) | `BUYER` | **Teste Multi-Tenant:** comutação dinâmica de contexto entre Matriz e Filial sem deslogar (`switch-context`). |
| **🚚 Transportador** | `transportador@agrobrasil.com.br`<br>`SenhaForte@2026!` | `CARRIER_DISPATCHER` (Despachante) | **AgroLog Transportes**<br>`77888999000103` | `CARRIER` | Emissão de propostas de frete, aceitação de cargas e validação de rotas em serviços de logística. |

### 5.2 Estrutura do Grupo Econômico e Empresas (Tenants)

```mermaid
graph TD
    Holding["Holding: AgroBrasil Participações S.A.<br>UUID: 01900000-0000-7000-8000-000000000001"]
    
    T1["Matriz: AgroBrasil Matriz e Exportadora S.A.<br>CNPJ: 11222333000181 | Tipo: HQ<br>UUID: 01900000-0000-7000-8000-000000000010<br>Escopos: BUYER, SUPPLIER, TENANT_ADMIN"]
    
    T2["Filial: Cooperativa AgroBrasil Sul<br>CNPJ: 44555666000192 | Tipo: BRANCH<br>UUID: 01900000-0000-7000-8000-000000000020<br>Escopos: BUYER"]
    
    T3["Logística: AgroLog Transportes e Cargas Ltda<br>CNPJ: 77888999000103 | Tipo: HQ<br>UUID: 01900000-0000-7000-8000-000000000030<br>Escopos: CARRIER"]
    
    Holding --> T1
    Holding --> T2
    Holding --> T3

    U_Admin["👑 admin@agrobrasil.com.br<br>(GROUP_MANAGER / Owner)"] --> T1
    U_Supplier["🌾 fornecedor@agrobrasil.com.br<br>(SUPPLIER_SALES)"] --> T1
    U_Buyer["🛒 comprador@agrobrasil.com.br<br>(BUYER_OPERATOR)"] --> T1
    U_Buyer --> T2
    U_Carrier["🚚 transportador@agrobrasil.com.br<br>(CARRIER_DISPATCHER)"] --> T3
```

---

## 6. Arquitetura do Runner Automatizado de Suíte de Testes (Frontend Engine)

A execução do pipeline automatizado na página de testes é governada por uma máquina de estados finita (*Finite State Machine - FSM*) implementada em TypeScript/JavaScript no próprio navegador do desenvolvedor.

### 6.1 Máquina de Estados do Runner

```mermaid
stateDiagram-v2
    [*] --> IDLE : Página Carregada
    IDLE --> RUNNING : Clique em 'Run Test Pipeline'
    RUNNING --> STEP_EXEC : Dispara Step (ex: J-01)
    STEP_EXEC --> ASSERTION : Resposta HTTP Recebida
    ASSERTION --> STEP_PASS : Todas as asserções OK
    ASSERTION --> STEP_FAIL : Código HTTP ou Contrato Inválido
    STEP_PASS --> RUNNING : Próximo Step
    STEP_PASS --> COMPLETED : Último Step (J-07) concluído
    STEP_FAIL --> ABORTED : Falha Crítica / Stop-on-Failure
    COMPLETED --> IDLE : Exportar Relatório / Reset
    ABORTED --> IDLE : Reset / Re-executar
```

### 6.2 Estrutura de Asserções e Validação de Contrato

Para cada passo executado, o runner realiza 4 níveis rigorosos de validação:
1. **Status HTTP & Latência:**
   - Verifica se o status code corresponde exatamente ao esperado (ex: `200`, `201`, `204`, `401`).
   - Mede o tempo decorrido via `window.performance.now()`. Alerta visual caso $\Delta t > 300$ms.
2. **Cabeçalho `X-Correlation-Id`:**
   - Assegura que o cabeçalho retornado é um UUID canônico de 36 caracteres com hífens.
3. **Validação de Assinatura Criptográfica RS256:**
   - Para qualquer token JWT emitido (`access_token`), o runner baixa ou utiliza a chave pública em cache do JWKS e executa `crypto.subtle.verify` localmente, atestando que o token foi genuinamente emitido pelo Identity-Service.
4. **Validação de Envelope RFC 9457:**
   - Em caso de respostas com status $\ge 400$, valida a presença dos campos `type`, `title`, `status`, `code`, `detail` e `correlation_id`.

### 6.3 Modelo de Dados da Matriz Visual de Resultados

Os resultados da suíte são exibidos em uma grade visual interativa e consolidados na estrutura JSON padronizada:

```typescript
export interface TestCaseResult {
  id: string;               // Ex: "J-01", "J-02"
  title: string;            // Ex: "Health & Cryptography Discovery"
  endpoint: string;         // Ex: "GET /.well-known/jwks.json"
  httpStatusExpected: number;
  httpStatusReceived: number;
  latencyMs: number;
  contractValid: boolean;
  cryptoValid?: boolean;
  status: "PASS" | "FAIL" | "SKIPPED";
  errorMessage?: string;
  correlationId?: string;
}

export interface TestPipelineExecutionReport {
  environment: "PRD" | "STG" | "DEV" | "LOC";
  baseUrl: string;
  executedAt: string;       // ISO 8601
  totalDurationMs: number;
  passedCount: number;
  failedCount: number;
  results: TestCaseResult[];
}
```

---

## 7. Guia de Integração para Microsserviços Consumidores

Para as equipes que desenvolvem microsserviços de negócio (Grupo 2 - Fornecimentos, Grupo 8 - Negociação, Equipe 9 - DevOps, e demais serviços de Demanda, Logística e Pedidos), a validação de tokens e consumo da API deve seguir as seguintes diretrizes oficiais:

1. **Validação Criptográfica sem Segredo Compartilhado:**  
   Nunca utilize validação simétrica HS256 com senhas compartilhadas (`JWT_SECRET_KEY`). Configure seu serviço para ler as chaves públicas em `https://identity.matheus-alves.dev/.well-known/jwks.json` utilizando bibliotecas consolidadas como `PyJWKClient` (Python), `Microsoft.IdentityModel` (.NET) ou `jwks-rsa` (Node.js/Go).
2. **Extração e Roteamento pelo Contexto Ativo (`active_scope` e `tenant_id`):**  
   Os microsserviços devem autorizar as operações inspecionando a claim `active_scope` (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) e aplicar o isolamento de dados relacional via RLS ou filtros de consulta com base no `tenant_id`.
3. **Respeito à Denylist Distribuída:**  
   Em rotas de altíssima segurança ou transferências financeiras, consulte a denylist em Redis antes de processar transações críticas para garantir que a sessão não foi revogada nos últimos segundos.
4. **Tratamento Universal de Falhas:**  
   Interprete erros HTTP mapeando os envelopes `application/problem+json` da RFC 9457 e sempre registre o cabeçalho `X-Correlation-Id` em logs para rastreamento ponta a ponta.

---

*Especificação técnica homologada para a implantação da Página de Testes do Identity-Service (`pages-identity.matheus-alves.dev`). Documentação mantida pelo time de QA e Arquitetura de Software.*

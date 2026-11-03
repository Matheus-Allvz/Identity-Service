# Diretriz de Alinhamento e Contratos do Ecossistema B2B: Identity-Service e Microsserviços

> **Documento Oficial de Arquitetura e Contratos Inter-Equipes**  
> **Serviço Responsável:** `Identity-Service` (Grupo 1 / Equipe A — Autenticação e Multi-Tenancy)  
> **Destinatários:**  
> - **Equipe 9 (Infraestrutura e DevOps):** `Printf-PedroCardoso/portal-b2b-kit-infra`  
> - **Grupo 2 (Produtos e Fornecimentos):** `davicalvarenga/Sistema-MicServ-SD`  
> - **Grupo 8 (QA e Negociação):** `AmandaBfa/AED---Processo-e-Artefato-SD-`  
> - Demais equipes de microsserviços de domínio (`Demanda`, `Mercado`, `Pedidos`, `Logística`, `Transportadoras`)  
> **Data de Emissão:** 2026-11-02 · **Estado:** Homologado pelo PO (Matheus Alves)

---

## 1. Princípio Diretor de Alinhamento Arquitetural

O Portal de Comércio B2B constitui uma plataforma distribuída de alta criticidade orientada a eventos e microsserviços integrados via API Gateway (tráfego síncrono norte-sul) e Apache Kafka / Redpanda (tráfego assíncrono leste-oeste). Neste ecossistema, o **`Identity-Service`** atua como a autoridade central de identidade, autenticação, controle de acesso e governança de organizações multi-tenant.

A diretriz estabelecida pela liderança técnica e pelo Product Owner (**Matheus Alves**) define o seguinte princípio orientador:

> *"Preservaremos integralmente o modelo arquitetural e os contratos especificados para o `Identity-Service`, solicitando a adaptação correspondente aos projetos parceiros de infraestrutura e serviços de domínio, exceto nos pontos onde a decisão tomada pelo ecossistema configure aperfeiçoamento técnico ou correção justificada."*

### 1.1. Por que o modelo do `Identity-Service` é preservado como o padrão oficial

O modelo arquitetural do `Identity-Service` não representa uma escolha arbitrária, mas a solução robusta para requisitos complexos de comércio B2B que os protótipos iniciais do kit de infraestrutura não cobriam:

1. **Assinatura Assimétrica (RS256) e JWKS (`/.well-known/jwks.json`):**
   O uso de chaves simétricas (HMAC-SHA256) com segredo compartilhado (`JWT_SECRET_KEY`), adotado preliminarmente no `portal-b2b-kit-infra`, viola as premissas fundamentais de segurança em microsserviços. Como apontado pelo próprio Grupo 2 no seu **ADR-012**, qualquer serviço em posse do segredo simétrico pode forjar tokens JWT arbitrários com privilégios de qualquer empresa ou usuário. A criptografia assimétrica RSA-2048 (RS256) com publicação de chave pública via JWKS elimina o compartilhamento de segredos de escrita, permitindo validação stateless irrefutável em todos os nós da rede.

2. **Hierarquia Multi-CNPJ e Modelagem de Grupo Econômico:**
   Grandes participantes do agronegócio e atacado operam sob holdings e grupos econômicos que controlam múltiplas filiais e subsidiárias com CNPJs distintos. O `Identity-Service` modela essa realidade através da hierarquia `economic_group` $\rightarrow$ `tenant` (CNPJ) $\rightarrow$ `tenant_scope`, permitindo gestão consolidada sem duplicação de cadastros de operadores.

3. **Resolução da Pendência P-31 / Issue #13 (Múltiplos Perfis Simultâneos):**
   O Grupo 2 formalizou a **Pendência P-31** (`docs/relatorio-tecnico.md`), alertando que no banco de dados do kit de infraestrutura as tabelas `empresa` e `usuario` suportavam apenas um único `tipo` e um único `perfil` (`comprador`, `fornecedor` ou `transportadora`). Na operação real do Portal B2B, uma cooperativa agrícola ou grande distribuidor atua **simultaneamente como compradora de insumos, fornecedora de grãos e prestadora de frete**. O `Identity-Service` resolve essa pendência estrutural através de múltiplos registros em `tenant_scope` e da mecânica de **troca explícita de contexto (`POST /api/v1/auth/switch-context`)**, onde o usuário seleciona em qual escopo operacional ativo (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`) deseja emitir ordens.

4. **Isolamento de Dados em 4 Camadas e PostgreSQL RLS:**
   O `Identity-Service` garante que o acesso a dados multi-tenant seja restringido não apenas na aplicação, mas diretamente no SGBD PostgreSQL através de *Row-Level Security* (`FORCE ROW LEVEL SECURITY`) executado sob a role restrita `identity_app`, prevenindo vazamentos lógicos entre concorrentes de mercado.

5. **Revogação Imediata via Denylist em Redis:**
   O desacoplamento *stateless* do JWT é complementado por uma denylist distribuída em Redis (`denylist:jti`, `denylist:user`, `denylist:tenant`), garantindo que suspensões judiciais de CNPJs ou comprometimentos de credenciais encerrem o acesso imediatamente em todos os serviços consumidores sem sobrecarregar a base relacional.

6. **Resolução da Pendência P-12 / Issue #2 (Contrato do Evento `empresa_cadastrada` e `context_jws`):**
   O Grupo 2 e os serviços consumidores necessitam de dados completos da empresa para suas projeções locais (`empresas_conhecidas`). O `Identity-Service` formaliza o payload de `EV-01: empresa_cadastrada` e propaga nos cabeçalhos Kafka a assinatura criptográfica `context_jws`, assegurando proveniência e integridade leste-oeste auditável.

---

## 2. Matriz de Decisões Técnicas sobre as 12 Divergências do Ecossistema

A tabela a seguir consolida o posicionamento oficial para cada uma das 12 divergências identificadas entre o `Identity-Service` e o ecossistema (`portal-b2b-kit-infra`, `Sistema-MicServ-SD` e `AED---Processo-e-Artefato-SD-`):

| ID | Área Técnica | Especificação `Identity-Service` | Padrão Inicial do Ecossistema | Decisão Arquitetural | Ação Requerida das Equipes |
|---|---|---|---|---|---|
| **D-ECO-01** | Roteamento no API Gateway | Rotas `/api/v1/auth/*`, `/api/v1/tenants/*`, `/api/v1/users/*`, `/.well-known/jwks.json` na porta 8080. | Nginx só roteava `/api/v1/usuarios/*` para `app1/2-usuarios:8101`. | **Preservar Identity + Solicitar Nginx.** O Gateway DEVE rotear as URLs padronizadas do Identity. | **Equipe 9:** Atualizar `gateway/common_routes.conf` e `nginx.conf`. |
| **D-ECO-02** | Cache e Denylist (Redis) | Dependência mandatória de Redis 7.4.6 para revogação imediata (*fail-closed*). | Sem container Redis no `docker-compose.yml` da infraestrutura. | **Preservar Identity + Solicitar Container.** O ecossistema adota Redis para revogação distribuída. | **Equipe 9:** Adicionar container `redis:7.4.6-alpine` ao Compose oficial. |
| **D-ECO-03** | Algoritmo de Assinatura JWT | Assinatura assimétrica RS256 com exposição de chaves via JWKS. | Segredo simétrico compartilhado HS256 (`JWT_SECRET_KEY`). | **Preservar Identity + Fornecer Validador.** Elimina o risco de forja de tokens apontado no ADR-012 do Grupo 2. | **Equipes 2, 8 e 9:** Atualizar `services/common/auth.py` com snippet JWKS fornecido. |
| **D-ECO-04** | Claims JWT e Multi-Perfil (P-31) | Claims `sub`, `tenant_id`, `group_id`, `active_scope`, `view_mode`, `roles`, `permissions`, `act`. | Claims `sub`, `empresaId`, `perfil`, `type`. Apenas 1 perfil fixo. | **Preservar Identity + Mapear Equivalências.** O ecossistema adota o fluxo de `switch-context` e consome as claims oficiais. | **Grupos 2 e 8:** Mapear claims do token no middleware de autorização dos serviços. |
| **D-ECO-05** | Eventos Kafka e `context_jws` (P-12) | 4 tópicos (`empresa_cadastrada`, `tenant_bloqueado`, `usuario_bloqueado`, `sessao_comprometida`) com `context_jws` em header. | Somente `empresa_cadastrada` provisionado no `init_topics.sh`. Payload sem perfis múltiplos. | **Preservar Identity + Provisionar Tópicos.** Formalizar o contrato de `empresa_cadastrada` e criar tópicos de segurança. | **Equipe 9:** Atualizar `init_topics.sh`. **Grupo 2:** Consumir payload rico de `empresa_cadastrada`. |
| **D-ECO-06** | Banco de Dados e Bug K1 | PostgreSQL com schema gerenciado pelo Identity, role `identity_app` e RLS ativa. | `01-init.sql` com bug K1 (`GRANT` antes de `CREATE TABLE`) e permissão negada. | **Preservar Identity + Orientar Correção K1.** O Identity aplica suas migrações e instrui o fix no kit. | **Equipe 9:** Corrigir `01-init.sql` com `ALTER DEFAULT PRIVILEGES`. |
| **D-ECO-07** | Formato de Erro HTTP | RFC 9457 com 6 campos canônicos (`type`, `title`, `status`, `code`, `detail`, `correlation_id`). | RFC 7807 heterogênea (`codigo`, `correlationId`, `timestamp`). | **Preservar RFC 9457.** RFC 9457 é a evolução oficial da RFC 7807 (IETF standard). | **Todas as equipes:** Alinhar tratamento de exceções em `application/problem+json`. |
| **D-ECO-08** | Formato do `X-Correlation-Id` | Exige formato UUID canônico de 36 caracteres com hífens (`Guid.TryParseExact(val, "D")`). | Nginx gerava `$request_id` (32 caracteres hexadecimais sem hífens). | **Solicitar Ajuste no Gateway / Clientes.** Garantir envio de UUID formatado para preservar rastreabilidade. | **Equipe 9:** Configurar Nginx para preservar header enviado ou gerar UUID com hífens. |
| **D-ECO-09** | Endpoints de Identidade vs CRUD | Foco em Autenticação, Contexto, Governança de Tenants e Membros. | Boilerplate com endpoints CRUD básicos `/api/v1/usuarios/usuarios`. | **Preservar Identity.** O gerenciamento de membros ocorre via `POST /api/v1/tenants/{id}/members`. | **Frontend / QA:** Adequar jornadas de teste ao fluxo corporativo do Identity. |
| **D-ECO-10** | Endpoints de Observabilidade | Núcleo focado no domínio; métricas e readiness expostas na camada de hosting. | Endpoints `/health`, `/health/ready`, `/metrics` exigidos no container. | **Melhoria Incorporada.** O Identity expõe endpoints operacionais no pipeline de hosting. | **Equipe 1 (Identity):** Garantir `GET /health` e `/metrics` no runtime de implantação. |
| **D-ECO-11** | Pool de Conexões no PostgreSQL | Npgsql com connection pooling configurável. | Limite estrito de 5 conexões por réplica (cota global de 100). | **Melhoria Incorporada.** Fixar `Maximum Pool Size=5` na string de conexão do serviço. | **Equipe 1 (Identity):** Configurar `Maximum Pool Size=5` no perfil de produção. |
| **D-ECO-12** | Modelo de Dados Relacional | 10 tabelas relacionais (`UUIDv7`, integridade referencial, histórico). | 4 tabelas simples com chave `VARCHAR(64)` no schema `usuarios`. | **Preservar Identity.** Isolamento *Schema-per-Service* garante autonomia das tabelas internas. | **Equipe 9:** Assegurar que o schema `usuarios` seja de posse exclusiva do Identity. |

---

## 3. Especificação Detalhada das Solicitações e Contratos Inter-Equipes

### 3.1. D-ECO-01: Rotas e Roteamento no Nginx Gateway (`portal-b2b-kit-infra`)

O API Gateway oficial Nginx atualmente intercepta apenas `/api/v1/usuarios/*`. Para habilitar a comunicação dos frontends e testes com a autoridade de autenticação, a **Equipe 9** deve aplicar o seguinte ajuste nos arquivos de configuração do Gateway:

#### A. Ajuste em `gateway/nginx.conf`
Definir o bloco upstream apontando para as réplicas do `Identity-Service` (porta padrão `8080`):

```nginx
upstream usuarios_backend {
    server app1-usuarios:8080 max_fails=3 fail_timeout=10s;
    server app2-usuarios:8080 max_fails=3 fail_timeout=10s;
    keepalive 32;
}
```

#### B. Ajuste em `gateway/common_routes.conf`
Adicionar os blocos de proxy para as rotas canônicas do serviço:

```nginx
# ==============================================================================
# Roteamento Oficial: Identity-Service (Autenticação, Governança e JWKS)
# ==============================================================================

# 1. Autenticação, Sessão, Troca de Contexto e Logout
location /api/v1/auth/ {
    proxy_pass http://usuarios_backend;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Correlation-Id $correlation_id;
    proxy_set_header Authorization $http_authorization;
}

# 2. Governança de Tenants e Membros
location /api/v1/tenants {
    proxy_pass http://usuarios_backend;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Correlation-Id $correlation_id;
    proxy_set_header Authorization $http_authorization;
}

# 3. Governança de Estado de Usuários
location /api/v1/users/ {
    proxy_pass http://usuarios_backend;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Correlation-Id $correlation_id;
    proxy_set_header Authorization $http_authorization;
}

# 4. Exposição de Chaves Públicas para Validação Stateless (JWKS)
location = /.well-known/jwks.json {
    proxy_pass http://usuarios_backend;
    proxy_http_version 1.1;
    proxy_set_header Connection "";
    proxy_set_header Host $host;
    proxy_set_header X-Correlation-Id $correlation_id;
    proxy_cache_valid 200 1h;
}
```

---

### 3.2. D-ECO-02: Container Redis no `docker-compose.yml` (`portal-b2b-kit-infra`)

Para suportar o mecanismo de revogação imediata de tokens (*Token Denylist*) e bloqueio distribuído de sessões, a **Equipe 9** deve incluir o serviço Redis no `docker-compose.yml` principal:

```yaml
  redis:
    image: redis:7.4.6-alpine
    container_name: portal-b2b-redis
    restart: unless-stopped
    ports:
      - "6379:6379"
    command: >
      redis-server 
      --appendonly yes 
      --maxmemory 256mb 
      --maxmemory-policy volatile-lru
    volumes:
      - redis_data:/data
    networks:
      - portal-b2b-network
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 5s
      timeout: 3s
      retries: 5
      start_period: 5s

volumes:
  redis_data:
    driver: local
```

*Configuração das Aplicações:* Disponibilizar para o `Identity-Service` e demais serviços a variável de ambiente:
```env
REDIS_CONNECTION_STRING=redis:6379
```

---

### 3.3. D-ECO-03: Validação de Tokens JWT via RS256 e JWKS

Para erradicar a vulnerabilidade de tokens forjados (**ADR-012** do Grupo 2), todos os microsserviços do ecossistema devem validar o JWT utilizando a chave pública disponibilizada em `GET /.well-known/jwks.json`.

Abaixo apresenta-se a implementação em Python com `PyJWKClient` / `PyJWT`, pronta para ser incorporada ao arquivo `services/common/auth.py` do boilerplate oficial:

```python
"""
Módulo de Validação Criptográfica de Tokens JWT (RS256 + JWKS)
Substitui a validação simétrica HS256 por verificação assimétrica oficial.
Compatível com portal-b2b-kit-infra e serviços de domínio (Python 3.12 / FastAPI).
"""

import os
import logging
from typing import Dict, Any, List, Optional
import jwt
from jwt import PyJWKClient, PyJWKClientError, InvalidTokenError
from fastapi import HTTPException, Security, status
from fastapi.security import HTTPBearer, HTTPAuthorizationCredentials

logger = logging.getLogger("auth_middleware")

# Endereço interno do JWKS via Gateway ou direto no serviço de identidade
JWKS_URL = os.getenv("AUTH_JWKS_URL", "http://gateway:8080/.well-known/jwks.json")

# Cliente PyJWK com cache local automático de chaves públicas
jwks_client = PyJWKClient(
    JWKS_URL,
    cache_keys=True,
    max_cached_keys=10,
    cache_jwk_set=True,
    lifespan=3600  # 1 hora de retenção em cache local
)

security = HTTPBearer()

def get_current_token_payload(credentials: HTTPAuthorizationCredentials = Security(security)) -> Dict[str, Any]:
    """
    Decodifica e valida o Access Token RS256 extraído do cabeçalho Authorization: Bearer.
    Lança HTTPException 401 caso o token seja inválido, esteja expirado ou tenha sido adulterado.
    """
    token = credentials.credentials
    try:
        # Recupera a chave pública RSA adequada com base no header 'kid' do token
        signing_key = jwks_client.get_signing_key_from_jwt(token)
        
        payload = jwt.decode(
            token,
            signing_key.key,
            algorithms=["RS256"],
            options={
                "verify_signature": True,
                "verify_exp": True,
                "verify_iat": True,
                "require": ["exp", "iat", "sub", "tenant_id", "active_scope"]
            }
        )
        return payload

    except jwt.ExpiredSignatureError:
        logger.warning("Token de acesso expirado.")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={"code": "ERR-AUTH-005", "message": "Token de acesso expirado."},
            headers={"WWW-Authenticate": "Bearer error=\"invalid_token\", error_description=\"Token has expired\""}
        )
    except (PyJWKClientError, InvalidTokenError) as err:
        logger.error(f"Falha na validação do token JWT: {str(err)}")
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail={"code": "ERR-AUTH-001", "message": "Token inválido ou assinatura não reconhecida."},
            headers={"WWW-Authenticate": "Bearer error=\"invalid_token\""}
        )

def require_scope(allowed_scopes: List[str]):
    """
    Dependency do FastAPI para garantir que a requisição opera no escopo exigido.
    Exemplo: @app.post("/fornecimentos", dependencies=[Depends(require_scope(["SUPPLIER"]))])
    """
    def scope_checker(payload: Dict[str, Any] = Security(get_current_token_payload)):
        active_scope = payload.get("active_scope")
        if active_scope not in allowed_scopes:
            logger.warning(f"Acesso negado: escopo ativo '{active_scope}' não está em {allowed_scopes}")
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail={"code": "ERR-AUTH-010", "message": f"Escopo ativo '{active_scope}' não autorizado para este recurso."}
            )
        return payload
    return scope_checker
```

---

### 3.4. D-ECO-04: Estrutura de Claims JWT e Fluxo Multi-Escopo (Resolução P-31)

Para suportar empresas com múltiplos perfis simultâneos sem inconsistências de autorização, o `Identity-Service` adota a autenticação orientada a contexto ativo:

#### A. Tabela de Mapeamento de Claims para os Consumidores

| Claim JWT | Tipo | Significado no `Identity-Service` | Equivalente nos Consumidores | Observações para os Desenvolvedores |
|---|---|---|---|---|
| `sub` | `string (UUID)` | ID do Usuário Autenticado | `usuario_id` / `sub` | Identificador único do indivíduo no sistema. |
| `tenant_id` | `string (UUID)` | ID do Tenant (CNPJ ativo) | `empresaId` / `empresa_id` | **Atenção:** Consumidores que liam `empresaId` devem mapear a partir de `tenant_id`. |
| `group_id` | `string (UUID)` | ID do Grupo Econômico | `grupo_economico_id` | Identifica a holding/matriz à qual o CNPJ pertence. |
| `active_scope` | `string` | Escopo Operacional Ativo | `perfil` | Valores padronizados: `BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`. |
| `view_mode` | `string` | Modo de Visão Operacional | — | `SINGLE` (opera sobre 1 CNPJ) ou `MACRO_GROUP` (visão consolidada de holding). |
| `allowed_tenants` | `array[UUID]` | Tenants aos quais o token tem acesso | `empresas_autorizadas` | No modo `SINGLE`, contém `[tenant_id]`. No modo `MACRO_GROUP`, contém os CNPJs do grupo. |
| `roles` | `array[string]` | Papéis de negócio no escopo ativo | `perfis_detalhados` | Papéis funcionais (ex.: `COMMERCIAL_OFFICER`, `PURCHASING_ANALYST`). |
| `permissions` | `array[string]` | Permissões granulares | `permissoes` | Códigos canônicos no formato `<dominio>:<recurso>:<acao>` (ex.: `supply:offer:write`). |
| `act` | `object` | Contexto de Impersonation (RFC 8693) | — | Presente apenas em sessões de suporte/auditoria (`act.sub` = agente de suporte). |

#### B. Mecânica da Troca de Contexto (`POST /api/v1/auth/switch-context`)

1. **Passo 1 (Login Inicial):**  
   Ao chamar `POST /api/v1/auth/login`, o usuário autentica suas credenciais. O token inicial é emitido com `view_mode: "NONE"`, contendo na resposta a lista de todos os grupos econômicos, CNPJs e escopos autorizados para aquele operador. Esse token inicial **não possui permissão de escrita em microsserviços de negócio**, protegendo contra operações acidentais no CNPJ incorreto.
2. **Passo 2 (Seleção de CNPJ e Escopo Ativo):**  
   O frontend apresenta ao usuário a seleção de empresa e perfil. A aplicação chama `POST /api/v1/auth/switch-context` enviando:
   ```json
   {
     "target_tenant_id": "0194eb82-7000-7000-a000-000000000001",
     "target_scope": "SUPPLIER",
     "view_mode": "SINGLE"
   }
   ```
   O serviço valida se o usuário possui vínculo ativo com aquele CNPJ para o escopo `SUPPLIER`, revoga o `jti` anterior e emite um novo token pronto para uso contendo `tenant_id` e `active_scope: "SUPPLIER"`.
3. **Alternância Dinâmica:**  
   Se o mesmo usuário precisar emitir um lance de frete ou comprar insumos pela mesma empresa, basta invocar novamente o `switch-context` alternando para `CARRIER` ou `BUYER`. Não há necessidade de deslogar ou manter cadastros duplicados.

---

### 3.5. D-ECO-05: Contrato de Eventos Kafka e Assinatura `context_jws` (Resolução P-12)

#### A. Provisionamento de Tópicos em `scripts/init_topics.sh` (`portal-b2b-kit-infra`)
A Equipe 9 deve adicionar a criação dos 4 tópicos de governança emitidos pelo `Identity-Service`:

```bash
# ==============================================================================
# Tópicos de Identidade, Governança e Segurança (Identity-Service)
# ==============================================================================
rpk topic create empresa_cadastrada -p 3 -r 3
rpk topic create tenant_bloqueado -p 3 -r 3
rpk topic create usuario_bloqueado -p 3 -r 3
rpk topic create sessao_comprometida -p 3 -r 3
```

#### B. Contrato Canônico de `EV-01: empresa_cadastrada` (Resolução da Pendência P-12 do Grupo 2)
Quando uma nova empresa/filial é cadastrada via `POST /api/v1/tenants`, o evento publicado em `empresa_cadastrada` atende integralmente à demanda de dados do `fornecimentos-service` e `demanda-service`:

**Chave da Mensagem (Kafka Key):** String do `tenant_id` (ex.: `"0194eb82-7000-7000-a000-000000000001"`).

**Corpo da Mensagem (Payload JSON):**
```json
{
  "event_id": "0194eb82-7000-7000-b000-000000000001",
  "tenant_id": "0194eb82-7000-7000-a000-000000000001",
  "group_id": "0194eb82-7000-7000-9000-000000000001",
  "cnpj": "11222333000181",
  "legal_name": "AgroBrasil Insumos e Fornecimentos S.A.",
  "branch_type": "HEADQUARTERS",
  "geo_lat": -12.971400,
  "geo_lng": -38.501400,
  "status": "ACTIVE",
  "scopes": ["BUYER", "SUPPLIER", "TENANT_ADMIN"],
  "created_at": "2026-11-02T14:30:00Z"
}
```

**Cabeçalhos da Mensagem (Kafka Headers):**
- `event_id`: Identificador único do evento (UUID).
- `tenant_id`: ID da empresa/tenant criada.
- `group_id`: ID do grupo econômico.
- `initiator_user_id`: ID do usuário que solicitou o cadastro.
- `actor_user_id`: ID do operador que realizou a ação.
- `acting_scope`: Escopo em que a ação ocorreu (`TENANT_ADMIN` ou `PLATFORM_ADMIN`).
- `correlation_id`: Identificador de correlação ponta a ponta da transação HTTP original.
- `context_jws`: **Assinatura criptográfica JWS compacta (RS256)** gerada pelo `Identity-Service`, contendo os claims de contexto do evento. Os consumidores podem validar o `context_jws` com a chave pública do JWKS para garantir que a mensagem foi genuinamente emitida pelo serviço de identidade e não foi adulterada no barramento.

---

### 3.6. D-ECO-06: Isolamento de Banco de Dados, Role `identity_app` e Correção do Bug K1

O `Identity-Service` opera em conformidade com o padrão *Schema-per-Service* do ecossistema, isolando suas tabelas no PostgreSQL.

#### A. Modelo de Execução com Role `identity_app` e RLS
Ao inicializar a conexão no pool, a aplicação executa `SET ROLE identity_app` e injeta via `set_config`:
- `app.allowed_tenants`: Lista de tenants permitidos para leitura.
- `app.write_tenants`: Lista de tenants permitidos para inserção/atualização.

As políticas `FORCE ROW LEVEL SECURITY` ativas no PostgreSQL impedem que requisições malformadas ou falhas na aplicação acessem dados de outras organizações.

#### B. Orientação de Correção do Bug K1 no `01-init.sql` do Kit (`portal-b2b-kit-infra`)
O Grupo 2 relatou no achado **K1 / P-32** que os containers de serviço recebiam `permission denied` ao conectar no PostgreSQL. A causa raiz foi identificada no `database/01-init.sql` do kit de infraestrutura: os privilégios foram concedidos com `GRANT ALL ON ALL TABLES` **antes** que as tabelas fossem criadas pela role `portal_ddl`.

Para sanar este problema em todos os microsserviços, a Equipe 9 deve configurar os privilégios padrão (`ALTER DEFAULT PRIVILEGES`) para a role dona do schema DDL:

```sql
-- Correção definitiva no database/01-init.sql da infraestrutura:
ALTER DEFAULT PRIVILEGES FOR ROLE portal_ddl IN SCHEMA usuarios
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO svc_usuarios;

ALTER DEFAULT PRIVILEGES FOR ROLE portal_ddl IN SCHEMA usuarios
    GRANT USAGE, SELECT ON SEQUENCES TO svc_usuarios;
```

---

### 3.7. D-ECO-07: Padrão Oficial de Erros HTTP (RFC 9457)

O `Identity-Service` adota formalmente a **RFC 9457** (*Problem Details for HTTP APIs*, evolução normativa da RFC 7807) como o padrão estrito de comunicação de erros, utilizando o content-type `application/problem+json`.

Todas as respostas de erro contêm exatamente os 6 campos canônicos:

```json
{
  "type": "https://api.portalb2b.com.br/errors/ERR-AUTH-001",
  "title": "Credenciais inválidas",
  "status": 401,
  "code": "ERR-AUTH-001",
  "detail": "Credenciais inválidas",
  "correlation_id": "0194eb82-7000-7000-8000-000000000001"
}
```

*Recomendação para os Consumidores:* Os demais microsserviços e gateways devem interoperar tratando respostas de erro com deserialização estruturada deste envelope, utilizando o campo `code` para decisões programáticas e `correlation_id` para investigação em logs centralizados (Loki/Jaeger).

---

### 3.8. D-ECO-08: Rastreabilidade Distribuída e Formato do `X-Correlation-Id`

O `Identity-Service` valida e propaga o cabeçalho `X-Correlation-Id` utilizando a convenção canônica UUID de 36 caracteres com hífens (`Guid.TryParseExact(val, "D")`), registrando este identificador em todas as linhas de log estruturado, eventos de auditoria e mensagens do Kafka.

Solicita-se à **Equipe 9** que na configuração do Nginx Gateway:
1. Caso o cliente ou teste envie `X-Correlation-Id`, o Nginx preserve e repasse o valor exato recebido.
2. Caso o header esteja ausente, os testes integrados das equipes enviem sempre o cabeçalho preenchido no formato UUID com hífens (`00000000-0000-0000-0000-000000000000`) — caso contrário, o `Identity-Service` gerará um novo UUID v7 automaticamente (`PRS-52`).

---

## 4. Guia de Ação Rápida para as Equipes Parceiras

### 4.1. Equipe 9 (`portal-b2b-kit-infra`)
- [ ] Aplicar no `gateway/common_routes.conf` e `nginx.conf` os roteamentos para `/api/v1/auth`, `/api/v1/tenants`, `/api/v1/users` e `/.well-known/jwks.json` (§3.1).
- [ ] Acrescentar o container `redis:7.4.6-alpine` no `docker-compose.yml` (§3.2).
- [ ] Adicionar os 4 tópicos do `Identity-Service` em `scripts/init_topics.sh` (§3.5).
- [ ] Aplicar `ALTER DEFAULT PRIVILEGES` no `database/01-init.sql` para corrigir o bug K1 (§3.6).

### 4.2. Grupo 2 (`Sistema-MicServ-SD` — Produtos e Fornecimentos)
- [ ] Atualizar `services/common/auth.py` para validar tokens via RS256 consultando `http://gateway:8080/.well-known/jwks.json` com o snippet disponibilizado (§3.3).
- [ ] Ajustar o deserializador do consumidor Kafka de `empresa_cadastrada` para processar o payload oficial contendo `tenant_id`, `cnpj`, `legal_name`, `status` e a lista `scopes` (§3.5).
- [ ] Mapear as chamadas autorizadas utilizando a claim `active_scope` para distinguir se o solicitante opera como `SUPPLIER` ou `BUYER` (§3.4).

### 4.3. Grupo 8 (`AED---Processo-e-Artefato-SD-` — QA e Testes)
- [ ] Atualizar as suites de teste de integração (pytest / Playwright / Postman) para contemplar o fluxo de autenticação em 2 passos: `POST /api/v1/auth/login` seguido de `POST /api/v1/auth/switch-context` (§3.4).
- [ ] Validar a presença e integridade do cabeçalho `X-Correlation-Id` em formato UUID nos testes ponta a ponta (§3.8).
- [ ] Executar asserções de erro baseadas na RFC 9457 com campo `code` e content-type `application/problem+json` (§3.7).

---
*Documento elaborado e chancelado pela Equipe de Arquitetura do Identity-Service. Dúvidas de integração podem ser endereçadas diretamente ao repositório ou via canal técnico de arquitetura da disciplina.*

# Especificação de Arquitetura: Identidade, Autenticação, Multi-Tenancy e Governança de Dados B2B

## 1. Visão Geral e Contexto Arquitetural
O ecossistema consiste em uma plataforma distribuída de comércio B2B composta por microsserviços orientados a domínio (`Usuários/Acesso`, `Produtos`, `Fornecimentos`, `Demanda`, `Mercado/Matching Engine`, `Negociação/Leilão`, `Pedidos`, `Logística/Frete` e `Transportadoras`), integrados de forma síncrona via **API Gateway (REST)** e assíncrona via **Apache Kafka**[cite: 1].

A persistência opera sob o modelo de **Banco de Dados Centralizado**, utilizando o Apache Kafka como amortecedor de picos de carga, orquestrador de fluxos de negócio assíncronos e barramento de eventos[cite: 1], enquanto o isolamento lógico entre clientes é garantido de ponta a ponta via **`AuthContext`**.

---

## 2. Decisões Arquiteturais Consolidadas (ADRs)

| ID | Decisão Arquitetural | Justificativa Técnica e de Negócio |
| :--- | :--- | :--- |
| **ADR-01** | **Segregação Control Plane vs. Data Plane** | O administrador da plataforma SaaS (*Platform Admin*) opera em plano separado dos clientes B2B. Nenhum CNPJ participante recebe permissões de governança da plataforma. |
| **ADR-02** | **Hierarquia Multi-CNPJ: Grupo Econômico $\rightarrow$ Tenant (CNPJ)** | Suporta holdings, grupos empresariais com múltiplos CNPJs e estruturas de Matriz/Filiais sem exigir recadastro de usuários ou trocas repetitivas de sessão. |
| **ADR-03** | **Quatro Escopos Operacionais Explícitos** | Além dos 3 escopos de negócio (`BUYER`, `SUPPLIER`, `CARRIER`)[cite: 1], institui-se o 4º escopo (`TENANT_ADMIN`) dedicado à gestão corporativa e visão macro da organização[cite: 1]. |
| **ADR-04** | **Troca Explícita de Contexto e Visão Macro (Mapa)** | O usuário opera com token vinculado ao escopo ativo (`switch-context`). Permite alternar entre um CNPJ específico (Modo Unitário) ou visão agregada de múltiplos CNPJs autorizados (Modo Macro/Mapa). |
| **ADR-05** | **Banco de Dados Centralizado com Isolamento via `AuthContext`** | Reduz a complexidade operacional de transações multi-serviço e permite consultas consolidadas, mitigando o risco de vazamento de dados através de injeção obrigatória do `AuthContext` em ORMs, conexões e eventos Kafka[cite: 1]. |
| **ADR-06** | **Validação Stateless (JWKS) + Revogação Imediata via Redis** | Microsserviços validam JWTs localmente via chave pública, consultando uma *denylist* distribuída no Redis para bloqueio instantâneo de sessões ou empresas suspensas[cite: 1]. |
| **ADR-07** | **OAuth 2.1 com Rotação de Refresh Token e Detecção de Reuso** | Protege contra sequestro de sessão invalidando toda a família de tokens caso um Refresh Token já utilizado seja reenviado. |
| **ADR-08** | **Token de Delegação / Impersonation Auditável (RFC 8693)** | Permite que a equipe de suporte opere temporariamente na visão de um cliente vinculando um chamado técnico, preservando na auditoria o autor real e o usuário personificado. |
| **ADR-09** | **Segurança Leste-Oeste e Contexto Assinado** | O tráfego interno entre API Gateway, Microsserviços e Kafka propaga o contexto criptograficamente assinado, impedindo adulteração de identidade na rede interna[cite: 1]. |

---

## 3. Modelo de Identidade, Multi-Tenancy (Multi-CNPJ) e 4 Escopos
-- (Propor melhorias/outras estruturas)
### 3.1. Viabilidade do 4º Escopo (`TENANT_ADMIN`) e Gestão de N CNPJs
Na arquitetura do portal, o bloco de clientes contempla `Compradores`, `Fornecedores`, `Transportadoras` e `Admins`[cite: 1]. Transformar o `ADMIN` (no nível da organização cliente) no **4º escopo selecionável** separa quem executa a operação diária (compra, vende, transporta)[cite: 1] de quem governa a estrutura corporativa (gerencia usuários, filiais, certificados, integrações de ERP e acompanha indicadores globais)[cite: 1].

Para permitir que uma pessoa gerencie **N CNPJs** (sejam filiais `0001`, `0002` ou empresas com raízes de CNPJ distintas pertencentes à mesma holding/grupo) e consiga tanto alternar rapidamente entre a Empresa A e a Empresa B quanto visualizar todas simultaneamente em um **mapa ou dashboard macro**, adota-se uma camada acima do `TENANT`: o **`ECONOMIC_GROUP` (ou `ACCOUNT` / Grupo Empresarial)**.

```text
[ ECONOMIC_GROUP / ACCOUNT ] (Ex: Holding AgroBrasil)
 │
 ├── [ TENANT A ] CNPJ: 11.111.111/0001-01 (AgroBrasil Insumos - BUYER, SUPPLIER, TENANT_ADMIN)
 ├── [ TENANT B ] CNPJ: 11.111.111/0002-82 (AgroBrasil Filial Sul - BUYER, TENANT_ADMIN)
 └── [ TENANT C ] CNPJ: 99.888.777/0001-55 (TransAgro Logística - CARRIER, TENANT_ADMIN)
```

### 3.2. Os 4 Escopos Operacionais do Ecossistema
1. **`BUYER` (Comprador):** Registro de demandas únicas e recorrentes, definição de frequência/quantidade, participação em leilões e acompanhamento de pedidos[cite: 1].
2. **`SUPPLIER` (Fornecedor):** Publicação de fornecimentos (produto, preço, quantidade e origem)[cite: 1], atualização de estoque e envio de lances e propostas em negociações[cite: 1].
3. **`CARRIER` (Transportadora):** Cadastro de transportadora, recebimento de solicitações de frete e envio de respostas de cotações[cite: 1].
4. **`TENANT_ADMIN` (Gestão Corporativa - 4º Escopo):** Visão geral de um ou N CNPJs do grupo, administração de membros e papéis, configuração de contas de serviço (ERP) e visualização macro consolidada em mapas e painéis gerenciais[cite: 1].

> **⚠️ RESSALVA ARQUITETURAL — ESTRUTURA DE PERMISSÕES:**
> A modelagem detalhada de *Roles*, *Claims* e tabelas de permissões granulares será conferida e alinhada com a implementação existente nos projetos locais **`userservice`** e **`accountservice`** dispostos na pasta Actuar-projects em documents, eles são do dominio de outro negócio, mas podem servir como material de estudo.
---

## 4. Modelagem de Dados (ERD de Identidade e Acesso)

```text
  +-------------------------+          +-------------------------+
  |     ECONOMIC_GROUP      | 1      N |         TENANT          |
  |-------------------------|----------|-------------------------|
  | id (UUID, PK)           |          | id (UUID, PK)           |
  | name (VARCHAR)          |          | group_id (UUID, FK)     |
  | status (ENUM)           |          | cnpj (VARCHAR, UQ)      |
  +-------------------------+          | legal_name (VARCHAR)    |
                                       | branch_type (HQ/BRANCH) |
                                       | geo_lat_lng (POINT)     |
                                       | status (ENUM)           |
                                       +-------------------------+
                                              | 1           | 1
                       +----------------------+             +-------------------+
                       | N                                                      | N
          +-------------------------+                              +-------------------------+
          |      TENANT_SCOPE       |                              |      TENANT_MEMBER      |
          |-------------------------|                              |-------------------------|
          | id (UUID, PK)           |                              | id (UUID, PK)           |
          | tenant_id (UUID, FK)    |                              | tenant_id (UUID, FK)    |
          | scope (ENUM)            |                              | user_id (UUID, FK)      |
          | is_active (BOOLEAN)     |                              | is_owner (BOOLEAN)      |
          +-------------------------+                              | can_view_group (BOOL)   |
                                                                   +-------------------------+
                                                                                | 1
                                                                                | N
                                                                   +-------------------------+
                                                                   |       MEMBER_ROLE       |
                                                                   |-------------------------|
                                                                   | tenant_member_id (FK)   |
                                                                   | role_id (UUID, FK)      |
                                                                   +-------------------------+
                                                                                | N
                                                                                | 1
  +-------------------------+                                      +-------------------------+
  |          USER           |                                      |          ROLE           |
  |-------------------------|                                      |-------------------------|
  | id (UUID, PK)           |                                      | id (UUID, PK)           |
  | email (VARCHAR, UQ)     |                                      | name (VARCHAR)          |
  | password_hash (VARCHAR) |                                      | scope (ENUM)            |
  | name (VARCHAR)          |                                      +-------------------------+
  +-------------------------+
```

---

## 5. Fluxos de Autenticação, Troca de Contexto e Visão Macro (Mapa)

### 5.1. Login e Descoberta de Contextos
1. O usuário autentica-se em `POST /auth/login` informando suas credenciais de Pessoa Física.
2. O serviço identifica todos os registros de `TENANT` vinculados ao usuário e verifica se ele possui permissão de agregação (`can_view_group`) dentro do `ECONOMIC_GROUP`.

### 5.2. Modos de Operação na Troca de Contexto (`POST /auth/switch-context`)

#### Modo 1: Operacional Unitário (Contexto de 1 CNPJ)
Utilizado quando o operador vai atuar diretamente em nome de uma empresa específica (ex.: dar lance como Fornecedor pelo Tenant A)[cite: 1].

**Requisição:**
```json
{
  "target_tenant_id": "ten_A_uuid",
  "target_scope": "SUPPLIER",
  "view_mode": "SINGLE"
}
```

**Claims do Token Emitido:**
```json
{
  "sub": "usr_01",
  "group_id": "grp_agrobrasil_uuid",
  "tenant_id": "ten_A_uuid",
  "allowed_tenants": ["ten_A_uuid"],
  "view_mode": "SINGLE",
  "active_scope": "SUPPLIER",
  "roles": ["SUPPLIER_SALES"],
  "permissions": ["fornecimento:publicar", "leilao:lance"]
}
```
Para alternar rapidamente da Empresa A para a Empresa B, o usuário seleciona a Empresa B no seletor superior do Frontend, disparando `POST /auth/switch-context` com `target_tenant_id: "ten_B_uuid"`.

#### Modo 2: Gestão Macro / Mapa (Multi-CNPJ ou Matriz/Filiais)
Utilizado quando o usuário entra no 4º escopo (`TENANT_ADMIN`) ou quando um comprador central da Matriz ativa a **"Visão Consolidada do Grupo"** para operar ou monitorar múltiplas filiais simultaneamente.

**Requisição:**
```json
{
  "target_group_id": "grp_agrobrasil_uuid",
  "target_tenants": ["ten_A_uuid", "ten_B_uuid", "ten_C_uuid"],
  "target_scope": "TENANT_ADMIN",
  "view_mode": "MACRO_GROUP"
}
```

**Claims do Token Emitido:**
```json
{
  "sub": "usr_01",
  "group_id": "grp_agrobrasil_uuid",
  "tenant_id": null,
  "allowed_tenants": ["ten_A_uuid", "ten_B_uuid", "ten_C_uuid"],
  "view_mode": "MACRO_GROUP",
  "active_scope": "TENANT_ADMIN",
  "roles": ["GROUP_MANAGER"]
}
```

#### Como a Visão Macro Opera no Mapa e nas Compras Matriz/Filiais:
* **Renderização no Mapa:** Quando o Frontend consulta rotas geográficas ou operacionais (ex.: posição de cargas em `Logística/Frete`, demandas abertas em `Demanda` ou origens em `Fornecimentos`)[cite: 1], o backend identifica `view_mode == MACRO_GROUP` no `AuthContext` e executa a filtragem pelo conjunto de empresas permitidas:
  ```sql
  SELECT * FROM solicitacoes_frete 
  WHERE tenant_id IN ('ten_A_uuid', 'ten_B_uuid', 'ten_C_uuid');
  ```
  O mapa exibe os pinos e métricas de todas as empresas do grupo simultaneamente, permitindo filtrar visualmente na tela ou clicar em uma filial para abrir seus detalhes sem necessidade de novo login.
* **Compras Centralizadas (Matriz comprando para Filiais):** Quando um comprador central está em modo `MACRO_GROUP` com escopo `BUYER` e registra uma nova `Demanda`[cite: 1], o payload de criação exige a seleção explícita de qual dos CNPJs da sua lista `allowed_tenants` será o **CNPJ Comprador/Faturamento** e qual será o **CNPJ de Destino/Entrega Logística**.

---

## 6. Ciclo de Vida de Tokens, Redis Denylist e Segurança Avançada

### 6.1. Padrão Phantom Token vs. JWT Assinado
> **⚠️ RESSALVA ARQUITETURAL — PHANTOM TOKEN:**
> A adoção do padrão *Phantom Token* (emissão de token opaco para o navegador do cliente com tradução para JWT interno no API Gateway) deverá ser consultada e comparada com a implementação atual nos microsserviços locais **`userservice`** e **`accountservice`**.

### 6.2. Revogação em Tempo Real via Denylist Distribuída (Redis)
Como a validação de JWT via chave pública (JWKS) é *stateless*, existe uma janela entre a emissão do token e sua expiração natural (5 a 15 minutos). Para garantir bloqueio imediato de usuários desligados ou CNPJs suspensos durante leilões em andamento[cite: 1], utiliza-se uma **Denylist no Redis**:

1. **Estrutura de Chaves no Redis (com TTL igual ao tempo restante do JWT):**
   * `denylist:jti:<token_jti>` $\rightarrow$ Revoga imediatamente um token específico (ex.: logout ou troca de contexto).
   * `denylist:user:<user_id>` $\rightarrow$ Armazena o timestamp Unix do bloqueio/alteração de permissão. Qualquer token cujo `iat` (Issued At) seja menor que esse timestamp é rejeitado.
   * `denylist:tenant:<tenant_id>` $\rightarrow$ Bloqueia instantaneamente qualquer operação de todos os usuários vinculados àquele CNPJ.
2. **Validação na Borda e em Serviços Críticos:**
   * O **API Gateway** consulta o cluster Redis em memória (tempo de resposta $< 1\text{ ms}$) a cada requisição autenticada antes de rotear para os microsserviços[cite: 1].

### 6.3. Rotação de Refresh Token com Detecção Automática de Reuso (OAuth 2.1)
1. Todo `Refresh Token` gerado recebe um identificador de família (`token_family_id`) vinculado à sessão inicial de login.
2. A cada chamada em `POST /auth/refresh`, o Refresh Token utilizado é invalidado (marcado como `CONSUMED` com registro de timestamp) e um novo par (`Access Token` + `Refresh Token` filho na mesma família) é retornado.
3. **Mecanismo de Defesa contra Roubo:** Caso um `Refresh Token` já marcado como `CONSUMED` seja apresentado novamente, o serviço de autenticação detecta uma violação de reuso e **revoga imediatamente todos os tokens pertencentes àquele `token_family_id`** (incluindo inserção do usuário/sessão na Denylist do Redis), forçando reautenticação completa.

### 6.4. Token de Delegação / Impersonation Auditável para Suporte (RFC 8693)
Para permitir suporte técnico e diagnóstico operacional sem compartilhamento de senhas:
1. O analista de suporte autenticado aciona o endpoint `POST /auth/impersonate` informando o `target_user_id`, `target_tenant_id` e o código obrigatório do chamado (`ticket_id`).
2. O serviço emite um token curto (ex.: 15 minutos) contendo a claim estruturada `act` (*Actor*):
   ```json
   {
     "sub": "usr_cliente_operador",
     "tenant_id": "ten_A_uuid",
     "active_scope": "BUYER",
     "act": {
       "sub": "usr_suporte_analista",
       "ticket_id": "SUP-4821"
     }
   }
   ```
3. O `AuthContext` expõe tanto `UserId` (`usr_cliente_operador`) quanto `ActorUserId` (`usr_suporte_analista`), registrando ambas as identidades nos logs do Loki/ELK e nos cabeçalhos de eventos do Kafka[cite: 1].

### 6.5. Segurança Leste-Oeste e Assinatura de Contexto Interno
Para garantir confiança zero (*Zero Trust*) no tráfego interno entre componentes:
* **Comunicação Síncrona (API Gateway $\rightarrow$ Microsserviços):** O API Gateway repassa o token JWT assinado (ou assina os cabeçalhos de contexto com chave privada interna)[cite: 1]. Os microsserviços jamais confiam em headers HTTP em texto puro (`X-Tenant-Id`) sem validar a assinatura criptográfica via JWKS em cache local.
* **Comunicação Assíncrona (Produtor $\rightarrow$ Kafka $\rightarrow$ Consumidor):** Todo evento publicado no Kafka carrega nos headers os metadados de identidade (`tenant_id`, `group_id`, `initiator_user_id`, `acting_scope`, `correlation_id`) acompanhados de um header `context_jws` (assinatura criptográfica desses metadados), validado pelo consumidor antes de processar a mensagem[cite: 1].

---

## 7. Operação com Banco Centralizado, Kafka e Isolamento via `AuthContext`

### 7.1. Papel do Apache Kafka com Banco de Dados Centralizado
Com a adoção de um banco de dados centralizado compartilhado entre os microsserviços, o Apache Kafka atua em quatro frentes essenciais[cite: 1]:
1. **Nivelamento de Carga (*Write Buffering*):** No serviço de `Negociação/Leilão`, rajadas de lances simultâneos entram no tópico `lance_realizado`, evitando esgotamento do pool de conexões e contenção de locks no banco centralizado[cite: 1].
2. **Orquestração Assíncrona de Domínios:** O fechamento de um leilão (`negociacao_fechada`) dispara a criação do pedido (`pedido_criado`), que aciona a logística (`solicitacao_frete_criada`) sem acoplamento temporal e sem rotinas de *polling* no banco[cite: 1].
3. **Prevenção de *Dual-Write* (Transactional Outbox):** Cada microsserviço grava sua alteração de negócio e o registro do evento na tabela `outbox_messages` dentro da mesma transação ACID no banco centralizado, garantindo entrega confiável ao Kafka[cite: 1].
4. **Desacoplamento Analítico:** Sistemas externos de BI/Analytics e auditoria consomem os fluxos do Kafka sem onerar o banco transacional com queries pesadas de relatórios[cite: 1].

### 7.2. Mitigação de Vazamento de Dados com `AuthContext` em Todos os Pontos, semelhante ao aplicado no Actuar-projects.
Em um banco de dados centralizado, a proteção contra vazamento de dados entre concorrentes não pode depender da lembrança manual do desenvolvedor ao escrever cláusulas `WHERE`. O isolamento é imposto por uma biblioteca transversal **`AuthContext`** operando em **4 camadas obrigatórias**:

```text
[ Requisição HTTP ] ──► (AuthContextMiddleware) ──┐
                                                  ├──► [ AuthContext (Scoped) ]
[ Evento Kafka ]    ──► (KafkaConsumerFilter)   ──┘            │
                                                               ├──► 1. Validação de Escopo/Rota ( Guard )
                                                               ├──► 2. Filtro Global de Leitura ( ORM Query Filter )
                                                               ├──► 3. Interceptor de Escrita ( SaveChanges Hook )
                                                               └──► 4. Sessão do Banco ( PostgreSQL RLS )
```

1. **Camada 1 — Hidratação Unificada (HTTP + Kafka):**
   * Na entrada HTTP, o `AuthContextMiddleware` valida o token e popula a instância *Scoped* do `AuthContext` (`UserId`, `ActorUserId`, `GroupId`, `ActiveTenantId`, `AllowedTenants[]`, `ActiveScope`, `ViewMode`).
   * Na entrada do consumidor Kafka, o `KafkaConsumerFilter` lê e valida os headers do evento (`tenant_id`, `acting_scope`, `context_jws`) e hidrata a **mesma estrutura `AuthContext`** antes de invocar a camada de serviço/repositório[cite: 1].
2. **Camada 2 — Filtro Automático de Leitura (ORM Global Query Filters):**
   * O contexto de banco de dados recebe o `AuthContext` por injeção de dependência.
   * Todas as tabelas de domínio implementam a interface de isolamento por Tenant e recebem filtro automático:
     * Modo `SINGLE`: injeta `WHERE tenant_id = @ActiveTenantId`.
     * Modo `MACRO_GROUP`: injeta `WHERE tenant_id = ANY(@AllowedTenants)`.
3. **Camada 3 — Blindagem de Escrita (Persistence Interceptor):**
   * Antes de qualquer `INSERT`, `UPDATE` ou `DELETE` ser enviado ao banco centralizado, o interceptor valida:
     * Em `INSERT`: preenche automaticamente `entity.TenantId = _authContext.ActiveTenantId` (ou, no modo Macro, exige que o `entity.TenantId` informado pertença à lista `_authContext.AllowedTenants`).
     * Em `UPDATE` / `DELETE`: aborta a transação com exceção crítica de segurança caso o registro alvo pertença a um `TenantId` fora de `_authContext.AllowedTenants`.
4. **Camada 4 — Defesa em Profundidade no SGBD (Row-Level Security - RLS):**
   * Para proteger consultas feitas via SQL puro, views ou Dapper (fora do ORM), o interceptor de abertura de conexão executa:
     ```sql
     SET LOCAL app.allowed_tenants = 'ten_A_uuid,ten_B_uuid';
     ```
   * As políticas de *Row-Level Security* (RLS) nativas do banco de dados garantem que nenhuma linha fora dessa lista seja lida ou alterada pela sessão ativa.

---

## 8. ANEXO EXTERNO: Especificação para o Time de Produtos e Domínio (Trava de Self-Dealing)

> **Nota de Encaminhamento:** Esta seção foi estruturada de forma independente para envio direto à equipe responsável pelos domínios de **Produtos, Fornecimentos, Demanda, Mercado (Matching Engine) e Negociação/Leilão**[cite: 1].

### 8.1. Contexto do Problema (Conflito de Interesses / Auto-Negociação)
Na arquitetura do Portal B2B, uma mesma empresa (CNPJ) — ou empresas pertencentes ao mesmo Grupo Econômico (`group_id`) — pode possuir simultaneamente os escopos de **Comprador (`BUYER`)**, **Fornecedor (`SUPPLIER`)** e **Transportadora (`CARRIER`)**[cite: 1].

Embora o microsserviço de Identidade e Acesso isole a sessão do usuário em um único escopo ativo por vez (`active_scope`), existe o risco negocial de **Auto-Negociação (*Self-Dealing*)** e manipulação de mercado:
* **Cenário 1 (Compra vs. Fornecimento):** Um operador atuando como `BUYER` pelo CNPJ X registra uma demanda ou abre um leilão reverso[cite: 1], e outro operador da mesma empresa (ou de outra filial/empresa do mesmo Grupo Econômico, atuando como `SUPPLIER`) envia lances ou propostas para vencer a própria demanda ou distorcer o preço de referência do mercado[cite: 1].
* **Cenário 2 (Pedido vs. Frete):** Uma empresa que possui frota própria cadastrada como `CARRIER` participa da cotação de frete de um pedido originado por ela mesma quando a regra comercial exigir transportadora terceirizada isenta[cite: 1].

### 8.2. Metadados Disponibilizados pelo Serviço de Identidade
Em todas as requisições REST e em todos os eventos publicados no Kafka, o serviço de Identidade disponibilizará no `AuthContext` os seguintes atributos validados[cite: 1]:
* `tenant_id`: Identificador único (UUID) do CNPJ em operação.
* `group_id`: Identificador único (UUID) do Grupo Econômico (Holding/Matriz) ao qual o CNPJ pertence.
* `active_scope`: Escopo sob o qual a ação está sendo executada (`BUYER`, `SUPPLIER`, `CARRIER`, `TENANT_ADMIN`)[cite: 1].

### 8.3. Definições Necessárias junto ao Time de Produtos

Solicitamos a validação do time de Produtos e Domínio sobre **onde** e **como** as travas abaixo devem ser aplicadas:

#### Decisão 1: Qual microsserviço será responsável por aplicar a trava de Compra vs. Fornecimento? (Ver como acontece no Actuar-projects e me trazer essa informação).
* [ ] **Opção A — Microsserviço de `Mercado (Matching Engine)`[cite: 1]:** Ao calcular oferta vs. demanda (`demanda_criada` vs. `fornecimento_criado`)[cite: 1], o motor ignora automaticamente o pareamento entre registros que possuam o mesmo `tenant_id` (ou `group_id`).
* [ ] **Opção B — Microsserviço de `Negociação / Leilão`[cite: 1]:** Ao receber uma proposta ou lance (`lance_realizado`)[cite: 1], o serviço valida se o `tenant_id` / `group_id` do Fornecedor conflita com o do Comprador titular da demanda, rejeitando a operação com erro de regra de negócio (`422 Unprocessable Entity`).
* [ ] **Opção C — Ambos os serviços (Recomendado):** O `Matching Engine` não sugere o pareamento automático e o serviço de `Negociação / Leilão` bloqueia tentativas de lances manuais diretos[cite: 1].

#### Decisão 2: Qual a regra para empresas de CNPJs diferentes que pertencem ao mesmo Grupo Econômico (`group_id`)?
* [ ] **Regra 1 (Restrição Total no Grupo):** Bloquear negociações sempre que `fornecedor.group_id == comprador.group_id` (impede transações entre Matriz e Filial ou empresas da mesma Holding dentro da plataforma).
* [ ] **Regra 2 (Restrição Apenas por CNPJ Exato):** Bloquear apenas quando `fornecedor.tenant_id == comprador.tenant_id`, permitindo operações *intercompany* entre CNPJs distintos do mesmo grupo econômico.
* [ ] **Regra 3 (Parametrizável por Demanda/Leilão):** O comprador define na abertura da demanda se aceita ou bloqueia propostas de empresas coligadas ao seu grupo econômico[cite: 1].

#### Decisão 3: Qual a regra de negócio para Cotação de Frete (`Logística / Frete` vs. `Transportadoras`)?
* [ ] Quando o microsserviço de `Logística / Frete` orquestrar cotações com `Transportadoras`[cite: 1], transportadoras cujo `tenant_id` ou `group_id` seja o mesmo do Comprador ou do Fornecedor do pedido podem receber a solicitação de cotação (caracterizando frete próprio/FOB dirigido) ou devem ser filtradas da disputa?
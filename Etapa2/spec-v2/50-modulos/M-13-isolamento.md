# M-13 · Isolamento

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-13) · **Estado:** Rascunho (ciclo 5) · **Atualizado em:** 2026-10-19
> **Pacote de contexto deste módulo:** este ficheiro + `10` (base §1–§2, §3 comum, §4, §6–§10 + extras §3.10, §5) + `30` (comum §1–§3, ENT-03, ENT-05, ENT-07, §5, §5.1) + `40` (§1, IF-08.1, IF-11, tipos partilhados) + `70` (comum §1) + `20` (EXT-01). Tamanho total: 39.6 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo

| Dentro | Fora (outro módulo ou fora do sistema) |
|---|---|
| Filtragem transparente de consultas LINQ (camada 2) em `ITenantScoped` (`tenant_scope`, `tenant_member`, `member_role`) via Global Query Filters do EF Core. | Autenticação e extração de claims no pipeline HTTP (M-00). |
| Interceção e bloqueio de persistência (camada 3) em `TenantSaveChangesInterceptor` para entidades `ITenantScoped` com `TenantId ∉ WriteTenants` ou `TenantId = Guid.Empty`. | Definição física de tabelas, índices e migrações no PostgreSQL (M-11). |
| Configuração de sessão e aplicação forçada de Row-Level Security no PostgreSQL 17.6 (camada 4) via `RlsConnectionInterceptor` com papel `identity_app`. | Gestão de membros, papéis e escopos operacionais (M-04, M-05). |
| Execução controlada de operações administrativas com bypass temporário via `IF-11.3` (`ExecuteWithBypassAsync`) e reposição estrita em bloco `finally`. | Consulta e cálculo de permissões de membros (M-08). |
| Aplicação da regra global RG-09: rejeição com 403 `ERR-CTX-007` e reversão transacional imediata. | Publicação de eventos de auditoria e integração (M-00, M-14). |

**Escreve:** Nenhuma tabela direta (atua via interceptores de persistência e comandos de sessão PostgreSQL) · **Lê:** `tenant_scope`, `tenant_member`, `member_role` (filtra leituras) · **Usa interfaces de:** M-00 (`IF-08.1` `IAuthContext`)

## 2. Endpoints e eventos (detalhe)

### EP (Endpoints)

| Campo | Valor |
|---|---|
| Endpoints HTTP | Nenhum. O módulo M-13 não expõe rotas HTTP diretamente. As suas garantias de isolamento multi-tenant são consumidas internamente pelo `IdentityDbContext` e módulos de domínio através da interface `IF-11` (`ITenantIsolation`) e dos interceptores do EF Core |

### IF-11 `ITenantIsolation` (`M13Isolamento/Interface/`)

Registo no DI: `ITenantIsolation` como *singleton*; interceptores como *scoped* (leem `IAuthContext` do pedido). `IdentityDbContext` registado via `AddDbContext` (*scoped*), sem *pooling*.

| Operação | Assinatura | Efeito e regras | Consumidores |
|---|---|---|---|
| IF-11.1 | `void ApplyQueryFilters(ModelBuilder modelBuilder, IdentityDbContext context)` | Camada 2: aplica a cada entidade `ITenantScoped` o filtro `e => context.AuthContext.BypassTenantFilter \|\| context.TenantIsolationBypass \|\| context.AuthContext.AllowedTenants.Contains(e.TenantId)`. Chamado em `OnModelCreating` | M-11 |
| IF-11.2 | `IReadOnlyList<IInterceptor> CreateInterceptors(IServiceProvider requestServices)` | Devolve `TenantSaveChangesInterceptor` (camada 3) e `RlsConnectionInterceptor` (camada 4) do escopo do pedido. Injetado na configuração do `IdentityDbContext` | M-11 |
| IF-11.3 | `Task<T> ExecuteWithBypassAsync<T>(IdentityDbContext context, Func<IdentityDbContext, Task<T> > operation)` | Bypass temporário para EP-09 (PRS-72). (1) `TenantIsolationBypass = true` à entrada → `InvalidOperationException`; (2) se ligação fechada, abre via `OpenConnectionAsync`; (3) ativa `TenantIsolationBypass = true` e `SELECT set_config('app.auth_flow', 'true', false)`; (4) executa `await operation(context)`; (5) em `finally`: repõe `TenantIsolationBypass = false`, executa `SELECT set_config('app.auth_flow', @b, false)` (@b da camada 4 do pedido) e fecha a ligação se a abriu em (2). Se `operation` lançar, falha na reposição (ex.: 25P02 em transação abortada) é descartada e relança exceção original; senão, erro de reposição propaga. Leitura só com `AsNoTracking`, escrita só com `ExecuteUpdateAsync` | M-05, M-08 |

### EV (Eventos)

| Campo | Tipo | Obrigatório | Valor |
|---|---|---|---|
| Nenhum | Não se aplica | Não se aplica | M-13 não publica nem consome tópicos de mensageria diretamente |

## 3. Requisitos funcionais

### RF-16 — Assegurar isolamento multi-tenant em múltiplas camadas

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Qualquer módulo do serviço executando consulta ou persistência em entidades `ITenantScoped` através do `IdentityDbContext` |
| Pré-condições | `IdentityDbContext` configurado com interceptores de `IF-11.2` e filtros de `IF-11.1`; tabelas protegidas com `FORCE ROW LEVEL SECURITY` no PostgreSQL |
| RN | RN-150, RN-151, RN-152, RN-153, RN-154, RN-155, RN-156, RN-157 |

**Fluxo principal:**
1. Abertura de conexão: em cada ligação de base de dados, `RlsConnectionInterceptor` define `SET ROLE identity_app` e preenche `app.allowed_tenants`, `app.write_tenants` e `app.auth_flow` via `set_config` (camada 4, RN-152).
2. Consulta de leitura: EF Core interpola o Global Query Filter da entidade `ITenantScoped`, restringindo resultados a `AllowedTenants`, salvo se `BypassTenantFilter` ou `TenantIsolationBypass` for verdadeiro (camada 2, RN-150).
3. Persistência de escrita: antes de persistir, `TenantSaveChangesInterceptor` valida entradas `ITenantScoped` em `Added`, `Modified` ou `Deleted`. Se `TenantId ∉ WriteTenants` ou `TenantId = Guid.Empty` (com bypass inativo), lança `ApiErrorException("ERR-CTX-007")` (camada 3, RN-151).
4. Execução SQL: PostgreSQL aplica as políticas RLS `USING` (leitura) e `WITH CHECK` (escrita). Se violadas, PostgreSQL emite `SQLSTATE 42501`, mapeado para 403 `ERR-CTX-007` com reversão transacional (camada 4, RN-153).
5. Bypass pontual: quando `ExecuteWithBypassAsync` é invocado, ativa `TenantIsolationBypass = true` e `app.auth_flow = 'true'`, executa a operação administrativa e restaura os parâmetros anteriores no bloco `finally` (RN-154, RN-155, RN-156).

**Fluxos de exceção**

| ID | Passo | Condição | Resposta | Efeitos / estado final |
|---|---|---|---|---|
| EX-16.1 | 3 | Entidade `ITenantScoped` com `TenantId ∉ WriteTenants` (camada 3) | 403 `ERR-CTX-007` | Transação desfeita. Nenhuma linha persistida |
| EX-16.2 | 3 | Entidade `ITenantScoped` com `TenantId = Guid.Empty` (camada 3) | 403 `ERR-CTX-007` | Transação desfeita. Nenhuma linha persistida |
| EX-16.3 | 4 | Instrução SQL rejeitada por política RLS (PostgreSQL 42501) | 403 `ERR-CTX-007` | Transação revertida. Nenhuma linha persistida |
| EX-16.4 | 5 | Invocação aninhada de `ExecuteWithBypassAsync` com bypass já ativo | 500 `ERR-SYS-001` | Lança `InvalidOperationException`. Transação desfeita |
| EX-16.5 | 5 | Operação sob bypass lança exceção em transação abortada (25P02 na reposição) | Exceção original da operação | Reposição descartada, transação revertida no PostgreSQL |

**Concorrência, idempotência e retomada**

| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Conexões simultâneas no pool sob diferentes inquilinos | Cada ligação executa `RlsConnectionInterceptor` com valores de `IAuthContext` próprios. Sem partilha de variáveis de sessão | CA-16.8 |
| Operação abortada durante `ExecuteWithBypassAsync` | Bloco `finally` repõe variáveis e fecha ligação se aberta localmente. Rollback descarta `set_config` transacional | CA-16.7 |
| Invocação de bypass concorrente com consulta padrão | Contextos EF Core distintos mantêm isolamento de instâncias; `TenantIsolationBypass` afeta unicamente o contexto do chamador | CA-16.6 |
| Reutilização de conexão física do pool após bypass | Conexão seguinte executa `RlsConnectionInterceptor` redefinindo `app.auth_flow = 'false'` e variáveis de contexto do novo pedido | CA-16.8 |

**Pós-condições:**
1. Linhas fora de `AllowedTenants` são invisíveis para consultas LINQ.
2. Escritas fora de `WriteTenants` ou com `TenantId = Guid.Empty` são bloqueadas antes ou durante a persistência SQL.
3. Toda violação de isolamento resulta em 403 `ERR-CTX-007` com reversão transacional.
4. Conexões reutilizadas operam sem retenção residual de bypass.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-150 | Autorização | **SE** consulta LINQ acede a entidade `ITenantScoped` **ENTÃO** aplica o filtro `BypassTenantFilter \|\| TenantIsolationBypass \|\| AllowedTenants.Contains(e.TenantId)` **SENÃO** exclui a linha do resultado | Nenhum | Nenhum | RF-16 | `[DOC] ADR-05, 10 §3.10, 40 IF-11.1` |
| RN-151 | Integridade | **SE** entidade `ITenantScoped` em `Added`/`Modified`/`Deleted` tem `TenantId ∉ WriteTenants` ou `TenantId = Guid.Empty` (com bypass inativo) **ENTÃO** bloqueia persistência e lança 403 `ERR-CTX-007` com rollback (RG-09) **SENÃO** permite gravação | `Guid.Empty proibido` | `ERR-CTX-007` | RF-16 | `[DOC] ADR-05, 10 §3.10, PRS-47, PRS-51` |
| RN-152 | Autorização | **SE** conexão de base de dados for aberta **ENTÃO** executa `SET ROLE identity_app` e define `app.allowed_tenants`, `app.write_tenants` e `app.auth_flow` via `set_config` **SENÃO** rejeita abertura com 503 `ERR-SYS-002` | Nenhum | `ERR-SYS-002` | RF-16 | `[DOC] 10 §3.10, 30 §5, PRS-17` |
| RN-153 | Autorização | **SE** instrução SQL violar política RLS `USING` ou `WITH CHECK` **ENTÃO** PostgreSQL emite `SQLSTATE 42501` convertido em 403 `ERR-CTX-007` com reversão transacional (RG-09) **SENÃO** executa comando SQL | Nenhum | `ERR-CTX-007` | RF-16 | `[MEDIDO] 30 §5.1, ADR-05` |
| RN-154 | Autorização | **SE** `ExecuteWithBypassAsync` for acionado com `TenantIsolationBypass = false` **ENTÃO** ativa bypass temporário em camadas 2 a 4 e restaura estado no `finally` **SENÃO** lança `InvalidOperationException` se bypass já ativo | Nenhum | `ERR-SYS-001` | RF-16 | `[DOC] 10 §3.10, 40 IF-11.3, PRS-72` |
| RN-155 | Integridade | **SE** operação sob bypass lançar exceção e reposição falhar com `SQLSTATE 25P02` em transação abortada **ENTÃO** descarta falha de reposição e propaga exceção original da operação **SENÃO** propaga erro de reposição | Nenhum | Nenhum | RF-16 | `[MEDIDO] 30 §5.1 (I26, I27), 40 IF-11.3` |
| RN-156 | Validação | **SE** operação sob bypass chamar `SaveChangesAsync` ou alterar `ChangeTracker` **ENTÃO** operação é inválida devendo usar exclusivamente `AsNoTracking` e `ExecuteUpdateAsync` **SENÃO** executa com integridade | Nenhum | `ERR-SYS-001` | RF-16 | `[DOC] 40 IF-11.3` |
| RN-157 | Autorização | **SE** conexão física for devolvida ao pool ou reutilizada **ENTÃO** redefine variáveis de sessão RLS na abertura com base no `IAuthContext` do novo pedido sem reter bypass **SENÃO** rejeita operação | Nenhum | `ERR-SYS-002` | RF-16 | `[MEDIDO] 30 §5.1 (I28), 10 §3.10` |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-16 — Assegurar isolamento multi-tenant em múltiplas camadas

  Contexto:
    Dado base de dados com RLS ativa e tenants "TEN1", "TEN2" e "TEN3" configurados

  @CA-16.1 @RF-16 @RN-150 @MUST @critico @borda
  Cenário: Camada 2 — Filtragem de leitura LINQ omite registos fora de AllowedTenants
    Dado membros registados nos tenants "TEN1" e "TEN2"
    E contexto de autenticação com view_mode "SINGLE" e AllowedTenants contendo apenas "TEN1"
    Quando consulta LINQ em tenant_member é executada sem tracking
    Então apenas os registos pertencentes a "TEN1" são retornados no resultado
    E nenhum registo de "TEN2" é retornado
    E consulta com AllowedTenants vazio retorna zero registos sem erro

  @CA-16.2 @RF-16 @RN-150 @MUST @borda
  Cenário: Camada 2 — Leitura com múltiplos tenants em visão macro e modo bypass
    Dado membros registados nos tenants da lista
    E contexto de autenticação com AllowedTenants contendo 50 tenants distintos
    Quando consulta LINQ em tenant_member é executada
    Então exatamente os registos pertencentes aos 50 tenants são retornados
    E consulta com BypassTenantFilter verdadeiro retorna registos de todos os tenants

  @CA-16.3 @RF-16 @RN-151 @MUST @critico @borda
  Esquema do Cenário: Camada 3 — Interceptor bloqueia escrita fora de WriteTenants ou com Guid vazio
    Dado entidade ITenantScoped com tenant_id <tenant_alvo> no ChangeTracker
    E contexto com WriteTenants contendo apenas "TEN1" e bypass desativado
    Quando SaveChangesAsync é executado no DbContext
    Então a persistência é interrompida lançando exceção com código "ERR-CTX-007"
    E nenhuma alteração é persistida na base de dados

    Exemplos:
      | tenant_alvo                            |
      | "TEN2"                                 |
      | "00000000-0000-0000-0000-000000000000" |

  @CA-16.4 @RF-16 @RN-152 @RN-153 @MUST @critico @borda
  Cenário: Camada 4 — Política RLS no PostgreSQL rejeita escrita fora de write_tenants
    Dado conexão aberta com papel "identity_app" e app.write_tenants configurado para "TEN1"
    Quando comando SQL de inserção em tenant_scope é executado diretamente para "TEN2"
    Então o PostgreSQL rejeita a instrução com código SQLSTATE "42501"
    E inserção com RETURNING para "TEN1" é aceite com sucesso

  @CA-16.5 @RF-16 @RN-153 @MUST @borda
  Cenário: Camada 4 — Atualização cruzada afeta zero linhas ou falha por RLS
    Dado membro existente pertencente ao tenant "TEN2"
    E sessão PostgreSQL configurada com app.allowed_tenants e app.write_tenants iguais a "TEN1"
    Quando comando SQL UPDATE em tenant_member é executado para alterar o membro de "TEN2"
    Então exatamente zero linhas são afetadas na base de dados
    E tentativa de UPDATE alterando a coluna tenant_id para "TEN2" falha com SQLSTATE "42501"

  @CA-16.6 @RF-16 @RN-154 @RN-156 @MUST @borda
  Cenário: IF-11.3 — Execução com bypass e reposição de variáveis no encerramento
    Dado operação administrativa invocada via ExecuteWithBypassAsync no DbContext
    Quando a operação executa leitura cruzada em "TEN2" com AsNoTracking e conclui com sucesso
    Então a leitura cruzada retorna os dados solicitados
    E após o término TenantIsolationBypass tem valor falso no DbContext
    E variável app.auth_flow tem valor "false" na sessão PostgreSQL
    E invocação aninhada de ExecuteWithBypassAsync lança InvalidOperationException

  @CA-16.7 @RF-16 @RN-154 @RN-155 @MUST @critico @borda
  Cenário: IF-11.3 — Reposição sob exceção em transação abortada suprime 25P02
    Dado operação executada sob ExecuteWithBypassAsync que gera erro SQL provocando transação abortada
    Quando bloco finally executa reposição de app.auth_flow e PostgreSQL emite SQLSTATE "25P02"
    Então o erro de reposição 25P02 é descartado
    E a exceção original da operação é relançada para o chamador
    E TenantIsolationBypass retorna para valor falso

  @CA-16.8 @RF-16 @RN-157 @MUST @borda
  Cenário: Reutilização de conexão do pool opera com isolamento limpo
    Dado conexão física devolvida ao pool após operação com bypass
    Quando nova requisição sem token abre a referida conexão do pool
    Então RlsConnectionInterceptor reconfigura app.auth_flow para "false"
    E app.allowed_tenants e app.write_tenants são definidos como vazios
    E comandos SQL contra tabelas ITenantScoped afetam zero linhas ou falham com "42501"
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-16 | MUST | RN-150, RN-151, RN-152, RN-153, RN-154, RN-155, RN-156, RN-157 | Nenhum | EX-16.1, EX-16.2, EX-16.3, EX-16.4, EX-16.5 | CA-16.1, CA-16.2, CA-16.3, CA-16.4, CA-16.5, CA-16.6, CA-16.7, CA-16.8 | F-00 |

**Totais:** RF = 1 · RN = 8 · EP = 0 · EV = 0 · CA = 8 (`@critico` = 4) · Σw = 24

## 7. Pressupostos do módulo

### 7.1 Pressupostos (PRS)

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-310 | `ITenantIsolation` é registado como *singleton* e seus interceptores (`TenantSaveChangesInterceptor`, `RlsConnectionInterceptor`) são registados como *scoped*, associando-se à instância de `IAuthContext` da requisição HTTP corrente | Garante isolamento estrito por requisição e compatibilidade com AddDbContext sem pooling (ADR-05, IF-11) | Pendente (PO) |

### 7.2 Dúvidas (Q)

| ID | Dúvida | Hipótese adotada | Bloqueia |
|---|---|---|---|
| Q-150 | Qual é a fase de entrega de RF-16 no plano de fases (`60`)? | Hipótese: RF-16 pertence à fase `F-00` por integrar o núcleo transversal de isolamento e persistência das tabelas `ITenantScoped` (`10` §3.10) | Não |
| Q-151 | Qual é a convenção de cálculo para o somatório $\Sigma w$? | Hipótese: Cada CA `@MUST` possui peso $w = 3$, totalizando $\Sigma w = 8 \times 3 = 24$ | Não |

### 7.3 Pedidos de alteração (CR)

Nenhum. Contratos partilhados (`IF-11`, `ITenantScoped`), variáveis RLS (`30` §5, §5.1), códigos de erro e regras de dependência constam integralmente em `pacote-M-13.md`.

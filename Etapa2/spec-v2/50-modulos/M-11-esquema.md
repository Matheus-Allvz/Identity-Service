# M-11 · Esquema, migração e seed

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-11) · **Estado:** Rascunho (ciclo 5, UT-3-11) · **Atualizado em:** 2026-10-19
> **Pacote:** este ficheiro + `10` + `30` (ENT-01 a ENT-10, §5, §6) + `40` (IF-08.1, IF-11.1, IF-11.2) + `70` (§1, §2) + `20` (EXT-01) + `90`. Total: 57 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Esquema DDL via EF Migrations (`30` §3, §5, §6) | Escritas de negócio em runtime |
| Configuração EF e convenções em `IdentityDbContext` | Interceptores e RLS das camadas 3 e 4 (M-13) |
| `Database.MigrateAsync()` no arranque em `Program.cs` | Endpoints HTTP e tokens (M-00 a M-10) |
| Seed idempotente dos 4 papéis em `role` | Denylist Redis e Kafka (M-00, M-14) |
| Registo de `IdentityDbContext` com IF-11.2 | Fixtures de teste (M-01, M-04, `70`) |

**Escreve:** `role` (seed), DDL ENT-01 a ENT-10, RLS e `identity_app` · **Lê:** `role` · **Usa:** M-13 (IF-11.1, IF-11.2), M-00 (IF-08.1)

## 2. Endpoints e eventos (detalhe)
O M-11 não expõe endpoints HTTP nem publica eventos Kafka em runtime. Opera no arranque e na persistência.

## 3. Requisitos funcionais

### RF-19 — Provisionamento de esquema, migração e seed
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Arranque da aplicação (`Program.cs`) / Kestrel |
| Pré-condições | PostgreSQL acessível via `DB_CONNECTION_STRING` |
| RN | RN-130, RN-131, RN-132, RN-133, RN-134 |

**Fluxo principal:**
1. A aplicação inicia em `Program.cs` e valida `DB_CONNECTION_STRING`.
2. Cria `DbContextOptionsBuilder<IdentityDbContext>` com `UseNpgsql(DB_CONNECTION_STRING)` e `UseSnakeCaseNamingConvention()`, sem interceptores.
3. Executa `Database.MigrateAsync()`.
4. A migração cria tabelas ENT-01 a ENT-10, índices, restrições e FK `ON DELETE RESTRICT`.
5. Executa via SQL a role `identity_app`, permissões, RLS forçado e políticas.
6. Insere os 4 papéis em `role` com `ON CONFLICT (name) DO NOTHING`.
7. Regista `IdentityDbContext` no DI como scoped sem pooling, com `IAuthContext` e interceptores de `IF-11.2`.
8. O arranque conclui e liberta a porta HTTP 8080.

**Fluxos de exceção**
| ID | Passo | Condição | Resposta | Efeitos |
|---|---|---|---|---|
| EX-19.1 | 1 | `DB_CONNECTION_STRING` ausente | Termina código 1 | Não arranca |
| EX-19.2 | 3 | PostgreSQL inacessível | Termina código 1 | Migração abortada |
| EX-19.3 | 4 | Falha na execução DDL | Termina código 1 | BD inalterada |

**Concorrência, idempotência e retomada**
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Instâncias simultâneas em BD vazia | Lock do EF serializa; uma aplica DDL/seed, outra conclui sem erro | CA-19.1 |
| Processo morto a meio da migração | Transação DDL revertida; próximo arranque reaplica do início | CA-19.3 |
| Reinício com BD já migrada | `MigrateAsync()` não reaplica; seed não duplica registos | CA-19.2 |
| Papéis de `role` já existentes | `ON CONFLICT (name) DO NOTHING` ignora registos sem erro | CA-19.2 |

**Pós-condições:** Tabelas ENT-01 a ENT-10 criadas; RLS e privilégios ativos; `role` contém exatamente os 4 papéis.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-130 | Integridade | **SE** inicia **ENTÃO** DEVE executar `Database.MigrateAsync()` antes de `app.Run()` **SENÃO** não DEVE responder pedidos HTTP | `Timeout = 300 s` | – | RF-19 | ADR-11, `10` §3.11 |
| RN-131 | Integridade | **SE** migra **ENTÃO** DEVE criar ENT-01 a ENT-10 em snake_case singular com FK `ON DELETE RESTRICT` **SENÃO** falha e reverte | snake_case | – | RF-19 | PRS-19, `30` §1, §3 |
| RN-132 | Integridade | **SE** cria tabelas com isolamento **ENTÃO** DEVE criar `identity_app`, privilégios, RLS forçado e políticas **SENÃO** falha | `identity_app` | – | RF-19 | ADR-05, `30` §5 |
| RN-133 | Integridade | **SE** executa seed **ENTÃO** DEVE inserir os 4 papéis com `ON CONFLICT (name) DO NOTHING` **SENÃO** falha | 4 papéis | – | RF-19 | `30` §6 |
| RN-134 | Validação | **SE** regista `IdentityDbContext` no DI **ENTÃO** DEVE registar scoped sem pooling com interceptores de `IF-11.2` **SENÃO** não DEVE disponibilizar contexto | scoped | – | RF-19 | IF-11, IF-11.2 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-19 — Provisionamento de esquema, migração e seed

  Contexto:
    Dado que PostgreSQL está acessível via "DB_CONNECTION_STRING"

  @CA-19.1 @RF-19 @RN-130 @RN-131 @RN-132 @RN-133 @MUST @critico
  Cenário: Migração e seed executados com sucesso numa base de dados vazia
    Dado que a base de dados PostgreSQL está vazia sem tabelas
    Quando o serviço executa o arranque e invoca "Database.MigrateAsync()"
    Então todas as tabelas ENT-01 a ENT-10 são criadas
    E o papel "identity_app" existe com privilégios concedidos
    E as tabelas com isolamento têm Row-Level Security forçado
    E a contagem de registos na tabela "role" é exatamente 4
    E a tabela "role" contém os 4 papéis com 17 permissões
    E o serviço completa o arranque e responde status 200 em GET "/.well-known/jwks.json"

  @CA-19.2 @RF-19 @RN-130 @RN-133 @MUST
  Cenário: Idempotência da migração e do seed em reinício do serviço
    Dado que a base de dados PostgreSQL já foi migrada e contém os 4 papéis em "role"
    Quando o serviço reinicia e executa "Database.MigrateAsync()"
    Então nenhuma nova migração é aplicada
    E a contagem de registos na tabela "role" permanece exatamente 4
    E o serviço completa o arranque com status 200 em GET "/.well-known/jwks.json"

  @CA-19.3 @RF-19 @RN-130 @RN-131 @MUST @critico @borda
  Esquema do Cenário: Falha de arranque perante BD indisponível ou configuração vazia
    Dado que "DB_CONNECTION_STRING" está definida com <configuracao>
    Quando a aplicação executa o arranque em "Program.cs"
    Então o processo termina com código de saída 1
    E a porta HTTP 8080 não é disponibilizada

    Exemplos:
      | configuracao                                                    |
      | ""                                                              |
      | "Host=127.0.0.1;Port=5433;Database=identity;Username=invalido" |
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-19 | MUST | RN-130 a RN-134 | Nenhum | EX-19.1 a EX-19.3 | CA-19.1 a CA-19.3 | F-00 |

**Totais:** RF = 1 · RN = 5 · EP = 0 · EV = 0 · CA = 3 (`@critico` = 2) · Σw = 5

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-210 | DDL de RLS e `identity_app` na migração inicial via SQL direto | `30` §5 e `10` §3.11 | PO, 2026-10-19 |
| PRS-211 | Utilizador de migração detém privilégios de superutilizador | Necessário para RLS e GRANT (`70` §2) | PO, 2026-10-19 |
| PRS-212 | Migrações automáticas ocorrem com lock do EF Core | Garante integridade atómica de esquema | PO, 2026-10-19 |

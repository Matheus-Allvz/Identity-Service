# M-11 · Esquema, migração e seed

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR (UT-3-11) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** `10` + `30` (ENT-01..10, §5, §6) + `40` (IF-08.1, IF-11.1, IF-11.2) + `70` + `20` + `90`. Total: 57 KiB.

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
| Prioridade / Ator | MUST / Arranque da aplicação (`Program.cs`) / Kestrel |
| Pré-condições / RN | PostgreSQL acessível via `DB_CONNECTION_STRING` / RN-130 a RN-134 |

**Fluxo principal:** 1. Valida `DB_CONNECTION_STRING`. 2. Configura DbContext com `UseNpgsql` e `UseSnakeCaseNamingConvention` sem interceptores. 3. Executa `Database.MigrateAsync()`. 4. Cria tabelas ENT-01 a ENT-10, índices e FKs `ON DELETE RESTRICT`. 5. Cria role `identity_app`, permissões e RLS forçado. 6. Insere os 4 papéis com `ON CONFLICT (name) DO NOTHING`. 7. Regista DbContext scoped sem pooling com IF-11.2. 8. Liberta porta HTTP 8080.
**Fluxos de exceção:** EX-19.1 (`DB_CONNECTION_STRING` ausente → termina 1); EX-19.2 (PostgreSQL inacessível → termina 1); EX-19.3 (falha DDL → termina 1).
**Concorrência e idempotência:** Instâncias concorrentes são serializadas por lock do EF (CA-19.1). Processo interrompido reverte transação DDL (CA-19.3). Reinício com BD migrada não reaplica migrações nem duplica papéis (CA-19.2).
**Pós-condições:** Tabelas ENT-01 a ENT-10 criadas; RLS e privilégios ativos; `role` contém exatamente os 4 papéis.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-130 | Integridade | **SE** inicia **ENTÃO** executa `Database.MigrateAsync()` antes de `app.Run()` **SENÃO** não responde HTTP | 300 s | – | RF-19 | ADR-11 |
| RN-131 | Integridade | **SE** migra **ENTÃO** cria ENT-01..10 em snake_case singular com FK RESTRICT **SENÃO** falha e reverte | snake_case | – | RF-19 | PRS-19, `30` |
| RN-132 | Integridade | **SE** isolamento **ENTÃO** cria `identity_app`, permissões, RLS forçado e políticas **SENÃO** falha | `identity_app` | – | RF-19 | ADR-05, `30` |
| RN-133 | Integridade | **SE** seed **ENTÃO** insere os 4 papéis com `ON CONFLICT (name) DO NOTHING` **SENÃO** falha | 4 papéis | – | RF-19 | `30` §6 |
| RN-134 | Validação | **SE** regista DbContext **ENTÃO** scoped sem pooling com interceptores de `IF-11.2` **SENÃO** falha | scoped | – | RF-19 | IF-11.2 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-19 — Provisionamento de esquema, migração e seed
  Contexto:
    Dado que PostgreSQL está acessível via "DB_CONNECTION_STRING"

  @CA-19.1 @RF-19 @RN-130 @RN-131 @RN-132 @RN-133 @MUST @critico @borda
  Cenário: Migração e seed numa base de dados vazia
    Dado base PostgreSQL vazia sem tabelas
    Quando arranque invoca "Database.MigrateAsync()"
    Então tabelas ENT-01 a ENT-10 são criadas
    E papel "identity_app" existe e tabelas têm RLS forçado
    E tabela "role" contém os 4 papéis com 17 permissões
    E arranque conclui com 200 em GET "/.well-known/jwks.json"

  @CA-19.2 @RF-19 @RN-130 @RN-133 @MUST @borda
  Cenário: Idempotência da migração e seed em reinício
    Dado base já migrada com os 4 papéis em "role"
    Quando serviço reinicia e executa "Database.MigrateAsync()"
    Então nenhuma nova migração é aplicada e "role" mantém 4 registos
    E arranque conclui com 200 em GET "/.well-known/jwks.json"

  @CA-19.3 @RF-19 @RN-130 @RN-131 @MUST @critico @borda
  Esquema do Cenário: Falha de arranque perante BD indisponível ou vazia
    Dado que "DB_CONNECTION_STRING" está definida com <configuracao>
    Quando a aplicação executa o arranque em "Program.cs"
    Então o processo termina com código de saída 1
    E a porta HTTP 8080 não é disponibilizada
    Exemplos:
      | configuracao |
      | "" |
      | "Host=127.0.0.1;Port=5433;Database=identity;Username=invalido" |

  @CA-19.4 @RF-19 @RN-131 @MUST @borda
  Cenário: Integridade referencial com ON DELETE RESTRICT
    Dado tabela "tenant" com registo referenciado em "tenant_member"
    Quando tentativa de DELETE direto em "tenant" ocorre
    Então base de dados rejeita operação com violação de chave estrangeira

  @CA-19.5 @RF-19 @RN-132 @MUST @borda
  Cenário: RLS forçado e privilégios restritos de identity_app
    Dado migração concluída com papel "identity_app"
    Quando verificação de políticas de segurança nas tabelas com isolamento ocorre
    Então Row-Level Security está ativo com FORCE RLS
    E "identity_app" não possui privilégios de DDL

  @CA-19.6 @RF-19 @RN-134 @MUST @borda
  Cenário: Registo do DbContext no contentor DI
    Dado contentor de dependências configurado no arranque
    Quando resolução de "IdentityDbContext" em dois escopos distintos ocorre
    Então instâncias resolvidas são distintas e configuradas como scoped sem pooling
    E interceptores de IF-11.2 estão associados
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-19 | MUST | RN-130 a RN-134 | Nenhum | EX-19.1 a EX-19.3 | CA-19.1 a CA-19.6 | F-00 |

**Totais:** RF = 1 · RN = 5 · EP = 0 · EV = 0 · CA = 6 (`@critico` = 2) · Σw = 18

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-210 | DDL de RLS e `identity_app` na migração inicial via SQL direto | `30` §5 e `10` §3.11 | PO, 2026-11-02 (D-23) |
| PRS-211 | Utilizador de migração detém privilégios de superutilizador | Necessário para RLS e GRANT (`70` §2) | PO, 2026-11-02 (D-23) |
| PRS-212 | Migrações automáticas ocorrem com lock do EF Core | Garante integridade atómica de esquema | PO, 2026-11-02 (D-23) |

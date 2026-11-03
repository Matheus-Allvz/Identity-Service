# M-01 · Login

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-01) · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** este doc + `10` + `30` (ENT-04, ENUM-04) + `40` (EP-01, EV-03, IF-01, IF-02, IF-03, IF-04, IF-06, IF-07, IF-09) + `70` + `90`. Total: ~54 KiB (orçamento ≤ 60 KiB).

## 1. Âmbito do módulo
| Dentro | Fora |
|---|---|
| Autenticação inicial via EP-01 | Troca de contexto ativo (M-03) |
| Verificação Argon2id (RNF-01) e proteção temporal | Rotação/consumo de refresh token (M-02) |
| Bloqueio após 5 falhas consecutivas | Emissão/assinatura de tokens JWT (M-07) |
| Transições de status em `user_account` | Consulta e agregação de contextos (M-08) |
| Listagem de contextos via IF-09.6 | Despacho outbox e publicação Kafka (M-14) |
| `IUserAccountStatusWriter` (IF-02) | Suspensão administrativa de utilizador (M-10) |

**Escreve:** ENT-04 (`user_account`) · **Lê:** ENT-04 · **Usa:** M-00 (IF-03.1, IF-04.2), M-02 (IF-01.1/4), M-07 (IF-06.1), M-08 (IF-09.1/6), M-14 (IF-07.3)

## 2. Endpoints e eventos (detalhe)

### EP-01 `POST /api/v1/auth/login`
| Campo | Valor |
|---|---|
| RF / RN | RF-01 / RN-01 a RN-03, RN-30 a RN-34 |
| Permissão | Pública (`Authorization` ignorado; sem token obrigatório) |
| Idempotência | Não. Sucesso gera nova sessão; falha incrementa contador |

**Request:** `{"email":"utilizador@example.test","password":"PasswordSegura123!"}`

**Ordem de avaliação:** 1. Rota/método → 404/405. 2. Tamanho ≤ 64 KiB → 413 `ERR-REQ-003`. 3. Content-Type `application/json; charset=utf-8` → 415 `ERR-REQ-004`. 4. JSON válido, sem extras, `email`/`password` presentes e não nulos (RN-01) → 400 `ERR-VAL-001`. 5. Formato `email` (RFC 5322) e `password` (12..128 chars, 4 classes sem trim) (RN-02) → 422 `ERR-VAL-003`. 6. Normalização `trim().ToLowerInvariant()` e consulta `user_account` (RN-03); falha de BD → 503 `ERR-SYS-002`. 7. Utilizador inexistente: Argon2id contra hash fictício e auditoria `LOGIN_FAILED` sem escrita → 401 `ERR-AUTH-001` (RN-30, RNF-01). 8. Se `status = LOCKED` com `locked_until > agora`: Argon2id e auditoria sem alterar BD → 401 `ERR-AUTH-001` (RN-30, RNF-01, PRS-83). 9. Se `status = LOCKED` com `locked_until ≤ agora`: passa a `ACTIVE`, falhas 0, `locked_until = NULL` na transação antes de checar senha (RN-31, D-08). 10. Validação da senha com Argon2id (RN-30, RNF-01). 11. Senha incorreta: (a) se `status = SUSPENDED`, auditoria sem somar falhas → 401 `ERR-AUTH-001` (RN-32, PRS-83); (b) se `status = ACTIVE` e falhas + 1 < 5, incrementa falhas e auditoria → 401 `ERR-AUTH-001` (RN-32); (c) se `status = ACTIVE` e falhas + 1 >= 5, passa a `LOCKED` (agora + 900 s), `denylist:user` IF-04.2, revoga famílias IF-01.4, EV-03 IF-07.3, auditoria `USER_LOCKED` → 401 `ERR-AUTH-001` (RN-32, PRS-31, PRS-32). 12. Senha correta mas `status = SUSPENDED`: sem alterar BD → 403 `ERR-AUTH-004` (RN-33, RG-05, D-08, D-11). 13. Senha correta e `status = ACTIVE`: zera falhas, `locked_until = NULL`, `updated_at = agora`, família IF-01.1, contextos IF-09.6; obtém `ISigningKey` e assina token IF-06.1 (`view_mode = NONE`) (se falhar, retorna 503 `ERR-SYS-002`), auditoria `LOGIN_SUCCESS`, commit único → 200 OK (RN-34, RNF-03).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-001`; 403 `ERR-AUTH-004`; 404 `ERR-REQ-001`; 405 `ERR-REQ-002`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 422 `ERR-VAL-003`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Sucesso (200):** `access_token` (`view_mode = NONE`), `token_type` (`"Bearer"`), `expires_in` (900), `refresh_token` (43 chars), `refresh_expires_in` (604800), `user` (`id`, `email`, `name`, `platform_role`), `available_contexts` (array ordenado RG-10). Headers: `Cache-Control: no-store`, `Pragma: no-cache`. Transação atómica via `IdentityDbContext`.

### EV-03 `usuario_bloqueado`
Publicado na outbox por IF-07.3 ao atingir 5 falhas consecutivas de login (RN-32): `event_id` (UUID v7), `user_id` (UUID), `status` (`"LOCKED"`), `locked_until` (ISO 8601 UTC `agora + 900 s`), `reason` (`"Excesso de tentativas consecutivas de login com palavra-passe incorreta"`, PRS-60), `blocked_at` (ISO 8601 UTC).

### IF-02 `IUserAccountStatusWriter`
- **IF-02.1 `SetAdministrativeStatusAsync(Guid userId, UserStatus status)`:** Altera status para `ACTIVE` ou `SUSPENDED`. Zera falhas, define `locked_until = NULL` e atualiza `updated_at`. Devolve status anterior ou `null` se inexistente. Rejeita `LOCKED` com `ArgumentException`.

## 3. Requisitos funcionais

### RF-01 — Autenticar utilizador e listar contextos
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / Utilizador anónimo envia pedido para `POST /api/v1/auth/login` |
| Pré-condições / RN | Nenhuma (endpoint público) / RN-01 a RN-03, RN-30 a RN-34 |

**Fluxo principal:** 1. `POST /api/v1/auth/login`. 2. Valida transporte e formato (RN-01, RN-02). 3. Normaliza e consulta e-mail (RN-03). 4. Se bloqueio expirado, reativa conta (RN-31). 5. Valida senha com Argon2id (RN-30). 6. Se senha correta e `SUSPENDED`, rejeita com 403 (RN-33). 7. Se credencial válida e conta `ACTIVE`, zera falhas (RN-34). 8. Cria família via IF-01.1, obtém chave, emite token inicial via IF-06.1 e consulta contextos via IF-09.6. 9. Em transação única grava alterações em `user_account`, sessão e auditoria `LOGIN_SUCCESS`. 10. Retorna 200 com tokens, contextos e headers sem cache.

**Fluxos de exceção:** EX-01.1 (formato inválido → 400); EX-01.2 (política violada → 422); EX-01.3 (e-mail inexistente, senha errada ou conta `LOCKED` → 401 com Argon2id e auditoria); EX-01.4 (senha correta em conta `SUSPENDED` → 403); EX-01.5 (falha de BD ou chave → 503, rollback).

**Concorrência e idempotência:** Falhas que atingem limite comutam para `LOCKED` uma vez com chave no Redis, revogação e EV-03 (CA-01.4). Sucesso concorrente com falha zera contador se comitar por último (CA-01.5). Falha de dependência efetua rollback (CA-01.7).

**Pós-condições:** Em sucesso, sessão `ACTIVE`, falhas zeradas e auditoria. Em bloqueio, conta `LOCKED`, Redis, revogação e outbox.

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-01 | Validação | **SE** JSON inválido, não objeto raiz, campos extras ou `email`/`password` ausentes/nulos **ENTÃO** 400 **SENÃO** segue | – | `ERR-VAL-001` | RF-01 | `40` §2 |
| RN-02 | Validação | **SE** `email` fora de 5..320 chars ou violar RFC 5322 OU `password` fora de 12..128 chars com 4 classes sem trim **ENTÃO** 422 **SENÃO** segue | – | `ERR-VAL-003` | RF-01 | PRS-30, PRS-80 |
| RN-03 | Normalização | **SE** pedido recebido **ENTÃO** aplica `trim().ToLowerInvariant()` no `email` antes de consultar em `user_account` **SENÃO** rejeita | – | – | RF-01 | PRS-11, `30` ENT-04 |
| RN-30 | Proteção Temporal | **SE** e-mail inexistente, conta `LOCKED` ativa ou senha incorreta **ENTÃO** executa Argon2id (`m=19456`, `t=2`, `p=1`) e responde 401 **SENÃO** segue | – | `ERR-AUTH-001` | RF-01 | RNF-01, D-08 |
| RN-31 | Temporal | **SE** conta `LOCKED` e `locked_until <= agora` **ENTÃO** passa a `ACTIVE`, zera falhas e `locked_until = NULL` na transação antes de checar senha **SENÃO** segue | – | – | RF-01 | D-08, UT-3-01 |
| RN-32 | Integridade | **SE** senha incorreta em conta `ACTIVE` **ENTÃO** soma falha; ao atingir 5, passa a `LOCKED`, `locked_until = agora + 900 s`, `denylist:user`, revoga famílias e emite EV-03 **SENÃO** soma falha | – | `ERR-AUTH-001` | RF-01 | PRS-31, PRS-32, D-09 |
| RN-33 | Autorização | **SE** senha correta mas `status = SUSPENDED` **ENTÃO** responde 403 sem alterar falhas **SENÃO** segue | – | `ERR-AUTH-004` | RF-01 | RG-05, D-08, D-11 |
| RN-34 | Integridade | **SE** senha correta e conta `ACTIVE` **ENTÃO** zera falhas, `locked_until = NULL`, emite sessão via IF-01.1 e token via IF-06.1 em transação atómica **SENÃO** segue | – | – | RF-01 | RNF-03, UT-3-01 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-01 — Autenticar utilizador e listar contextos
  Contexto:
    Dado que existe o utilizador "utilizador@example.test" ativo com senha "PasswordSegura123!"
    E que a derivação de chave utiliza parâmetros Argon2id com m=19456, t=2 e p=1

  @CA-01.1 @RF-01 @RN-01 @RN-03 @RN-34 @MUST @RNF-03 @borda
  Cenário: Autenticação com sucesso e emissão de sessão inicial
    Quando envia POST "/api/v1/auth/login" com corpo {"email":" Utilizador@Example.Test ","password":"PasswordSegura123!"}
    Então o status é 200, "token_type" é "Bearer", "expires_in" é 900, "view_mode" é "NONE", falhas 0, auditoria "LOGIN_SUCCESS" gravada em transação única (RNF-03)

  @CA-01.2 @RF-01 @RN-02 @MUST @borda
  Esquema do Cenário: Rejeição por formato de e-mail ou política de palavra-passe
    Quando envia POST "/api/v1/auth/login" com email <email_teste> e senha <senha_teste>
    Então o status é 422 e o código é "ERR-VAL-003"
    Exemplos:
      | email_teste               | senha_teste           |
      | "invalido"                | "PasswordSegura123!"  |
      | "utilizador@example.test" | "fraca"               |
      | "utilizador@example.test" | "SemDigitoOuEspecial" |

  @CA-01.3 @RF-01 @RN-30 @MUST @critico @borda @RNF-01
  Esquema do Cenário: Proteção de enumeração e credenciais inválidas
    Quando envia POST "/api/v1/auth/login" com email <email_cenario> e senha <senha_cenario>
    Então o status é 401, o código é "ERR-AUTH-001", executa Argon2id (RNF-01) e gera auditoria "LOGIN_FAILED"
    Exemplos:
      | email_cenario              | senha_cenario         |
      | "inexistente@example.test" | "PasswordSegura123!"  |
      | "utilizador@example.test"  | "PasswordErrada123!"  |

  @CA-01.4 @RF-01 @RN-32 @MUST @critico @borda
  Cenário: Bloqueio da conta após 5 tentativas falhadas consecutivas
    Dado que a conta "utilizador@example.test" possui 4 tentativas falhadas
    Quando envia POST "/api/v1/auth/login" com senha incorreta "PasswordErrada123!"
    Então o status é 401, código "ERR-AUTH-001", conta "LOCKED", Redis IF-04.2, revoga famílias IF-01.4, EV-03 e auditoria "USER_LOCKED"

  @CA-01.5 @RF-01 @RN-31 @MUST @borda
  Cenário: Reativação automática de conta após expiração do período de bloqueio
    Dado que a conta "utilizador@example.test" possui status "LOCKED" com locked_until expirado
    Quando envia POST "/api/v1/auth/login" com senha correta "PasswordSegura123!"
    Então o status é 200, conta passa para "ACTIVE" com locked_until nulo e falhas zeradas

  @CA-01.6 @RF-01 @RN-33 @MUST @critico @borda
  Cenário: Rejeição de utilizador suspenso com senha correta
    Dado que o utilizador "utilizador@example.test" possui status "SUSPENDED"
    Quando envia POST "/api/v1/auth/login" com senha correta "PasswordSegura123!"
    Então o status é 403 e código "ERR-AUTH-004", sem alterar falhas

  @CA-01.7 @RF-01 @RN-34 @MUST @critico @borda
  Cenário: Rollback total na falha de persistência durante autenticação
    Dado que ocorre falha na gravação da auditoria durante a autenticação
    Quando envia POST "/api/v1/auth/login" com credenciais válidas
    Então o status é 500, código "ERR-SYS-001" e nada é persistido na BD
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-01 | MUST | RN-01 a RN-03, RN-30 a RN-34 | EP-01, EV-03, IF-02 | EX-01.1 a EX-01.5 | CA-01.1 a CA-01.7 | F-01 |

**Totais:** RF = 1 · RN = 8 · EP = 1 · EV = 1 · CA = 7 (`@critico` = 4) · Σw = 21

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-110 | Validação de senha aplica 4 classes sem trim | Cumprir PRS-30 | PO, 2026-11-02 (D-23) |
| PRS-111 | Resposta 401 para conta LOCKED mantém tempo constante | Mitigar enumeração (D-08) | PO, 2026-11-02 (D-23) |
| PRS-112 | EV-03 e revogação de famílias ocorrem no 5º erro | Isolar conta sob ataque | PO, 2026-11-02 (D-23) |

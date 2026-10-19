# M-01 · Login

> **Conjunto:** [00](../00-indice.md) · **Dono:** AR (UT-3-01) · **Estado:** Rascunho · **Atualizado:** 2026-10-19
> **Pacote:** este doc + `10` + `30` (ENT-04, ENUM-04, AUD.M01) + `40` (§1..§3, EP-01, EV-03, IF-01.1/4, IF-02/2.1, IF-03.1, IF-04.2, IF-06.1, IF-07.3, IF-09.1/6, matriz §5) + `70` (§1) + `90` (UT-3-01, PRS-30..32). Total: ~54 KiB (teto ≤ 60 KiB).

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
| RF / RN | RF-01 / RN-10 a RN-17 |
| Permissão | Pública (`Authorization` ignorado; sem token obrigatório) |
| Idempotência | Não. Sucesso gera nova sessão; falha incrementa contador |

**Request:** `email` (body string); `password` (body string).
```json
{"email":"utilizador@example.test","password":"PasswordSegura123!"}
```

**Ordem de avaliação:** 1. Rota/método → 404/405. 2. Tamanho ≤ 64 KiB → 413. 3. Content-Type `application/json` → 415. 4. Chave/BD acessíveis → 503 `ERR-SYS-002`. 5. JSON válido, objeto, sem extras, `email`/`password` presentes e não nulos (RN-10) → 400 `ERR-VAL-001`. 6. Formato `email` (5..320 chars, RFC 5322) e `password` (12..128 chars, 4 classes sem trim) (RN-11) → 422 `ERR-VAL-003`. 7. Normalização `email`: `trim().ToLowerInvariant()` e pesquisa em `user_account` (RN-12). 8. Se utilizador inexistente: Argon2id contra hash fictício e auditoria `LOGIN_FAILED` sem escrita → 401 `ERR-AUTH-001` (RN-13, RNF-01). 9. Se `status = LOCKED` com `locked_until > agora`: Argon2id contra `password_hash` e auditoria `LOGIN_FAILED` sem alterar BD → 401 `ERR-AUTH-001` (RN-13, RNF-01, PRS-83). 10. Se `status = LOCKED` com `locked_until ≤ agora`: passa a `ACTIVE`, falhas 0, `locked_until = NULL` na mesma transação antes de checar senha (RN-14, D-08). 11. Verificação da senha com Argon2id contra `password_hash` (RN-13, RNF-01). 12. Se senha incorreta: (a) se `status = SUSPENDED`, auditoria `LOGIN_FAILED` sem somar falhas → 401 `ERR-AUTH-001` (RN-15, PRS-83); (b) se `status = ACTIVE` e falhas + 1 < 5, incrementa falhas e auditoria `LOGIN_FAILED` → 401 `ERR-AUTH-001` (RN-15); (c) se `status = ACTIVE` e falhas + 1 >= 5, passa a `LOCKED`, `locked_until = agora + 900 s`, `denylist:user` via IF-04.2, revoga famílias via IF-01.4, EV-03 via IF-07.3, auditoria `USER_LOCKED` → 401 `ERR-AUTH-001` (RN-15, PRS-31, PRS-32). 13. Se senha correta mas `status = SUSPENDED`: avaliado após senha correta, não altera BD → 403 `ERR-AUTH-004` (RN-16, RG-05, D-08, D-11). 14. Se senha correta e `status = ACTIVE`: zera falhas, `locked_until = NULL`, `updated_at = agora`, família via IF-01.1, token via IF-06.1 (`view_mode = NONE`), contextos via IF-09.6, auditoria `LOGIN_SUCCESS`, commit único → 200 OK (RN-17, RNF-03).

**Respostas:** 200; 400 `ERR-VAL-001`; 401 `ERR-AUTH-001`; 403 `ERR-AUTH-004`; 404 `ERR-REQ-001`; 405 `ERR-REQ-002`; 413 `ERR-REQ-003`; 415 `ERR-REQ-004`; 422 `ERR-VAL-003`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Sucesso (200):** `access_token` (`view_mode = NONE`), `token_type` (`"Bearer"`), `expires_in` (900), `refresh_token` (43 chars), `refresh_expires_in` (604800), `user` (`id`, `email`, `name`, `platform_role`), `available_contexts` (array ordenado RG-10). Transação atómica via `IdentityDbContext`.

### EV-03 `usuario_bloqueado`
Publicado na outbox por IF-07.3 quando a conta atinge 5 falhas consecutivas de login (RN-15).
| Campo | Tipo | Descrição |
|---|---|---|
| `event_id` / `user_id` | UUID v7 / UUID | Identificador do evento outbox / utilizador bloqueado |
| `status` / `locked_until` | string / string | `"LOCKED"` / Timestamp ISO 8601 UTC (`agora + 900 s`) |
| `reason` / `blocked_at` | string / string | `"Excesso de tentativas consecutivas de login com palavra-passe incorreta"` (PRS-60) / Timestamp ISO 8601 UTC |

### IF-02 `IUserAccountStatusWriter`
- **IF-02.1 `SetAdministrativeStatusAsync(Guid userId, UserStatus status)`:** Altera status para `ACTIVE` ou `SUSPENDED`. Zera falhas, define `locked_until = NULL` e atualiza `updated_at`. Devolve status anterior ou `null` se inexistente. Rejeita `LOCKED` com `ArgumentException`.

## 3. Requisitos funcionais

### RF-01 — Autenticar utilizador e listar contextos
| Campo | Valor |
|---|---|
| Prioridade / Ator | MUST / Utilizador anónimo envia pedido para `POST /api/v1/auth/login` |
| Pré-condições / RN | Nenhuma (endpoint público) / RN-10 a RN-17 |

**Fluxo principal:** 1. `POST /api/v1/auth/login`. 2. Valida transporte e formato (RN-10, RN-11). 3. Normaliza e pesquisa e-mail (RN-12). 4. Se bloqueio expirado, reativa conta (RN-14). 5. Valida senha com Argon2id (RN-13). 6. Se credencial válida e conta `ACTIVE`, zera falhas (RN-17). 7. Cria família via IF-01.1, emite token inicial via IF-06.1 e consulta contextos via IF-09.6. 8. Em transação única grava alterações em `user_account`, sessão e auditoria `LOGIN_SUCCESS`. 9. Retorna 200 com tokens e contextos.

**Fluxos de exceção:** EX-01.1 (formato inválido → 400); EX-01.2 (política violada → 422); EX-01.3 (e-mail inexistente, senha errada ou conta `LOCKED` → 401 com Argon2id e auditoria); EX-01.4 (senha correta em conta `SUSPENDED` → 403); EX-01.5 (falha de dependência → 503, rollback).

**Concorrência e idempotência:** Falhas simultâneas que atingem limite comutam para `LOCKED` uma vez com chave no Redis, revogação e EV-03 (CA-01.4). Sucesso concorrente com falha zera contador se comitar por último (CA-01.5). Falha de dependência executa rollback sem resíduos (CA-01.7).

**Pós-condições:** Em sucesso, sessão `ACTIVE`, falhas zeradas e auditoria registada. Em bloqueio, conta `LOCKED`, chave no Redis, famílias revogadas e evento na outbox.

## 4. Regras de negócio
| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-10 | Validação | **SE** JSON inválido, não objeto raiz, campos desconhecidos ou `email`/`password` ausentes/nulos **ENTÃO** 400 **SENÃO** segue | Objeto estrito | `ERR-VAL-001` | RF-01 | `40` §2 |
| RN-11 | Validação | **SE** `email` fora de 5..320 chars ou violar RFC 5322 OU `password` fora de 12..128 chars com 4 classes sem trim **ENTÃO** 422 **SENÃO** segue | 12..128 chars | `ERR-VAL-003` | RF-01 | PRS-30, PRS-80 |
| RN-12 | Normalização | **SE** pedido recebido **ENTÃO** aplica `trim().ToLowerInvariant()` no `email` antes de buscar em `user_account` **SENÃO** rejeita | `btrim`, `lower` | – | RF-01 | PRS-11, `30` ENT-04 |
| RN-13 | Proteção Temporal | **SE** e-mail inexistente, conta `LOCKED` ativa ou senha incorreta **ENTÃO** executa Argon2id (`m=19456`, `t=2`, `p=1`) e responde 401 **SENÃO** segue | Hash constante | `ERR-AUTH-001` | RF-01 | RNF-01, D-08 |
| RN-14 | Temporal | **SE** conta `LOCKED` e `locked_until <= agora` **ENTÃO** passa a `ACTIVE`, zera falhas e `locked_until = NULL` na mesma transação antes de checar senha **SENÃO** segue | Expiração bloqueio | – | RF-01 | D-08, UT-3-01 |
| RN-15 | Integridade | **SE** senha incorreta em conta `ACTIVE` **ENTÃO** soma falha; ao atingir 5, passa a `LOCKED`, `locked_until = agora + 900 s`, `denylist:user`, revoga famílias e emite EV-03 **SENÃO** soma falha | Limite 5, 900 s | `ERR-AUTH-001` | RF-01 | PRS-31, PRS-32, D-09 |
| RN-16 | Autorização | **SE** senha correta mas `status = SUSPENDED` **ENTÃO** responde 403 sem alterar falhas **SENÃO** segue | `status = SUSPENDED` | `ERR-AUTH-004` | RF-01 | RG-05, D-08, D-11 |
| RN-17 | Integridade | **SE** senha correta e conta `ACTIVE` **ENTÃO** zera falhas, `locked_until = NULL`, emite sessão via IF-01.1 e token via IF-06.1 em transação atómica **SENÃO** segue | Transação única | – | RF-01 | RNF-03, UT-3-01 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-01 — Autenticar utilizador e listar contextos
  Contexto:
    Dado que existe o utilizador "utilizador@example.test" ativo com senha "PasswordSegura123!"
    E que a derivação de chave utiliza parâmetros Argon2id com m=19456, t=2 e p=1

  @CA-01.1 @RF-01 @RN-10 @RN-12 @RN-17 @MUST @RNF-03
  Cenário: Autenticação com sucesso e emissão de sessão inicial
    Quando envia POST "/api/v1/auth/login" com corpo {"email":" Utilizador@Example.Test ","password":"PasswordSegura123!"}
    Então o status é 200, "token_type" é "Bearer", "expires_in" é 900, "view_mode" é "NONE", falhas 0, auditoria "LOGIN_SUCCESS" registada e executa em transação única (RNF-03)

  @CA-01.2 @RF-01 @RN-11 @MUST @borda
  Esquema do Cenário: Rejeição por formato de e-mail ou política de palavra-passe
    Quando envia POST "/api/v1/auth/login" com email <email_teste> e senha <senha_teste>
    Então o status é 422 e o código é "ERR-VAL-003"
    Exemplos:
      | email_teste               | senha_teste           |
      | "invalido"                | "PasswordSegura123!"  |
      | "a@b"                     | "PasswordSegura123!"  |
      | "utilizador@example.test" | "fraca"               |
      | "utilizador@example.test" | "SemDigitoOuEspecial" |
      | "utilizador@example.test" | "SemEspecialNem123"   |

  @CA-01.3 @RF-01 @RN-13 @MUST @critico @borda @RNF-01
  Esquema do Cenário: Proteção de enumeração e credenciais inválidas
    Quando envia POST "/api/v1/auth/login" com email <email_cenario> e senha <senha_cenario>
    Então o status é 401, o código é "ERR-AUTH-001", executa Argon2id (RNF-01) e gera auditoria "LOGIN_FAILED"
    Exemplos:
      | email_cenario              | senha_cenario         |
      | "inexistente@example.test" | "PasswordSegura123!"  |
      | "utilizador@example.test"  | "PasswordErrada123!"  |

  @CA-01.4 @RF-01 @RN-15 @MUST @critico @borda
  Cenário: Bloqueio da conta após 5 tentativas falhadas consecutivas
    Dado que a conta "utilizador@example.test" possui exatamente 4 tentativas consecutivas falhadas
    Quando envia POST "/api/v1/auth/login" com senha incorreta "PasswordErrada123!"
    Então o status é 401, código "ERR-AUTH-001", conta passa a "LOCKED" com locked_until em agora + 900 s, chave no Redis via IF-04.2, famílias revogadas via IF-01.4, EV-03 emitido e auditoria "USER_LOCKED" gravada

  @CA-01.5 @RF-01 @RN-14 @MUST @borda
  Cenário: Reativação automática de conta após expiração do período de bloqueio
    Dado que a conta "utilizador@example.test" possui status "LOCKED" com locked_until expirado no passado
    Quando envia POST "/api/v1/auth/login" com senha correta "PasswordSegura123!"
    Então o status é 200, a conta passa para "ACTIVE" com locked_until nulo e falhas zeradas e nova sessão é emitida

  @CA-01.6 @RF-01 @RN-16 @MUST @critico @borda
  Cenário: Rejeição de utilizador suspenso com senha correta
    Dado que o utilizador "utilizador@example.test" possui status "SUSPENDED"
    Quando envia POST "/api/v1/auth/login" com senha correta "PasswordSegura123!"
    Então o status é 403 e código "ERR-AUTH-004", avaliado após senha correta sem alterar falhas

  @CA-01.7 @RF-01 @MUST @critico @borda
  Cenário: Rollback total na falha de persistência durante autenticação
    Dado que ocorre falha na gravação da auditoria durante a autenticação
    Quando envia POST "/api/v1/auth/login" com credenciais válidas
    Então o status é 500, código "ERR-SYS-001" e nada é persistido na base de dados
```

## 6. Rastreabilidade do módulo
| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-01 | MUST | RN-10 a RN-17 | EP-01, EV-03, IF-02 | EX-01.1 a EX-01.5 | CA-01.1 a CA-01.7 | F-00 |

**Totais:** RF = 1 · RN = 8 · EP = 1 · EV = 1 · CA = 7 (`@critico` = 4) · Σw = 17

## 7. Pressupostos do módulo
| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-110 | Validação de senha aplica 4 classes sem trim | Cumprir PRS-30 | PO, 2026-10-19 |
| PRS-111 | Resposta 401 para conta LOCKED mantém tempo constante | Mitigar enumeração (D-08) | PO, 2026-10-19 |
| PRS-112 | EV-03 e revogação de famílias ocorrem no 5º erro | Isolar conta sob ataque | PO, 2026-10-19 |

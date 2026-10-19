# M-07 · Chaves

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR · **Estado:** Proposta (UT-3-07) · **Atualizado em:** 2026-10-19
> **Pacote de contexto deste módulo:** este ficheiro + `10` (base + extra §3.7) + `30` (comum) + `40` (§1, §2, §3, EP-10, IF-05.1, IF-06, IF-06.1 e tipos partilhados) + `70` (comum) + `90` (UT-3-07, PRS-44). Tamanho total: 37.0 KiB (orçamento: 60 KiB).

## 1. Âmbito do módulo

| Dentro | Fora (outro módulo ou fora do sistema) |
|---|---|
| Exposição de `GET /.well-known/jwks.json` (EP-10) com chave pública RSA ativa | Leitura ou rotação física de ficheiro PEM da chave privada (M-00) |
| Implementação da interface interna `ITokenIssuer` (`IF-06`) | Validação de tokens de acesso em requisições HTTP (M-00) |
| Assinatura digital de tokens de acesso JWT com RS256 | Assinatura de eventos Kafka `context_jws` (M-14) |
| Geração de `jti` em UUID v7 minúsculo formato D | Persistência de sessões ou contas em base de dados (M-01, M-02, M-11) |
| Cálculo de `iat` e `exp = iat + 900` via `TimeProvider` | Registo de auditoria em `auth_audit_log` (M-01, M-02, M-03, M-06) |
| Ordenação ordinal ascendente de claims em lista | Revogação de tokens via denylist no Redis (M-00, M-02) |
| Validação de coerência de claims por `ViewMode` e personificação | Resolução de permissões e vínculos (M-08) |

**Escreve:** Nenhuma · **Lê:** Nenhuma (chave via `ISigningKey`) · **Usa interfaces de:** M-00 (`IF-05`)

## 2. Endpoints e eventos (detalhe)

### EP-10 `GET /.well-known/jwks.json`

| Campo | Valor |
|---|---|
| RF | RF-11 |
| Autenticação / Permissão | Pública (permitido sem token; cabeçalho `Authorization` ignorado se enviado) |
| Idempotência | Sim (método HTTP GET de leitura, sem mutação de estado) |
| RN aplicáveis | RN-90, RN-91, RN-92 |

**Request**
Nenhum campo no corpo, na rota ou na query string (ignorados conforme R1 e PRS-55).

| Campo | Local | Tipo | Obrigatório | Validação |
|---|---|---|---|---|
| `X-Correlation-Id` | header | string | Não | UUID formato D; ausente ou inválido gera UUID v7 |

**Ordem de avaliação**

| # | Verificação | Camada (`40` §2) | Se falhar |
|---|---|---|---|
| 1 | Rota e método HTTP (GET `/.well-known/jwks.json`) | 0 (Encaminhamento) | 404 `ERR-REQ-001` ou 405 `ERR-REQ-002` |
| 2 | Transporte a Existência (público, sem corpo nem parâmetros; query ignorada via PRS-55) | 1 a 5 | Camadas não aplicadas |
| 3 | Obtenção da chave de `ISigningKey` (`IF-05.1`); falha ou indisponibilidade | 6 (Regras de negócio) | 503 `ERR-SYS-002` |
| 4 | Composição e serialização do JWKS com exatamente 1 chave RSA sem preenchimento | 6 (Regras de negócio) | 200 OK |

**Respostas**

| Status | Condição | Código |
|---|---|---|
| 200 | Chave pública obtida com sucesso de `ISigningKey` | – |
| 404 | Rota inexistente | `ERR-REQ-001` |
| 405 | Método HTTP diferente de GET | `ERR-REQ-002` |
| 500 | Exceção inesperada não tratada | `ERR-SYS-001` |
| 503 | Chave criptográfica indisponível ou falha em `ISigningKey` | `ERR-SYS-002` |

**Corpo de sucesso**

| Campo | Tipo | Sempre presente | Valor / regra de cálculo | Ordenação |
|---|---|---|---|---|
| `keys` | array | Sim | Array de chaves públicas contendo exatamente 1 elemento | Não se aplica |
| `keys[0]` | object | Sim | JWK RSA com `kty="RSA"`, `use="sig"`, `alg="RS256"`, `kid=JWT_KEY_ID`, `n` e `e="AQAB"` em base64url | Não se aplica |

Cabeçalhos HTTP na resposta 200: `Content-Type: application/json; charset=utf-8`, `Cache-Control: public, max-age=300` (PRS-44), `X-Correlation-Id: <correlation_id>`.

```json
{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"key-2026-01","n":"u1II2IjT0n4lM_x3V6ZJ9aBcDeFgHiJkLmNoPqRsTuVwXyZ","e":"AQAB"}]}
```

**Efeitos colaterais (por ordem):** Nenhum (leitura em memória).
**Transacionalidade:** Não se aplica (sem persistência).

---

### IF-06 `ITokenIssuer.Issue`

Interface consumida por M-01, M-02, M-03 e M-06 para emissão de tokens de acesso.

| Campo | Valor |
|---|---|
| Assinatura | `IssuedToken Issue(AccessTokenClaims claims)` |
| Localização | Pasta `src/IdentityService/Modules/M07Chaves/Interface/` |
| Dependência | `ISigningKey` (`IF-05.1`) em `M00Transversal/Auth/` |
| Idempotência | Não (cada chamada gera um `jti` UUID v7 único e novo `iat`) |
| RN aplicáveis | RN-93, RN-94, RN-95, RN-96, RN-97, RN-98, RN-99 |

**Parâmetros de entrada (`AccessTokenClaims`)**

| Campo | Tipo | Nulidade | Regra de validação |
|---|---|---|---|
| `Sub` | `Guid` | Não | Titular do token (em personificação: utilizador alvo) |
| `FamilyId` | `Guid?` | Sim | Obrigatório se `Act == null`; proibido se `Act != null` |
| `PlatformRole` | `PlatformRole` | Não | Enum (`NONE`, `SUPPORT_AGENT`, `PLATFORM_ADMIN`) |
| `ViewMode` | `ViewMode` | Não | Enum (`NONE`, `SINGLE`, `MACRO_GROUP`) |
| `GroupId`, `TenantId` | `Guid?` | Sim | `GroupId` em `SINGLE`/`MACRO_GROUP`; `TenantId` só em `SINGLE` |
| `AllowedTenants` | `IReadOnlyList<Guid>` | Não | `[]` em `NONE`; `[TenantId]` em `SINGLE`; 1 a 50 em `MACRO_GROUP` |
| `ActiveScope` | `OperationalScope?` | Sim | `null` em `NONE`; obrigatório em `SINGLE` e `MACRO_GROUP` |
| `Roles`, `Permissions` | `IReadOnlyList<string>` | Não | `[]` em `NONE`; listas ordenadas em `SINGLE` e `MACRO_GROUP` |
| `Act` | `ActorClaim?` | Sim | `ActorClaim(Sub, TicketId)` em personificação; `null` fora |

**Ordem de avaliação**

| # | Verificação | Origem | Se falhar |
|---|---|---|---|
| 1 | Parâmetro `claims` não nulo | Contrato | `ArgumentNullException` (500 `ERR-SYS-001`) |
| 2 | Invariantes de `ViewMode` (`NONE`, `SINGLE`, `MACRO_GROUP`) | RN-95 | `ArgumentException` (500 `ERR-SYS-001`) |
| 3 | Invariantes de personificação (`Act` e `FamilyId`) | RN-96 | `ArgumentException` (500 `ERR-SYS-001`) |
| 4 | Obtenção da chave de `ISigningKey` (`IF-05.1`) | RN-99 | `CryptographicException` (503 `ERR-SYS-002`) |
| 5 | Obtenção de `iat` via `TimeProvider`, `exp = iat + 900` e `jti` UUID v7 | RN-93, RN-94 | Exceção não prevista (500 `ERR-SYS-001`) |
| 6 | Ordenação ascendente de `AllowedTenants`, `Roles` e `Permissions` | RN-97 | – |
| 7 | Montagem de cabeçalho JOSE, claims e assinatura RS256 | RN-98 | `CryptographicException` (503 `ERR-SYS-002`) |
| 8 | Retorno do registo imutável `IssuedToken` | Sucesso | Retorno do valor |

**Respostas / Resultados**

| Tipo de resultado | Condição | Ação / Exceção |
|---|---|---|
| `IssuedToken` preenchido | Claims válidas e chave privada disponível | Retorna `IssuedToken` com token compacto assinado e claims normalizadas |
| `ArgumentNullException` | Parâmetro `claims` nulo | Lança exceção propagada para o tratador global (500 `ERR-SYS-001`) |
| `ArgumentException` | Violação de composição de claims (RN-95, RN-96) | Lança exceção propagada para o tratador global (500 `ERR-SYS-001`) |
| `CryptographicException` | Chave privada inacessível ou falha criptográfica (RN-99) | Lança exceção propagada para o tratador global (503 `ERR-SYS-002`) |

**Estrutura do token gerado**
- Cabeçalho: `{"alg": "RS256", "typ": "JWT", "kid": "key-2026-01"}`
- Payload: `jti` (UUID v7 D), `sub` (UUID D), `fid` (UUID D; omitida se `Act != null`), `iat` (Unix s), `exp` (`iat + 900`), `iss` (`JWT_ISSUER`), `aud` (`JWT_AUDIENCE`), `platform_role`, `view_mode`, `group_id` (UUID D ou `null`), `tenant_id` (UUID D ou `null`), `allowed_tenants` (array de UUID D ordenado), `active_scope` (texto ou `null`), `roles` (array ordenado ascendente), `permissions` (array ordenado ascendente), `act` (`{"sub", "ticket_id"}` se personificação; omitida se `Act == null`).

**Efeitos colaterais (por ordem):** Nenhum (operação em memória).
**Transacionalidade:** Não se aplica (sem persistência).

---

### EV Eventos Kafka

O módulo M-07 não publica nem consome tópicos Kafka. Total de eventos: EV = 0.

## 3. Requisitos funcionais

### RF-11 — Publicar JWKS e emitir tokens de acesso

| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Cliente HTTP externo em EP-10; módulos internos autenticadores em `ITokenIssuer.Issue` (IF-06) |
| Pré-condições | `ISigningKey` registada no contentor de dependências com chave RSA 2048 bits; `TimeProvider` registado como singleton |
| RN | RN-90, RN-91, RN-92, RN-93, RN-94, RN-95, RN-96, RN-97, RN-98, RN-99 |

**Fluxo principal:**
1. No EP-10, o serviço recebe `GET /.well-known/jwks.json` e obtém material de chave pública de `ISigningKey` (`IF-05.1`).
2. O endpoint monta o JWKS com `keys` contendo 1 chave RSA (`kty="RSA"`, `use="sig"`, `alg="RS256"`, `kid=JWT_KEY_ID`, `n`, `e` em base64url sem `=`).
3. O endpoint anexa `Cache-Control: public, max-age=300` e `X-Correlation-Id`, devolvendo status 200 OK.
4. Em `ITokenIssuer.Issue` (IF-06), a operação recebe `AccessTokenClaims` e valida conformidade com `ViewMode` e `Act`.
5. A operação obtém `iat` via `TimeProvider` (segundos Unix) e calcula `exp = iat + 900`.
6. A operação gera novo `jti` em formato UUID v7 minúsculo formato D.
7. A operação ordena ascendentemente as coleções `AllowedTenants`, `Roles` e `Permissions`.
8. A operação monta o cabeçalho JOSE (`alg="RS256"`, `typ="JWT"`, `kid=KeyId`) e assina o JWT com a chave privada RSA 2048 bits de `ISigningKey`.
9. A operação devolve `IssuedToken` com token compacto assinado, `jti`, `iat`, `exp` e as claims ordenadas.

**Fluxos de exceção**

| ID | Passo | Condição | Resposta | Efeitos / estado final |
|---|---|---|---|---|
| EX-11.1 | 1 | `ISigningKey` indisponível ou falha criptográfica em EP-10 | 503 `ERR-SYS-002` | Nenhum dado emitido |
| EX-11.2 | 1 | Rota inexistente ou método diferente de GET em EP-10 | 404 / 405 | Rejeição pelo pipeline (`ERR-REQ-001` / `ERR-REQ-002`) |
| EX-11.3 | 4 | Claims fornecidas a `ITokenIssuer.Issue` violam regras de compatibilidade | Lança `ArgumentException` | Rollback do chamador com 500 `ERR-SYS-001` |
| EX-11.4 | 8 | `ISigningKey` indisponível ou falha criptográfica em `ITokenIssuer.Issue` | Lança `CryptographicException` | Rollback do chamador com 503 `ERR-SYS-002` |

**Concorrência, idempotência e retomada**

| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Múltiplos `GET /.well-known/jwks.json` simultâneos | Todos devolvem 200 com a mesma chave pública; execução em memória sem contenção | CA-11.1 |
| Múltiplas chamadas simultâneas a `ITokenIssuer.Issue` | Cada chamada opera isolada em memória, gera `jti` exclusivo e devolve token válido | CA-11.3, CA-11.4 |
| Processo terminado a meio | Operação em memória sem escrita em base de dados ou Redis; nenhum resíduo de estado | CA-11.2, CA-11.8 |
| Indisponibilidade transitória de `ISigningKey` | EP-10 responde 503 `ERR-SYS-002`; `ITokenIssuer.Issue` propaga exceção convertida em 503 | CA-11.2, CA-11.8 |

**Pós-condições:**
- EP-10: Chave pública RSA ativa disponibilizada publicamente sem exposição de dados privados.
- IF-06: Token JWT RS256 emitido com identificador único `jti`, carimbos temporais válidos, claims normalizadas e assinatura digital válida.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-90 | Integridade | **SE** EP-10 for invocado **ENTÃO** `keys` **DEVE** ter 1 JWK RSA (`kty="RSA"`, `use="sig"`, `alg="RS256"`, `kid=JWT_KEY_ID`, `n`, `e` base64url sem `=`) **SENÃO** chave não é publicada | `ALG = "RS256"` | – | RF-11 | ADR-06, UT-3-07, RFC 7517, RFC 7518 |
| RN-91 | Temporal | **SE** EP-10 responder 200 **ENTÃO** `Cache-Control` **DEVE** ser `public, max-age=300` **SENÃO** cabeçalho omitido | `MAX_AGE = 300 s` | – | RF-11 | PRS-44, D-10 |
| RN-92 | Integridade | **SE** `ISigningKey` falhar em EP-10 **ENTÃO** **DEVE** responder 503 `ERR-SYS-002` **SENÃO** responde 200 | – | `ERR-SYS-002` | RF-11 | C6, UT-3-07, CR-11, PRS-16 |
| RN-93 | Temporal | **SE** `ITokenIssuer.Issue` for invocado **ENTÃO** `iat` **DEVE** ser obtido de `TimeProvider` e `exp = iat + 900` **SENÃO** token não é emitido | `LIFETIME = 900 s` | – | RF-11 | 10 §3.5, PRS-20 |
| RN-94 | Integridade | **SE** `ITokenIssuer.Issue` for invocado **ENTÃO** `jti` **DEVE** ser novo UUID v7 em minúsculas formato D **SENÃO** token não é emitido | `UUID_VERSION = 7` | – | RF-11 | 10 §3.3, 10 §3.5, 40 §4.3 |
| RN-95 | Validação | **SE** `ViewMode` for violado (`NONE` com contexto, `SINGLE` sem `TenantId` ou `AllowedTenants` diferente de `[TenantId]`, `MACRO_GROUP` com `TenantId` ou `AllowedTenants.Count` fora de 1..50) **ENTÃO** `ITokenIssuer.Issue` **DEVE** lançar `ArgumentException` **SENÃO** validação de modo é aprovada | `MIN = 1, MAX = 50` | `ERR-SYS-001` | RF-11 | ADR-02, ADR-04, 10 §3.5, 40 §4.3 |
| RN-96 | Validação | **SE** personificação for inconsistente (`Act != null` com `FamilyId != null`, ou `Act == null` com `FamilyId == null`) **ENTÃO** `ITokenIssuer.Issue` **DEVE** lançar `ArgumentException`, omitindo `fid` se `Act != null` e incluindo `fid` se `Act == null` **SENÃO** validação é aprovada | – | `ERR-SYS-001` | RF-11 | ADR-08, 10 §3.5, 40 §4.3 |
| RN-97 | Cálculo | **SE** `ITokenIssuer.Issue` emitir token **ENTÃO** `AllowedTenants` (por texto minúsculo), `Roles` (ordinal) e `Permissions` (ordinal) **DEVEM** ser ordenados ascendentemente **SENÃO** ordenação não é aplicada | – | – | RF-11 | 10 §3.3, PRS-24, 40 §4.3 |
| RN-98 | Integridade | **SE** token JWT for emitido **ENTÃO** cabeçalho **DEVE** conter `alg="RS256"`, `typ="JWT"` e `kid=KeyId`, assinado com chave RSA 2048 bits de `ISigningKey` **SENÃO** token não é emitido | `RSA_BITS = 2048` | – | RF-11 | ADR-06, 10 §3.5, IF-05.1, UT-3-07 |
| RN-99 | Integridade | **SE** `ISigningKey` falhar em `ITokenIssuer.Issue` **ENTÃO** operação **DEVE** propagar exceção criptográfica para conversão em 503 `ERR-SYS-002` **SENÃO** token é devolvido | – | `ERR-SYS-002` | RF-11 | C5, C6, PRS-16, CR-11 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-11 — Publicar JWKS e emitir tokens de acesso

  Contexto:
    Dado que o serviço possui ISigningKey com KeyId "key-2026-01" e par de chaves RSA de 2048 bits
    E que o TimeProvider injetado indica o instante Unix 1776600000

  @CA-11.1 @RF-11 @RN-90 @RN-91 @MUST
  Cenário: Leitura pública do JWKS com sucesso ignorando query e corpo
    Quando é enviado GET "/.well-known/jwks.json" com query "?origem=gw" e corpo "ignorado"
    Então o status da resposta é 200
    E o cabeçalho "Content-Type" contém "application/json; charset=utf-8"
    E o cabeçalho "Cache-Control" é "public, max-age=300"
    E a propriedade "keys" tem 1 elemento com "kty" "RSA", "use" "sig", "alg" "RS256", "kid" "key-2026-01", "n" sem "=" e "e" "AQAB"

  @CA-11.2 @RF-11 @RN-92 @MUST @critico
  Esquema do Cenário: Falha de dependência da chave pública ou rota incorreta em EP-10
    Quando é enviado pedido HTTP para <path> com método <metodo> e estado da chave <estado_chave>
    Então o status da resposta é <status>
    E o campo "code" é "<codigo>"
    Exemplos:
      | path                      | metodo | estado_chave  | status | codigo       |
      | "/.well-known/jwks.json"  | "GET"  | "INDISPONIVEL"| 503    | ERR-SYS-002  |
      | "/.well-known/jwks.json"  | "POST" | "DISPONIVEL"  | 405    | ERR-REQ-002  |
      | "/.well-known/jwks-outro" | "GET"  | "DISPONIVEL"  | 404    | ERR-REQ-001  |

  @CA-11.3 @RF-11 @RN-93 @RN-94 @RN-97 @RN-98 @MUST
  Cenário: Emissão de token com ViewMode NONE via IF-06
    Dado uma emissão com Sub "0192e2c0-0001-7000-8000-000000000001", FamilyId "0192e2c0-0002-7000-8000-000000000002", PlatformRole "NONE", ViewMode "NONE" e Act null
    Quando ITokenIssuer.Issue é executado
    Então o IssuedToken tem Jti UUID v7, Iat 1776600000 e Exp exatamente 1776600900
    E o JWT decodificado contém alg "RS256", typ "JWT", kid "key-2026-01" e fid "0192e2c0-0002-7000-8000-000000000002"
    E o JWT decodificado contém group_id null, tenant_id null, allowed_tenants [], active_scope null, roles [], permissions [] e sem claim "act"

  @CA-11.4 @RF-11 @RN-95 @RN-97 @RN-98 @MUST
  Cenário: Emissão de token com ViewMode SINGLE e ordenação de coleções via IF-06
    Dado uma emissão com Sub "0192e2c0-0001-7000-8000-000000000001", FamilyId "0192e2c0-0002-7000-8000-000000000002", ViewMode "SINGLE", GroupId "0192e2c0-0010-7000-8000-000000000010", TenantId "0192e2c0-0020-7000-8000-000000000020", AllowedTenants ["0192e2c0-0020-7000-8000-000000000020"], ActiveScope "BUYER", Roles ["role_z", "role_a"], Permissions ["perm:b", "perm:a"] e Act null
    Quando ITokenIssuer.Issue é executado
    Então o JWT decodificado contém view_mode "SINGLE", group_id "0192e2c0-0010-7000-8000-000000000010", tenant_id "0192e2c0-0020-7000-8000-000000000020" e allowed_tenants ["0192e2c0-0020-7000-8000-000000000020"]
    E o JWT decodificado contém roles ordenadas ["role_a", "role_z"] e permissions ordenadas ["perm:a", "perm:b"]
    E o registo Claims de IssuedToken contém Roles e Permissions ordenados de forma idêntica

  @CA-11.5 @RF-11 @RN-95 @RN-97 @MUST
  Esquema do Cenário: Emissão de token com ViewMode MACRO_GROUP nas fronteiras de tenants via IF-06
    Dado uma emissão com ViewMode "MACRO_GROUP", GroupId "0192e2c0-0010-7000-8000-000000000010", TenantId null e ActiveScope "SUPPLIER"
    E a lista AllowedTenants contém <quantidade> tenants distintos
    Quando ITokenIssuer.Issue é executado
    Então o JWT decodificado contém view_mode "MACRO_GROUP" e tenant_id null
    E o JWT decodificado contém allowed_tenants com <quantidade> elementos ordenados ascendentemente
    Exemplos:
      | quantidade |
      | 1          |
      | 50         |

  @CA-11.6 @RF-11 @RN-96 @RN-98 @MUST @critico
  Cenário: Emissão de token em impersonation com omissão de fid e presença de claim act via IF-06
    Dado uma emissão em personificação com Sub "0192e2c0-0001-7000-8000-000000000001", FamilyId null, ViewMode "SINGLE", GroupId "0192e2c0-0010-7000-8000-000000000010", TenantId "0192e2c0-0020-7000-8000-000000000020", AllowedTenants ["0192e2c0-0020-7000-8000-000000000020"], ActiveScope "CARRIER" e Act com sub "0192e2c0-0099-7000-8000-000000000099" e ticket_id "CHAMADO-1"
    Quando ITokenIssuer.Issue é executado
    Então o JWT decodificado não contém a claim "fid"
    E o JWT decodificado contém claim "act" com sub "0192e2c0-0099-7000-8000-000000000099" e ticket_id "CHAMADO-1"
    E a assinatura do JWT é validada com sucesso através da chave pública de ISigningKey

  @CA-11.7 @RF-11 @RN-95 @RN-96 @MUST
  Esquema do Cenário: Rejeição de combinações inconsistentes de claims em ITokenIssuer.Issue
    Dado uma emissão com modo <view_mode>, tenant <tenant_id>, contagem de tenants <qtd>, fid <family_id> e personificacao <act>
    Quando ITokenIssuer.Issue é executado
    Então a operação lança ArgumentException
    Exemplos:
      | view_mode     | tenant_id | qtd | family_id | act   |
      | "NONE"        | "GUID"    | 0   | "GUID"    | "NAO" |
      | "NONE"        | "NULO"    | 1   | "GUID"    | "NAO" |
      | "SINGLE"      | "NULO"    | 1   | "GUID"    | "NAO" |
      | "SINGLE"      | "GUID"    | 0   | "GUID"    | "NAO" |
      | "SINGLE"      | "GUID"    | 2   | "GUID"    | "NAO" |
      | "MACRO_GROUP" | "GUID"    | 5   | "GUID"    | "NAO" |
      | "MACRO_GROUP" | "NULO"    | 0   | "GUID"    | "NAO" |
      | "MACRO_GROUP" | "NULO"    | 51  | "GUID"    | "NAO" |
      | "SINGLE"      | "GUID"    | 1   | "GUID"    | "SIM" |
      | "SINGLE"      | "GUID"    | 1   | "NULO"    | "NAO" |

  @CA-11.8 @RF-11 @RN-99 @MUST @critico
  Cenário: Falha de chave criptográfica durante emissão em IF-06 propaga exceção
    Dado que ISigningKey lança CryptographicException ao ser consultada
    E uma requisição de emissão com claims válidas
    Quando ITokenIssuer.Issue é executado
    Então a operação propaga CryptographicException sem mascaramento
    E o tratador global converte a falha em status 503 com código "ERR-SYS-002"
```

## 6. Rastreabilidade do módulo

| RF | Prioridade | RN | EP / EV | EX | CA | Fase |
|---|---|---|---|---|---|---|
| RF-11 | MUST | RN-90, RN-91, RN-92, RN-93, RN-94, RN-95, RN-96, RN-97, RN-98, RN-99 | EP-10 | EX-11.1, EX-11.2, EX-11.3, EX-11.4 | CA-11.1, CA-11.2, CA-11.3, CA-11.4, CA-11.5, CA-11.6, CA-11.7, CA-11.8 | F-00 |

**Totais:** RF = 1 · RN = 10 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-170 | O endpoint `GET /.well-known/jwks.json` ignora cabeçalhos de autenticação sem rejeição | Consumo irrestrito de chaves por clientes externos e proxies sem validação de token (RFC 7517) | PO, 2026-10-19 |
| PRS-171 | O método `ITokenIssuer.Issue` opera em memória de processo sem chamadas síncronas a base de dados ou Redis | Desempenho determinístico e ausência de bloqueios externos na emissão de tokens | PO, 2026-10-19 |
| PRS-172 | A chave RSA de `ISigningKey` possui tamanho fixo de 2048 bits com expoente público `AQAB` (65537) | Interoperabilidade RS256 e conformidade com ADR-06 e RFC 7518 §3.1 | PO, 2026-10-19 |

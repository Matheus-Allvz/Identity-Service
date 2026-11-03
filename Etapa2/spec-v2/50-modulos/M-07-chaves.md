# M-07 · Chaves

> **Conjunto:** [00 Índice](../00-indice.md) · **Dono:** AR · **Estado:** Congelado (Gate 4) · **Atualizado em:** 2026-11-02
> **Pacote:** este ficheiro + `10` + `30` + `40` (§1-§3, EP-10, IF-05.1, IF-06, tipos) + `70` + `90` (UT-3-07, PRS-44). Total: 37 KiB.

## 1. Âmbito do módulo

| Dentro | Fora |
|---|---|
| Exposição de `GET /.well-known/jwks.json` (EP-10) com chave RSA ativa | Rotação ou leitura física do ficheiro PEM da chave privada (M-00) |
| Implementação da interface interna `ITokenIssuer` (`IF-06`) | Validação de tokens de acesso no pipeline HTTP (M-00) |
| Assinatura digital de tokens de acesso JWT com RS256 | Assinatura de eventos Kafka `context_jws` (M-14) |
| Geração de `jti` em UUID v7 minúsculo formato D | Persistência de sessões ou contas em base de dados (M-01, M-02) |
| Cálculo de `iat` e `exp = iat + 900` via `TimeProvider` | Registo de auditoria em `auth_audit_log` (M-01, M-02, M-03, M-06) |
| Ordenação ordinal ascendente de claims em lista | Revogação de tokens via denylist no Redis (M-00, M-02) |
| Validação de coerência de claims por `ViewMode` e personificação | Resolução de permissões e vínculos (M-08) |

**Escreve:** Nenhuma · **Lê:** Nenhuma (chave via `ISigningKey`) · **Usa:** M-00 (`IF-05`)

## 2. Endpoints e eventos (detalhe)

### EP-10 `GET /.well-known/jwks.json`

| Campo | Valor |
|---|---|
| RF / RN | RF-11 / RN-90, RN-91, RN-92 |
| Permissão | Pública (`Authorization` ignorado se enviado) |
| Idempotência | Sim (método HTTP GET de leitura, sem mutação) |

**Request:** Nenhum campo no corpo, rota ou query string (ignorados conforme R1 e PRS-55). Opcional `X-Correlation-Id` no header (UUID D).

**Ordem de avaliação:**
1. Rota e método HTTP (GET `/.well-known/jwks.json`) → 404 `ERR-REQ-001` ou 405 `ERR-REQ-002`.
2. Obtenção da chave de `ISigningKey` (`IF-05.1`); falha ou indisponibilidade → 503 `ERR-SYS-002`.
3. Composição e serialização do JWKS com 1 chave RSA pública sem preenchimento → 200 OK.

**Respostas:** 200; 404 `ERR-REQ-001`; 405 `ERR-REQ-002`; 500 `ERR-SYS-001`; 503 `ERR-SYS-002`.

**Corpo de sucesso:**
```json
{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"key-2026-01","n":"u1II2IjT0n4lM_x3V6ZJ9aBcDeFgHiJkLmNoPqRsTuVwXyZ","e":"AQAB"}]}
```
Cabeçalhos na resposta 200: `Content-Type: application/json; charset=utf-8`, `Cache-Control: public, max-age=300` (PRS-44), `X-Correlation-Id: <correlation_id>`.

---

### IF-06 `ITokenIssuer.Issue`

Interface consumida por M-01, M-02, M-03 e M-06 para emissão de tokens de acesso. Assinatura: `IssuedToken Issue(AccessTokenClaims claims)`. Não idempotente (gera `jti` UUID v7 único e novo `iat`).

**Parâmetros de entrada (`AccessTokenClaims`)**
- `Sub` (`Guid`, não nulo): titular do token (alvo em impersonation).
- `FamilyId` (`Guid?`): obrigatório se `Act == null`; proibido se `Act != null`.
- `PlatformRole` (`PlatformRole`, não nulo): enum (`NONE`, `SUPPORT_AGENT`, `PLATFORM_ADMIN`).
- `ViewMode` (`ViewMode`, não nulo): enum (`NONE`, `SINGLE`, `MACRO_GROUP`).
- `GroupId`, `TenantId` (`Guid?`): `GroupId` em `SINGLE`/`MACRO_GROUP`; `TenantId` só em `SINGLE`.
- `AllowedTenants` (`IReadOnlyList<Guid>`, não nulo): `[]` em `NONE`; `[TenantId]` em `SINGLE`; 1 a 50 em `MACRO_GROUP`.
- `ActiveScope` (`OperationalScope?`): `null` em `NONE`; obrigatório em `SINGLE` e `MACRO_GROUP`.
- `Roles`, `Permissions` (`IReadOnlyList<string>`, não nulo): `[]` em `NONE`; listas ordenadas nos outros modos.
- `Act` (`ActorClaim?`): `ActorClaim(Sub, TicketId)` em impersonation; `null` fora.

**Ordem de avaliação:**
1. Parâmetro `claims` não nulo → `ArgumentNullException` (500 `ERR-SYS-001`).
2. Invariantes de `ViewMode` (RN-95) → `ArgumentException` (500 `ERR-SYS-001`).
3. Invariantes de personificação (`Act` e `FamilyId`, RN-96) → `ArgumentException` (500 `ERR-SYS-001`).
4. Obtenção da chave de `ISigningKey` (RN-99) → `CryptographicException` (503 `ERR-SYS-002`).
5. Obtenção de `iat` via `TimeProvider`, `exp = iat + 900` e `jti` UUID v7 (RN-93, RN-94).
6. Ordenação ascendente de `AllowedTenants`, `Roles` e `Permissions` (RN-97).
7. Montagem do cabeçalho JOSE, claims e assinatura RS256 (RN-98).
8. Retorno do registo imutável `IssuedToken`.

**Estrutura do token gerado:**
- Cabeçalho: `{"alg": "RS256", "typ": "JWT", "kid": "key-2026-01"}`
- Payload: `jti` (UUID v7 D), `sub` (UUID D), `fid` (UUID D; omitida se `Act != null`), `iat` (Unix s), `exp` (`iat + 900`), `iss` (`JWT_ISSUER`), `aud` (`JWT_AUDIENCE`), `platform_role`, `view_mode`, `group_id`, `tenant_id`, `allowed_tenants` (array ordenado), `active_scope`, `roles` (array ordenado), `permissions` (array ordenado), `act` (`{"sub", "ticket_id"}` se impersonation; omitida se nulo).

### EV Eventos Kafka
O módulo M-07 não publica nem consome tópicos Kafka (EV = 0).

## 3. Requisitos funcionais

### RF-11 — Publicar JWKS e emitir tokens de acesso
| Campo | Valor |
|---|---|
| Prioridade | MUST |
| Ator / Gatilho | Cliente HTTP em EP-10; módulos internos via `ITokenIssuer.Issue` (IF-06) |
| Pré-condições | `ISigningKey` registada com chave RSA 2048 bits; `TimeProvider` disponível |
| RN | RN-90, RN-91, RN-92, RN-93, RN-94, RN-95, RN-96, RN-97, RN-98, RN-99 |

**Fluxo principal:**
1. No EP-10, recebe `GET /.well-known/jwks.json` e obtém chave pública de `ISigningKey` (`IF-05.1`).
2. Monta JWKS com 1 chave RSA (`kty="RSA"`, `use="sig"`, `alg="RS256"`, `kid=JWT_KEY_ID`, `n`, `e` em base64url sem `=`).
3. Anexa `Cache-Control: public, max-age=300`, `X-Correlation-Id` e devolve 200 OK.
4. Em `ITokenIssuer.Issue` (IF-06), recebe `AccessTokenClaims` e valida conformidade com `ViewMode` e `Act`.
5. Obtém `iat` via `TimeProvider` (segundos Unix) e calcula `exp = iat + 900`.
6. Gera novo `jti` em formato UUID v7 minúsculo formato D.
7. Ordena ascendentemente `AllowedTenants`, `Roles` e `Permissions`.
8. Monta cabeçalho JOSE (`alg="RS256"`, `typ="JWT"`, `kid=KeyId`) e assina JWT com chave RSA 2048 bits de `ISigningKey`.
9. Devolve `IssuedToken` com token compacto assinado, `jti`, `iat`, `exp` e claims normalizadas.

**Fluxos de exceção:**
- EX-11.1: `ISigningKey` indisponível ou falha criptográfica em EP-10 → 503 `ERR-SYS-002`.
- EX-11.2: Rota inexistente ou método diferente de GET em EP-10 → 404/405 (`ERR-REQ-001`/`ERR-REQ-002`).
- EX-11.3: Claims fornecidas a `ITokenIssuer.Issue` violam regras de compatibilidade → `ArgumentException` (500 `ERR-SYS-001`).
- EX-11.4: `ISigningKey` indisponível em `ITokenIssuer.Issue` → `CryptographicException` (503 `ERR-SYS-002`).

**Concorrência e idempotência:**
| Situação | Comportamento obrigatório | CA |
|---|---|---|
| Múltiplos `GET /.well-known/jwks.json` simultâneos | Todos devolvem 200 com a mesma chave pública sem contenção | CA-11.1 |
| Múltiplas chamadas a `ITokenIssuer.Issue` | Operam isoladas em memória, gerando `jti` exclusivo | CA-11.3, CA-11.4 |
| Indisponibilidade transitória de `ISigningKey` | EP-10 responde 503 `ERR-SYS-002`; `Issue` propaga exceção | CA-11.2, CA-11.8 |

**Pós-condições:** Chave pública RSA ativa disponibilizada em EP-10. Token JWT RS256 emitido com `jti` exclusivo, claims normalizadas e assinatura digital válida.

## 4. Regras de negócio

| ID | Tipo | Regra (SE / ENTÃO / SENÃO) | Parâmetros | Erro | RF | Evidência |
|---|---|---|---|---|---|---|
| RN-90 | Integridade | **SE** EP-10 for invocado **ENTÃO** `keys` **DEVE** ter 1 JWK RSA (`kty="RSA"`, `use="sig"`, `alg="RS256"`, `kid=JWT_KEY_ID`, `n`, `e` base64url sem `=`) **SENÃO** chave não é publicada | `alg="RS256"` | – | RF-11 | ADR-06, RFC 7517 |
| RN-91 | Temporal | **SE** EP-10 responder 200 **ENTÃO** `Cache-Control` **DEVE** ser `public, max-age=300` **SENÃO** cabeçalho omitido | `max-age=300` | – | RF-11 | PRS-44, D-10 |
| RN-92 | Integridade | **SE** `ISigningKey` falhar em EP-10 **ENTÃO** **DEVE** responder 503 `ERR-SYS-002` **SENÃO** responde 200 | – | `ERR-SYS-002` | RF-11 | C6, PRS-16 |
| RN-93 | Temporal | **SE** `ITokenIssuer.Issue` for invocado **ENTÃO** `iat` **DEVE** ser de `TimeProvider` e `exp = iat + 900` **SENÃO** token não é emitido | 900 s | – | RF-11 | 10 §3.5, PRS-20 |
| RN-94 | Integridade | **SE** `ITokenIssuer.Issue` for invocado **ENTÃO** `jti` **DEVE** ser novo UUID v7 minúsculo formato D **SENÃO** token não é emitido | UUID v7 | – | RF-11 | 10 §3.3, 40 §4.3 |
| RN-95 | Validação | **SE** `ViewMode` for violado (`NONE` com contexto, `SINGLE` sem `TenantId` ou `AllowedTenants` diferente de `[TenantId]`, `MACRO_GROUP` com `TenantId` ou contagem fora de 1..50) **ENTÃO** lança `ArgumentException` **SENÃO** aprova | 1 a 50 tenants | `ERR-SYS-001` | RF-11 | ADR-02, 10 §3.5 |
| RN-96 | Validação | **SE** personificação for inconsistente (`Act != null` com `FamilyId != null`, ou `Act == null` com `FamilyId == null`) **ENTÃO** lança `ArgumentException` **SENÃO** aprova | Invariante act | `ERR-SYS-001` | RF-11 | ADR-08, 10 §3.5 |
| RN-97 | Cálculo | **SE** emitir token **ENTÃO** `AllowedTenants`, `Roles` e `Permissions` **DEVEM** ser ordenados ascendentemente **SENÃO** não ordena | Ordem ordinal | – | RF-11 | 10 §3.3, PRS-24 |
| RN-98 | Integridade | **SE** token for emitido **ENTÃO** cabeçalho tem `alg="RS256"`, `typ="JWT"` e `kid=KeyId`, assinado com RSA 2048 bits **SENÃO** falha | RSA 2048 | – | RF-11 | ADR-06, 10 §3.5 |
| RN-99 | Integridade | **SE** `ISigningKey` falhar em `Issue` **ENTÃO** propaga exceção para conversão em 503 `ERR-SYS-002` **SENÃO** devolve token | – | `ERR-SYS-002` | RF-11 | C5, C6, PRS-16 |

## 5. Critérios de aceitação

```gherkin
# language: pt
Funcionalidade: RF-11 — Publicar JWKS e emitir tokens de acesso

  Contexto:
    Dado que o serviço possui ISigningKey com KeyId "key-2026-01" e par de chaves RSA de 2048 bits
    E que o TimeProvider injetado indica o instante Unix 1776600000

  @CA-11.1 @RF-11 @RN-90 @RN-91 @MUST @borda
  Cenário: Leitura pública do JWKS com sucesso ignorando query e corpo
    Quando é enviado GET "/.well-known/jwks.json" com query "?origem=gw" e corpo "ignorado"
    Então o status da resposta é 200
    E o cabeçalho "Content-Type" contém "application/json; charset=utf-8"
    E o cabeçalho "Cache-Control" é "public, max-age=300"
    E a propriedade "keys" tem 1 elemento com "kty" "RSA", "use" "sig", "alg" "RS256", "kid" "key-2026-01", "n" sem "=" e "e" "AQAB"

  @CA-11.2 @RF-11 @RN-92 @MUST @critico @borda
  Esquema do Cenário: Falha de dependência da chave pública ou rota incorreta em EP-10
    Quando é enviado pedido HTTP para <path> com método <metodo> e estado da chave <estado_chave>
    Então o status da resposta é <status>
    E o campo "code" é "<codigo>"
    Exemplos:
      | path                      | metodo | estado_chave  | status | codigo       |
      | "/.well-known/jwks.json"  | "GET"  | "INDISPONIVEL"| 503    | ERR-SYS-002  |
      | "/.well-known/jwks.json"  | "POST" | "DISPONIVEL"  | 405    | ERR-REQ-002  |
      | "/.well-known/jwks-outro" | "GET"  | "DISPONIVEL"  | 404    | ERR-REQ-001  |

  @CA-11.3 @RF-11 @RN-93 @RN-94 @RN-97 @RN-98 @MUST @borda
  Cenário: Emissão de token com ViewMode NONE via IF-06
    Dado uma emissão com Sub "0192e2c0-0001-7000-8000-000000000001", FamilyId "0192e2c0-0002-7000-8000-000000000002", PlatformRole "NONE", ViewMode "NONE" e Act null
    Quando ITokenIssuer.Issue é executado
    Então o IssuedToken tem Jti UUID v7, Iat 1776600000 e Exp exatamente 1776600900
    E o JWT decodificado contém alg "RS256", typ "JWT", kid "key-2026-01" e fid "0192e2c0-0002-7000-8000-000000000002"
    E o JWT decodificado contém group_id null, tenant_id null, allowed_tenants [], active_scope null, roles [], permissions [] e sem claim "act"

  @CA-11.4 @RF-11 @RN-95 @RN-97 @RN-98 @MUST @borda
  Cenário: Emissão de token com ViewMode SINGLE e ordenação de coleções via IF-06
    Dado uma emissão com Sub "0192e2c0-0001-7000-8000-000000000001", FamilyId "0192e2c0-0002-7000-8000-000000000002", ViewMode "SINGLE", GroupId "0192e2c0-0010-7000-8000-000000000010", TenantId "0192e2c0-0020-7000-8000-000000000020", AllowedTenants ["0192e2c0-0020-7000-8000-000000000020"], ActiveScope "BUYER", Roles ["role_z", "role_a"], Permissions ["perm:b", "perm:a"] e Act null
    Quando ITokenIssuer.Issue é executado
    Então o JWT decodificado contém view_mode "SINGLE", group_id "0192e2c0-0010-7000-8000-000000000010", tenant_id "0192e2c0-0020-7000-8000-000000000020" e allowed_tenants ["0192e2c0-0020-7000-8000-000000000020"]
    E o JWT decodificado contém roles ordenadas ["role_a", "role_z"] e permissions ordenadas ["perm:a", "perm:b"]
    E o registo Claims de IssuedToken contém Roles e Permissions ordenados de forma idêntica

  @CA-11.5 @RF-11 @RN-95 @RN-97 @MUST @borda
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

  @CA-11.6 @RF-11 @RN-96 @RN-98 @MUST @critico @borda
  Cenário: Emissão de token em impersonation com omissão de fid e presença de claim act via IF-06
    Dado uma emissão em personificação com Sub "0192e2c0-0001-7000-8000-000000000001", FamilyId null, ViewMode "SINGLE", GroupId "0192e2c0-0010-7000-8000-000000000010", TenantId "0192e2c0-0020-7000-8000-000000000020", AllowedTenants ["0192e2c0-0020-7000-8000-000000000020"], ActiveScope "CARRIER" e Act com sub "0192e2c0-0099-7000-8000-000000000099" e ticket_id "CHAMADO-1"
    Quando ITokenIssuer.Issue é executado
    Então o JWT decodificado não contém a claim "fid"
    E o JWT decodificado contém claim "act" com sub "0192e2c0-0099-7000-8000-000000000099" e ticket_id "CHAMADO-1"
    E a assinatura do JWT é validada com sucesso através da chave pública de ISigningKey

  @CA-11.7 @RF-11 @RN-95 @RN-96 @MUST @borda
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

  @CA-11.8 @RF-11 @RN-99 @MUST @critico @borda
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
| RF-11 | MUST | RN-90 a RN-99 | EP-10 | EX-11.1 a EX-11.4 | CA-11.1 a CA-11.8 | F-00 |

**Totais:** RF = 1 · RN = 10 · EP = 1 · EV = 0 · CA = 8 (`@critico` = 3) · Σw = 24

## 7. Pressupostos do módulo

| ID | Pressuposto | Justificação | Aprovado por (data) |
|---|---|---|---|
| PRS-170 | O endpoint `GET /.well-known/jwks.json` ignora cabeçalhos de autenticação sem rejeição | Consumo irrestrito de chaves por clientes externos e proxies sem validação de token (RFC 7517) | PO, 2026-11-02 (D-23) |
| PRS-171 | O método `ITokenIssuer.Issue` opera em memória de processo sem chamadas síncronas a base de dados ou Redis | Desempenho determinístico e ausência de bloqueios externos na emissão de tokens | PO, 2026-11-02 (D-23) |
| PRS-172 | A chave RSA de `ISigningKey` possui tamanho fixo de 2048 bits com expoente público `AQAB` (65537) | Interoperabilidade RS256 e conformidade com ADR-06 e RFC 7518 §3.1 | PO, 2026-11-02 (D-23) |

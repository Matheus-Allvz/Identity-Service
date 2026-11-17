# SDKs de validação de token (drop-in)

Arquivos únicos para copiar para o seu microsserviço e validar **offline** (RS256 + JWKS) os access tokens
emitidos pelo Identity-Service. Não há pacote publicado: copie o arquivo e instale as dependências.

| Arquivo | Stack | Dependências |
|---|---|---|
| `b2b_auth_fastapi.py` | Python / FastAPI | `pip install fastapi "pyjwt[crypto]"` |
| `B2bAuthMiddleware.cs` | ASP.NET Core (.NET 8+) | `dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer` |
| `auth-middleware.ts` | Node / Express | `npm install express jose` |
| `jwt_validator.go` | Go `net/http` (Go 1.21+) | `go get github.com/golang-jwt/jwt/v5` |

Configuração: variável de ambiente `IDENTITY_BASE_URL` (padrão `https://identity.matheus-alves.dev`).
As chaves vêm de `{IDENTITY_BASE_URL}/.well-known/jwks.json`, ficam em cache por 1h e são baixadas de novo
quando aparece um `kid` desconhecido (rotação de chaves).

## O que é validado

Assinatura RS256 (nenhum outro algoritmo é aceito), `typ = JWT`, `iss = identity-service`,
`aud = b2b-platform`, `exp` (tokens duram 900s; tolerância de relógio de 60s).
Token com `view_mode = NONE` (pré-contexto, emitido no login antes do `switch-context`) é recusado com **403**
por padrão; há uma flag de opt-out (`allow_pre_context` / `allowPreContext`) para as raras rotas que o aceitam.

## Uso

**Python (FastAPI)**

```python
from b2b_auth_fastapi import AuthContext, install, require_permission, require_scope
app = FastAPI(); install(app)  # handler RFC 9457
@app.post("/ofertas", dependencies=[Depends(require_scope("SUPPLIER"))])
def criar(ctx: AuthContext = Depends(require_permission("fornecimento:publicar"))):
    return {"tenant": ctx.tenant_id, "user": ctx.user_id}
```

**C# (ASP.NET Core)**

```csharp
builder.Services.AddB2bAuth();            // ou AddB2bAuth("https://...", allowPreContext: true)
app.UseAuthentication(); app.UseAuthorization();
app.MapPost("/ofertas", (ClaimsPrincipal user) => user.ToB2bContext().TenantId)
   .RequireScope("SUPPLIER").RequirePermission("fornecimento:publicar");
// Controllers: [Authorize] aplica a política padrão; cheque User.ToB2bContext().Permissions na action.
```

**TypeScript (Express)**

```ts
import { authenticate, requirePermission, requireScope } from "./auth-middleware";
app.use(authenticate());                  // ou authenticate({ allowPreContext: true })
app.post("/ofertas", requireScope("SUPPLIER"), requirePermission("fornecimento:publicar"), (req, res) => {
  res.json({ tenant: req.auth!.tenantId, user: req.auth!.userId });
});
```

**Go (net/http)**

```go
auth := b2bauth.Authenticate(false)       // true = aceita token pré-contexto
h := http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
	c := b2bauth.FromContext(r.Context()) // c.UserID(), c.TenantID, c.Permissions...
})
mux.Handle("/ofertas", auth(b2bauth.RequireScope("SUPPLIER")(b2bauth.RequirePermission("fornecimento:publicar")(h))))
```

`require_permission` exige **todas** as permissões informadas; `require_scope` aceita **qualquer um** dos escopos.

## Claims

| Claim | Tipo | Significado |
|---|---|---|
| `jti` | string | Identificador único do token |
| `sub` | UUID | Usuário autenticado |
| `iat` / `exp` | epoch (s) | Emissão / expiração (`exp = iat + 900`) |
| `iss` / `aud` | string | `identity-service` / `b2b-platform` |
| `platform_role` | string | `NONE` \| `SUPPORT_AGENT` \| `PLATFORM_ADMIN` |
| `view_mode` | string | `NONE` (pré-contexto) \| `SINGLE` (1 tenant) \| `MACRO_GROUP` (visão do grupo) |
| `group_id` | UUID \| null | Grupo econômico |
| `tenant_id` | UUID \| null | Tenant ativo; `null` em `NONE` e `MACRO_GROUP` |
| `allowed_tenants` | UUID[] | Tenants que o token alcança (`[tenant_id]` em `SINGLE`; 1 a 50 em `MACRO_GROUP`) |
| `active_scope` | string \| null | `BUYER` \| `SUPPLIER` \| `CARRIER` \| `TENANT_ADMIN` |
| `roles` | string[] | Papéis de negócio no escopo ativo |
| `permissions` | string[] | Permissões no formato `<recurso>:<acao>` |
| `fid` | UUID | Família da sessão (ausente em impersonation) |
| `act` | `{sub, ticket_id}` | Só em impersonation (RFC 8693): `act.sub` é o agente de suporte |
| `token_use` | string | `service` em tokens de serviço (Client Credentials); ausente em tokens de utilizador |
| `client_id` | string | Só em tokens de serviço: identificador da conta de serviço (o `sub` é o id dela) |

Tokens de serviço não têm `fid` nem `act`. Os helpers validam-nos como qualquer outro token; para tratar serviços de forma diferente, leia a claim `token_use` no payload.

Em visão `MACRO_GROUP`, filtre os dados por `allowed_tenants`, não por `tenant_id` (que é `null`).

## Respostas de erro

RFC 9457, `Content-Type: application/problem+json`, mesmo envelope do Identity-Service:

```json
{
  "type": "https://identity.b2b.local/errors/ERR-AUTH-002",
  "title": "Token de acesso ausente, inválido, expirado ou revogado",
  "status": 401,
  "code": "ERR-AUTH-002",
  "detail": "Token ausente, inválido ou expirado",
  "correlation_id": "0194eb82-7000-7000-8000-000000000001"
}
```

- **401 `ERR-AUTH-002`**: token ausente, inválido ou expirado.
- **403 `ERR-CTX-007`**: permissão, escopo ou contexto ativo insuficiente (inclui token pré-contexto).
- `X-Correlation-Id` recebido é propagado (cabeçalho e corpo) quando é um UUID válido; caso contrário é gerado um novo.

## Limites

- **A validação offline não enxerga a denylist do Redis.** Logout, suspensão de usuário ou de tenant e troca de
  contexto só passam a valer nos seus serviços quando o token expira — no pior caso, 15 minutos.
- `POST /api/v1/auth/introspect` existe **apenas para diagnóstico**; não o chame a cada requisição.
- Se o JWKS estiver inacessível e não houver chave em cache, a resposta é 401 (não 503).
- Os helpers só autenticam e checam permissão/escopo. O isolamento dos dados por tenant continua sendo
  responsabilidade do seu serviço.
- Verificado neste repositório: Python (autoteste `python b2b_auth_fastapi.py`) e C# (compilado e exercitado
  em .NET 10). Os arquivos TypeScript e Go ainda não foram compilados — rode `tsc --noEmit` e
  `gofmt -l . && go vet ./...` ao copiá-los.

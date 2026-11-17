// Package b2bauth valida offline (RS256 + JWKS) os access tokens do Identity-Service
// em servidores net/http. Copie este arquivo para o seu serviço.
// Única dependência externa: github.com/golang-jwt/jwt/v5 (o cache de JWKS usa só a stdlib).
//
//	mux.Handle("/ofertas", b2bauth.Authenticate(false)(b2bauth.RequirePermission("fornecimento:publicar")(h)))
package b2bauth

import (
	"context"
	"crypto/rand"
	"crypto/rsa"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"math/big"
	"net/http"
	"os"
	"regexp"
	"slices"
	"strings"
	"sync"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

const (
	issuer        = "identity-service"
	audience      = "b2b-platform"
	errorTypeBase = "https://identity.b2b.local/errors/" // mesmo prefixo de `type` usado pelo Identity-Service
	jwksTTL       = time.Hour                            // igual ao max-age do JWKS
	jwksCooldown  = 30 * time.Second                     // intervalo mínimo entre downloads
)

var uuidRE = regexp.MustCompile(`(?i)^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$`)

// Actor é o claim `act` (RFC 8693): presente apenas em tokens de impersonation.
type Actor struct {
	Sub      string `json:"sub"`
	TicketID string `json:"ticket_id"`
}

// Claims é o contexto tipado do token. UserID = Subject (sub); jti = ID.
type Claims struct {
	PlatformRole   string   `json:"platform_role"` // NONE | SUPPORT_AGENT | PLATFORM_ADMIN
	ViewMode       string   `json:"view_mode"`     // NONE | SINGLE | MACRO_GROUP
	GroupID        *string  `json:"group_id"`
	TenantID       *string  `json:"tenant_id"` // nil em NONE e MACRO_GROUP
	AllowedTenants []string `json:"allowed_tenants"`
	ActiveScope    *string  `json:"active_scope"` // BUYER | SUPPLIER | CARRIER | TENANT_ADMIN | nil
	Roles          []string `json:"roles"`
	Permissions    []string `json:"permissions"`
	FamilyID       string   `json:"fid,omitempty"` // vazio em tokens de impersonation
	Act            *Actor   `json:"act,omitempty"`
	jwt.RegisteredClaims
}

// UserID devolve o `sub` do token.
func (c *Claims) UserID() string { return c.Subject }

// ---- JWKS: busca + cache em memória (1h) com refetch para `kid` desconhecido ----

type jwksCache struct {
	mu      sync.Mutex
	url     string
	keys    map[string]*rsa.PublicKey
	fetched time.Time // última TENTATIVA de download
}

var cache = &jwksCache{url: strings.TrimRight(envOr("IDENTITY_BASE_URL", "https://identity.matheus-alves.dev"), "/") + "/.well-known/jwks.json"}

func envOr(name, def string) string {
	if v := os.Getenv(name); v != "" {
		return v
	}
	return def
}

func (c *jwksCache) key(kid string) (*rsa.PublicKey, error) {
	// ponytail: lock global segura as requisições durante o download (timeout 5s, 1x/h); trocar por singleflight se pesar.
	c.mu.Lock()
	defer c.mu.Unlock()
	age := time.Since(c.fetched)
	if _, ok := c.keys[kid]; age > jwksTTL || (!ok && age > jwksCooldown) {
		c.fetched = time.Now()
		if keys, err := fetchJWKS(c.url); err == nil {
			c.keys = keys
		} // se falhar, segue com as chaves antigas (o servidor permite stale por 24h)
	}
	if k, ok := c.keys[kid]; ok {
		return k, nil
	}
	return nil, fmt.Errorf("kid desconhecido: %q", kid)
}

func fetchJWKS(url string) (map[string]*rsa.PublicKey, error) {
	resp, err := (&http.Client{Timeout: 5 * time.Second}).Get(url)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	var set struct {
		Keys []struct{ Kty, Kid, N, E string }
	}
	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("jwks: HTTP %d", resp.StatusCode)
	}
	if err := json.NewDecoder(resp.Body).Decode(&set); err != nil {
		return nil, err
	}
	keys := make(map[string]*rsa.PublicKey)
	for _, k := range set.Keys {
		n, errN := base64.RawURLEncoding.DecodeString(k.N)
		e, errE := base64.RawURLEncoding.DecodeString(k.E)
		if k.Kty != "RSA" || errN != nil || errE != nil {
			continue
		}
		keys[k.Kid] = &rsa.PublicKey{N: new(big.Int).SetBytes(n), E: int(new(big.Int).SetBytes(e).Int64())}
	}
	return keys, nil
}

// ---- Verificação ----

var parser = jwt.NewParser(jwt.WithValidMethods([]string{"RS256"}), jwt.WithIssuer(issuer), jwt.WithAudience(audience),
	jwt.WithLeeway(60*time.Second), jwt.WithExpirationRequired(), jwt.WithIssuedAt())

// VerifyToken valida assinatura, iss, aud e exp e devolve os claims tipados.
func VerifyToken(raw string) (*Claims, error) {
	claims := &Claims{}
	_, err := parser.ParseWithClaims(raw, claims, func(t *jwt.Token) (any, error) {
		if t.Header["typ"] != "JWT" {
			return nil, errors.New("typ inválido")
		}
		kid, _ := t.Header["kid"].(string)
		return cache.key(kid)
	})
	if err != nil {
		return nil, err
	}
	if claims.Subject == "" || claims.ID == "" || claims.ViewMode == "" {
		return nil, errors.New("claims obrigatórios ausentes")
	}
	return claims, nil
}

type ctxKey struct{}

// FromContext devolve os claims colocados por Authenticate (nil se a rota não passou por ele).
func FromContext(ctx context.Context) *Claims {
	c, _ := ctx.Value(ctxKey{}).(*Claims)
	return c
}

// Authenticate lê `Authorization: Bearer <token>` e põe os claims no contexto da requisição.
// Tokens pré-contexto (view_mode = NONE) são recusados com 403, a menos que allowPreContext seja true.
func Authenticate(allowPreContext bool) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			scheme, token, _ := strings.Cut(r.Header.Get("Authorization"), " ")
			claims, err := VerifyToken(strings.TrimSpace(token))
			switch {
			case !strings.EqualFold(scheme, "Bearer") || err != nil:
				// ponytail: JWKS fora do ar também cai aqui como 401; separar em 503 se isso incomodar.
				problem(w, r, http.StatusUnauthorized, "Token ausente, inválido ou expirado")
			case claims.ViewMode == "NONE" && !allowPreContext:
				problem(w, r, http.StatusForbidden, "Token sem contexto ativo: chame /api/v1/auth/switch-context")
			default:
				next.ServeHTTP(w, r.WithContext(context.WithValue(r.Context(), ctxKey{}, claims)))
			}
		})
	}
}

func guard(check func(*Claims) string) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			c := FromContext(r.Context())
			if c == nil {
				problem(w, r, http.StatusUnauthorized, "Requisição não autenticada: use Authenticate antes")
			} else if denied := check(c); denied != "" {
				problem(w, r, http.StatusForbidden, denied)
			} else {
				next.ServeHTTP(w, r)
			}
		})
	}
}

// RequirePermission exige TODAS as permissões informadas (formato `<recurso>:<acao>`).
func RequirePermission(perms ...string) func(http.Handler) http.Handler {
	return guard(func(c *Claims) string {
		for _, p := range perms {
			if !slices.Contains(c.Permissions, p) {
				return "Permissão ausente: " + p
			}
		}
		return ""
	})
}

// RequireScope exige que o escopo ativo seja um dos informados.
func RequireScope(scopes ...string) func(http.Handler) http.Handler {
	return guard(func(c *Claims) string {
		if c.ActiveScope == nil || !slices.Contains(scopes, *c.ActiveScope) {
			return "Escopo ativo não autorizado para este recurso"
		}
		return ""
	})
}

// problem responde application/problem+json (RFC 9457): 401 -> ERR-AUTH-002, 403 -> ERR-CTX-007.
func problem(w http.ResponseWriter, r *http.Request, status int, detail string) {
	code, title := "ERR-AUTH-002", "Token de acesso ausente, inválido, expirado ou revogado"
	if status == http.StatusForbidden {
		code, title = "ERR-CTX-007", "Violação de isolamento de contexto ou permissão insuficiente"
	} else {
		w.Header().Set("WWW-Authenticate", `Bearer error="invalid_token"`)
	}
	cid := r.Header.Get("X-Correlation-Id") // propaga se for UUID válido; senão gera um v4
	if !uuidRE.MatchString(cid) {
		b := make([]byte, 16)
		_, _ = rand.Read(b)
		b[6], b[8] = b[6]&0x0f|0x40, b[8]&0x3f|0x80
		cid = fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:])
	}
	w.Header().Set("Content-Type", "application/problem+json")
	w.Header().Set("X-Correlation-Id", cid)
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]any{"type": errorTypeBase + code, "title": title, "status": status,
		"code": code, "detail": detail, "correlation_id": cid})
}

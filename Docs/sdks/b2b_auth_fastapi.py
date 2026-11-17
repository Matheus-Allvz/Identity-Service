"""
Validação offline (RS256 + JWKS) dos access tokens do Identity-Service para FastAPI.
Copie este arquivo para o seu serviço. Dependências: fastapi, PyJWT, cryptography.

    from b2b_auth_fastapi import install, current_user, require_permission, require_scope, AuthContext
    app = FastAPI(); install(app)                      # registra o handler RFC 9457
    @app.get("/ofertas")
    def listar(ctx: AuthContext = Depends(require_permission("fornecimento:publicar"))): ...
"""
import os
import uuid
from dataclasses import dataclass
from typing import Optional

import jwt
from fastapi import Depends, FastAPI, Request
from fastapi.responses import JSONResponse

BASE_URL = os.getenv("IDENTITY_BASE_URL", "https://identity.matheus-alves.dev").rstrip("/")
ISSUER, AUDIENCE, LEEWAY_S = "identity-service", "b2b-platform", 60
ERROR_TYPE_BASE = "https://identity.b2b.local/errors/"  # mesmo prefixo de `type` usado pelo Identity-Service
TITLES = {
    "ERR-AUTH-002": "Token de acesso ausente, inválido, expirado ou revogado",
    "ERR-CTX-007": "Violação de isolamento de contexto ou permissão insuficiente",
}

# Cache em memória de 1h (igual ao max-age do JWKS). Para um `kid` desconhecido o
# PyJWKClient refaz o download uma vez antes de falhar — cobre a rotação de chaves.
_jwks = jwt.PyJWKClient(f"{BASE_URL}/.well-known/jwks.json", cache_keys=True, cache_jwk_set=True, lifespan=3600)


class B2bAuthError(Exception):
    """401 -> ERR-AUTH-002 (token ausente/inválido/expirado); 403 -> ERR-CTX-007 (permissão/escopo/contexto)."""

    def __init__(self, status: int, detail: str):
        self.status, self.detail = status, detail
        self.code = "ERR-AUTH-002" if status == 401 else "ERR-CTX-007"


@dataclass(frozen=True)
class AuthContext:
    user_id: str                 # sub
    jti: str
    platform_role: str           # NONE | SUPPORT_AGENT | PLATFORM_ADMIN
    view_mode: str               # NONE | SINGLE | MACRO_GROUP
    group_id: Optional[str]
    tenant_id: Optional[str]     # None em NONE e MACRO_GROUP
    allowed_tenants: list[str]
    active_scope: Optional[str]  # BUYER | SUPPLIER | CARRIER | TENANT_ADMIN | None
    roles: list[str]
    permissions: list[str]
    family_id: Optional[str]     # fid — ausente em tokens de impersonation
    actor: Optional[dict]        # act = {"sub", "ticket_id"} — só em impersonation (RFC 8693)


def verify_token(token: str, allow_pre_context: bool = False) -> AuthContext:
    """Valida assinatura, iss, aud, exp e devolve o contexto tipado. Lança B2bAuthError."""
    try:
        if jwt.get_unverified_header(token).get("typ") != "JWT":
            raise jwt.InvalidTokenError("typ")
        key = _jwks.get_signing_key_from_jwt(token).key
        c = jwt.decode(token, key, algorithms=["RS256"], issuer=ISSUER, audience=AUDIENCE, leeway=LEEWAY_S,
                       options={"require": ["exp", "iat", "iss", "aud", "sub", "jti", "view_mode"]})
    except jwt.PyJWTError as e:
        # ponytail: JWKS fora do ar também cai aqui como 401; separar em 503 se isso incomodar.
        raise B2bAuthError(401, "Token ausente, inválido ou expirado") from e
    # Token pré-contexto (logo após o login, antes do switch-context): não serve para negócio.
    if c["view_mode"] == "NONE" and not allow_pre_context:
        raise B2bAuthError(403, "Token sem contexto ativo: chame /api/v1/auth/switch-context")
    return AuthContext(
        user_id=c["sub"], jti=c["jti"], platform_role=c.get("platform_role", "NONE"), view_mode=c["view_mode"],
        group_id=c.get("group_id"), tenant_id=c.get("tenant_id"), allowed_tenants=c.get("allowed_tenants") or [],
        active_scope=c.get("active_scope"), roles=c.get("roles") or [], permissions=c.get("permissions") or [],
        family_id=c.get("fid"), actor=c.get("act"))


def current_user(allow_pre_context: bool = False):
    """Dependency: lê `Authorization: Bearer <token>` e devolve o AuthContext."""
    def dep(request: Request) -> AuthContext:
        scheme, _, token = request.headers.get("authorization", "").partition(" ")
        if scheme.lower() != "bearer" or not token.strip():
            raise B2bAuthError(401, "Cabeçalho Authorization: Bearer ausente")
        return verify_token(token.strip(), allow_pre_context)
    return dep


def require_permission(*permissions: str):
    """Exige TODAS as permissões informadas (formato `<recurso>:<acao>`)."""
    def dep(ctx: AuthContext = Depends(current_user())) -> AuthContext:
        missing = [p for p in permissions if p not in ctx.permissions]
        if missing:
            raise B2bAuthError(403, f"Permissão ausente: {', '.join(missing)}")
        return ctx
    return dep


def require_scope(*scopes: str):
    """Exige que o escopo ativo seja um dos informados (ex.: require_scope("SUPPLIER", "CARRIER"))."""
    def dep(ctx: AuthContext = Depends(current_user())) -> AuthContext:
        if ctx.active_scope not in scopes:
            raise B2bAuthError(403, f"Escopo ativo '{ctx.active_scope}' não autorizado")
        return ctx
    return dep


def _correlation_id(request: Request) -> str:
    """Propaga X-Correlation-Id se for um UUID válido; senão gera um novo."""
    try:
        return str(uuid.UUID(request.headers.get("x-correlation-id", "")))
    except ValueError:
        return str(uuid.uuid4())


def install(app: FastAPI) -> None:
    """Registra o handler que converte B2bAuthError em application/problem+json (RFC 9457)."""
    @app.exception_handler(B2bAuthError)
    async def _handler(request: Request, exc: B2bAuthError) -> JSONResponse:
        cid = _correlation_id(request)
        headers = {"X-Correlation-Id": cid}
        if exc.status == 401:
            headers["WWW-Authenticate"] = 'Bearer error="invalid_token"'
        return JSONResponse(status_code=exc.status, media_type="application/problem+json", headers=headers, content={
            "type": ERROR_TYPE_BASE + exc.code, "title": TITLES[exc.code], "status": exc.status,
            "code": exc.code, "detail": exc.detail, "correlation_id": cid})


if __name__ == "__main__":  # autoteste: `python b2b_auth_fastapi.py` (sem rede; chave RSA gerada na hora)
    import time
    from types import SimpleNamespace
    from cryptography.hazmat.primitives.asymmetric import rsa

    priv = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    _jwks.get_signing_key_from_jwt = lambda _t: SimpleNamespace(key=priv.public_key())  # type: ignore[method-assign]

    def mint(**over):
        now = int(time.time())
        claims = {"jti": "j1", "sub": "u1", "iat": now, "exp": now + 900, "iss": ISSUER, "aud": AUDIENCE,
                  "platform_role": "NONE", "view_mode": "SINGLE", "group_id": "g1", "tenant_id": "t1",
                  "allowed_tenants": ["t1"], "active_scope": "BUYER", "roles": [], "permissions": ["a:b:c"],
                  "fid": "f1"} | over
        return jwt.encode(claims, priv, algorithm="RS256", headers={"kid": "key-2026-01"})

    def status_of(token, **kw):
        try:
            verify_token(token, **kw)
            return 200
        except B2bAuthError as e:
            return e.status

    assert verify_token(mint()).permissions == ["a:b:c"]
    assert status_of(mint(exp=int(time.time()) - 30)) == 200      # dentro da tolerância de 60s
    assert status_of(mint(exp=int(time.time()) - 120)) == 401     # expirado
    assert status_of(mint(aud="outro")) == 401 and status_of(mint(iss="outro")) == 401
    assert status_of(jwt.encode({"sub": "u1"}, "x" * 32, algorithm="HS256")) == 401  # HS256 nunca é aceito
    assert status_of(mint(view_mode="NONE")) == 403 and status_of(mint(view_mode="NONE"), allow_pre_context=True) == 200
    print("ok")

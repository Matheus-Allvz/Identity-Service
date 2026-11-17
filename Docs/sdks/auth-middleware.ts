/**
 * Validação offline (RS256 + JWKS) dos access tokens do Identity-Service para Node/Express.
 * Copie este arquivo para o seu serviço. Dependências: express, jose (ESM; Node >= 18).
 *
 *   app.use(authenticate());                                   // popula req.auth
 *   app.post("/ofertas", requireScope("SUPPLIER"), requirePermission("fornecimento:publicar"), handler);
 */
import { randomUUID } from "node:crypto";
import type { NextFunction, Request, RequestHandler, Response } from "express";
import { createRemoteJWKSet, jwtVerify } from "jose";

const BASE_URL = (process.env.IDENTITY_BASE_URL ?? "https://identity.matheus-alves.dev").replace(/\/+$/, "");
const ISSUER = "identity-service";
const AUDIENCE = "b2b-platform";
const ERROR_TYPE_BASE = "https://identity.b2b.local/errors/"; // mesmo prefixo de `type` usado pelo Identity-Service
const TITLES = {
  "ERR-AUTH-002": "Token de acesso ausente, inválido, expirado ou revogado",
  "ERR-CTX-007": "Violação de isolamento de contexto ou permissão insuficiente",
} as const;
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Cache em memória de 1h (igual ao max-age do JWKS). Para um `kid` desconhecido o jose
// refaz o download (no máximo 1x a cada 30s) — cobre a rotação de chaves.
const jwks = createRemoteJWKSet(new URL(`${BASE_URL}/.well-known/jwks.json`), {
  cacheMaxAge: 3_600_000,
  cooldownDuration: 30_000,
});

export type Scope = "BUYER" | "SUPPLIER" | "CARRIER" | "TENANT_ADMIN";

export interface AuthContext {
  userId: string; // sub
  jti: string;
  platformRole: "NONE" | "SUPPORT_AGENT" | "PLATFORM_ADMIN";
  viewMode: "NONE" | "SINGLE" | "MACRO_GROUP";
  groupId: string | null;
  tenantId: string | null; // null em NONE e MACRO_GROUP
  allowedTenants: string[];
  activeScope: Scope | null;
  roles: string[];
  permissions: string[];
  familyId: string | null; // fid — ausente em tokens de impersonation
  actor: { sub: string; ticket_id: string | null } | null; // act — só em impersonation (RFC 8693)
}

declare global {
  // eslint-disable-next-line @typescript-eslint/no-namespace
  namespace Express {
    interface Request {
      auth?: AuthContext;
    }
  }
}

/** 401 -> ERR-AUTH-002 (token ausente/inválido/expirado); 403 -> ERR-CTX-007 (permissão/escopo/contexto). */
export class B2bAuthError extends Error {
  readonly code: keyof typeof TITLES;
  constructor(readonly status: 401 | 403, detail: string) {
    super(detail);
    this.code = status === 401 ? "ERR-AUTH-002" : "ERR-CTX-007";
  }
}

/** Valida assinatura, iss, aud, exp e devolve o contexto tipado. Lança B2bAuthError. */
export async function verifyToken(token: string, opts: { allowPreContext?: boolean } = {}): Promise<AuthContext> {
  let c: Record<string, any>;
  try {
    ({ payload: c } = await jwtVerify(token, jwks, {
      algorithms: ["RS256"],
      issuer: ISSUER,
      audience: AUDIENCE,
      typ: "JWT",
      clockTolerance: 60,
      requiredClaims: ["exp", "iat", "sub", "jti", "view_mode"],
    }));
  } catch {
    // ponytail: JWKS fora do ar também cai aqui como 401; separar em 503 se isso incomodar.
    throw new B2bAuthError(401, "Token ausente, inválido ou expirado");
  }
  // Token pré-contexto (logo após o login, antes do switch-context): não serve para negócio.
  if (c.view_mode === "NONE" && !opts.allowPreContext) {
    throw new B2bAuthError(403, "Token sem contexto ativo: chame /api/v1/auth/switch-context");
  }
  return {
    userId: c.sub,
    jti: c.jti,
    platformRole: c.platform_role ?? "NONE",
    viewMode: c.view_mode,
    groupId: c.group_id ?? null,
    tenantId: c.tenant_id ?? null,
    allowedTenants: c.allowed_tenants ?? [],
    activeScope: c.active_scope ?? null,
    roles: c.roles ?? [],
    permissions: c.permissions ?? [],
    familyId: c.fid ?? null,
    actor: c.act ?? null,
  };
}

/** Responde application/problem+json (RFC 9457), propagando X-Correlation-Id se for um UUID válido. */
function problem(req: Request, res: Response, err: B2bAuthError): void {
  const received = req.header("x-correlation-id") ?? "";
  const correlationId = UUID_RE.test(received) ? received : randomUUID();
  res.setHeader("X-Correlation-Id", correlationId);
  if (err.status === 401) res.setHeader("WWW-Authenticate", 'Bearer error="invalid_token"');
  res.status(err.status).type("application/problem+json").send(
    JSON.stringify({
      type: ERROR_TYPE_BASE + err.code,
      title: TITLES[err.code],
      status: err.status,
      code: err.code,
      detail: err.message,
      correlation_id: correlationId,
    }),
  );
}

/** Middleware: lê `Authorization: Bearer <token>` e popula `req.auth`. */
export function authenticate(opts: { allowPreContext?: boolean } = {}): RequestHandler {
  return async (req: Request, res: Response, next: NextFunction) => {
    try {
      const [scheme, token] = (req.header("authorization") ?? "").split(" ");
      if (scheme?.toLowerCase() !== "bearer" || !token) throw new B2bAuthError(401, "Cabeçalho Authorization: Bearer ausente");
      req.auth = await verifyToken(token, opts);
      next();
    } catch (err) {
      if (err instanceof B2bAuthError) problem(req, res, err);
      else next(err);
    }
  };
}

function guard(check: (ctx: AuthContext) => string | null): RequestHandler {
  return (req, res, next) => {
    if (!req.auth) return problem(req, res, new B2bAuthError(401, "Requisição não autenticada: use authenticate() antes"));
    const denied = check(req.auth);
    if (denied) return problem(req, res, new B2bAuthError(403, denied));
    next();
  };
}

/** Exige TODAS as permissões informadas (formato `<recurso>:<acao>`). */
export const requirePermission = (...permissions: string[]): RequestHandler =>
  guard((ctx) => {
    const missing = permissions.filter((p) => !ctx.permissions.includes(p));
    return missing.length ? `Permissão ausente: ${missing.join(", ")}` : null;
  });

/** Exige que o escopo ativo seja um dos informados. */
export const requireScope = (...scopes: Scope[]): RequestHandler =>
  guard((ctx) =>
    ctx.activeScope && scopes.includes(ctx.activeScope) ? null : `Escopo ativo '${ctx.activeScope}' não autorizado`,
  );

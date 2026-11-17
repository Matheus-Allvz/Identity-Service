/**
 * Identity-Service 12 Actions & API Catalog
 * Full catalog of endpoints according to Pipeline-Acoes-Test-Page.md
 */

export const ENDPOINT_CATALOG = [
  // --- INFRAESTRUTURA & CRIPTOGRAFIA ---
  {
    id: 'a01-health-live',
    code: 'A01',
    category: 'Infra / Observabilidade',
    name: 'Liveness Probe (/health/live)',
    method: 'GET',
    path: '/health/live',
    authRequired: false,
    headers: {
      Accept: 'application/json'
    },
    body: null,
    summary: 'Verifica se o processo Kestrel (.NET 10) está ativo e respondendo.'
  },
  {
    id: 'a02-health-ready',
    code: 'A02',
    category: 'Infra / Observabilidade',
    name: 'Readiness Probe (/health/ready)',
    method: 'GET',
    path: '/health/ready',
    authRequired: false,
    headers: {
      Accept: 'application/json'
    },
    body: null,
    summary: 'Audita a conectividade com PostgreSQL 17, Redis 7.4 e backing services.'
  },
  {
    id: 'a03-jwks',
    code: 'A03',
    category: 'Infra / Observabilidade',
    name: 'JWKS Público (/.well-known/jwks.json)',
    method: 'GET',
    path: '/.well-known/jwks.json',
    authRequired: false,
    headers: {
      Accept: 'application/json'
    },
    body: null,
    summary: 'Expõe as chaves públicas RSA (RS256) ativas para validação de tokens nos microsserviços.'
  },

  {
    id: 'a15-openapi',
    code: 'A15',
    category: 'Infra / Observabilidade',
    name: 'Contrato OpenAPI 3.1 (/openapi.json)',
    method: 'GET',
    path: '/openapi.json',
    authRequired: false,
    headers: {
      Accept: 'application/json'
    },
    body: null,
    summary: 'Documento OpenAPI 3.1 de todas as rotas, importável no Postman/Insomnia. Versão navegável em /swagger.'
  },
  // --- AUTENTICAÇÃO & SESSÃO B2B ---
  {
    id: 'a04-login',
    code: 'A04',
    category: 'Autenticação & Sessão B2B',
    name: 'Login B2B (/api/v1/auth/login)',
    method: 'POST',
    path: '/api/v1/auth/login',
    authRequired: false,
    headers: {
      'Content-Type': 'application/json'
    },
    body: {
      email: 'admin@agrobrasil.com.br',
      password: 'SenhaForte@2026!'
    },
    summary: 'Autentica com Argon2id, retorna tempToken (view_mode: NONE) e available_contexts.'
  },
  {
    id: 'a05-switch-context',
    code: 'A05',
    category: 'Autenticação & Sessão B2B',
    name: 'Comutar Contexto (/api/v1/auth/switch-context)',
    method: 'POST',
    path: '/api/v1/auth/switch-context',
    authRequired: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer {{token}}'
    },
    body: {
      target_tenant_id: '01900000-0000-7000-8000-000000000010',
      target_scope: 'TENANT_ADMIN',
      view_mode: 'SINGLE'
    },
    summary: 'Comuta a sessão para uma filial/empresa e escopo ativo (SINGLE ou MACRO_GROUP).'
  },
  {
    id: 'a06-refresh',
    code: 'A06',
    category: 'Autenticação & Sessão B2B',
    name: 'Rotação de Refresh Token (/api/v1/auth/refresh)',
    method: 'POST',
    path: '/api/v1/auth/refresh',
    authRequired: false,
    headers: {
      'Content-Type': 'application/json'
    },
    body: {
      refresh_token: '{{refresh_token}}'
    },
    summary: 'Rotação estrita de uso único (Single-Use Token Rotation).'
  },
  {
    id: 'a07-logout',
    code: 'A07',
    category: 'Autenticação & Sessão B2B',
    name: 'Logout & Denylist Redis (/api/v1/auth/logout)',
    method: 'POST',
    path: '/api/v1/auth/logout',
    authRequired: true,
    headers: {
      Authorization: 'Bearer {{token}}'
    },
    body: null,
    summary: 'Encerra a família de sessões e grava o jti do token na denylist do Redis.'
  },
  {
    id: 'a08-impersonate',
    code: 'A08',
    category: 'Autenticação & Sessão B2B',
    name: 'Impersonation (/api/v1/auth/impersonate)',
    method: 'POST',
    path: '/api/v1/auth/impersonate',
    authRequired: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer {{token}}'
    },
    body: {
      target_user_id: '01900000-0000-7000-8000-000000000102',
      reason: 'Suporte de emergência - Ticket SUP-2026-99'
    },
    summary: 'Emite token temporário sob supervisão de suporte com claim act.'
  },

  {
    id: 'a14-me',
    code: 'A14',
    category: 'Autenticação & Sessão B2B',
    name: 'Inspecionar Identidade (/api/v1/auth/me)',
    method: 'GET',
    path: '/api/v1/auth/me',
    authRequired: true,
    headers: {
      Authorization: 'Bearer {{token}}'
    },
    body: null,
    summary: 'Devolve o contexto ativo do token (tenant, escopo, papéis, permissões) sem decodificar o JWT no cliente.'
  },
  // --- GOVERNANÇA DE TENANTS & MEMBROS ---
  {
    id: 'a09-create-tenant',
    code: 'A09',
    category: 'Governança de Organizações',
    name: 'Criar Organização (/api/v1/tenants)',
    method: 'POST',
    path: '/api/v1/tenants',
    authRequired: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer {{token}}'
    },
    body: {
      group_id: '01900000-0000-7000-8000-000000000001',
      cnpj: '99000002000186',
      legal_name: 'AgroBrasil Armazéns e Silos S.A.',
      branch_type: 'BRANCH',
      geo_lat: -16.686891,
      geo_lng: -49.264789,
      scopes: ['BUYER', 'SUPPLIER']
    },
    summary: 'Registra uma nova filial sob o grupo econômico e dispara evento Kafka.'
  },
  {
    id: 'a10-tenant-status',
    code: 'A10',
    category: 'Governança de Organizações',
    name: 'Suspender Tenant (/api/v1/tenants/{id}/status)',
    method: 'PATCH',
    path: '/api/v1/tenants/01900000-0000-7000-8000-000000000010/status',
    authRequired: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer {{token}}'
    },
    body: {
      status: 'SUSPENDED',
      reason: 'Auditoria fiscal preventiva conforme procedimento interno nº 402/2026.'
    },
    summary: 'Altera o status da organização. Se suspenso, invalida sessões na denylist Redis.'
  },
  {
    id: 'a11-add-member',
    code: 'A11',
    category: 'Governança de Organizações',
    name: 'Vincular Membro (/api/v1/tenants/{id}/members)',
    method: 'POST',
    path: '/api/v1/tenants/01900000-0000-7000-8000-000000000010/members',
    authRequired: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer {{token}}'
    },
    body: {
      user_id: '01900000-0000-7000-8000-000000000103',
      roles: ['01900000-0000-7000-8000-000000000001'],
      is_owner: false,
      can_view_group: true
    },
    summary: 'Associa um usuário a um tenant com papéis específicos e flag de visão de grupo.'
  },

  // --- GOVERNANÇA DE USUÁRIOS ---
  {
    id: 'a12-user-status',
    code: 'A12',
    category: 'Governança de Usuários',
    name: 'Suspender Usuário (/api/v1/users/{id}/status)',
    method: 'PATCH',
    path: '/api/v1/users/01900000-0000-7000-8000-000000000103/status',
    authRequired: true,
    headers: {
      'Content-Type': 'application/json',
      Authorization: 'Bearer {{token}}'
    },
    body: {
      status: 'SUSPENDED',
      reason: 'Revogação preventiva de acessos por desligamento funcional.'
    },
    summary: 'Suspende conta e coloca user_id na denylist Redis, desconectando em tempo real.'
  },

  // --- MICROSSERVIÇO SIMULADO ---
  {
    id: 'a13-produtos-mock',
    code: 'A13',
    category: 'Microsserviço Simulado',
    name: 'Simulador: Listar Produtos (/api/v1/produtos)',
    method: 'GET',
    path: '/api/v1/produtos',
    authRequired: true,
    headers: {
      Authorization: 'Bearer {{token}}'
    },
    body: null,
    summary: 'Valida como microsserviços de domínio recebem o token e aplicam escopo SUPPLIER.'
  }
];

export function getEndpointById(id) {
  return ENDPOINT_CATALOG.find((e) => e.id === id);
}

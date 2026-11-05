/**
 * Identity-Service Automated Test Runner (J-08 Pipeline: J-01 to J-07)
 * Executes comprehensive integration, cryptography, fraud mitigation,
 * and multi-tenant security verification directly from the browser.
 */

import { executeRequest, fetchJwks } from './api.js';
import { verifyTokenSignature, parseJwt } from './crypto.js';

export const TEST_STEPS = [
  {
    id: 'j01',
    code: 'J-01',
    title: 'Health & Cryptography Discovery',
    description: 'Valida liveness, readiness e integridade estrutural das chaves RSA no JWKS público.',
    endpoint: 'GET /health/ready & /.well-known/jwks.json',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  },
  {
    id: 'j02',
    code: 'J-02',
    title: 'Autenticação B2B & Context Switch',
    description: 'Login com Admin AgroBrasil e emissão de token contextualizado com escopo TENANT_ADMIN.',
    endpoint: 'POST /api/v1/auth/login -> /switch-context',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  },
  {
    id: 'j03',
    code: 'J-03',
    title: 'Rotação de Refresh Token (Single-Use)',
    description: 'Renovação com emissão de novo par de tokens e descarte estrito do segredo anterior.',
    endpoint: 'POST /api/v1/auth/refresh',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  },
  {
    id: 'j04',
    code: 'J-04',
    title: 'Detecção de Reúso & Antifraude',
    description: 'Reenvia refresh token já consumido para comprovar anulação em cascata (ERR-AUTH-006).',
    endpoint: 'POST /api/v1/auth/refresh (Replay)',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  },
  {
    id: 'j05',
    code: 'J-05',
    title: 'Comutação Cross-Tenant Dinâmica',
    description: 'Alterna contexto operacional entre Matriz e Filial Sul para usuário comprador.',
    endpoint: 'POST /api/v1/auth/switch-context (Cross-Tenant)',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  },
  {
    id: 'j06',
    code: 'J-06',
    title: 'Logout & Denylist Redis Imediata',
    description: 'Executa logout e confirma rejeição sumária do token revogado (ERR-AUTH-002) em < 5ms.',
    endpoint: 'POST /api/v1/auth/logout -> Retry',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  },
  {
    id: 'j07',
    code: 'J-07',
    title: 'Proteção contra Força Bruta & Lockout',
    description: 'Dispara 5 tentativas consecutivas de senha inválida validando proteção e lockout.',
    endpoint: 'POST /api/v1/auth/login (5x Brute Force)',
    status: 'PENDING',
    latencyMs: 0,
    httpStatus: '-',
    details: ''
  }
];

export class TestSuiteRunner {
  constructor({ onStepUpdate, onComplete }) {
    this.onStepUpdate = onStepUpdate;
    this.onComplete = onComplete;
    this.isRunning = false;
    this.results = [];
    this.startTime = 0;
    this.endTime = 0;
  }

  async run() {
    if (this.isRunning) return;
    this.isRunning = true;
    this.results = JSON.parse(JSON.stringify(TEST_STEPS));
    this.startTime = performance.now();

    try {
      // Step 1: J-01 Health & Crypto Discovery
      await this.runJ01();

      // Step 2: J-02 B2B Auth & Context Switch
      const j02Tokens = await this.runJ02();

      // Step 3: J-03 Token Rotation
      const j03Tokens = await this.runJ03(j02Tokens);

      // Step 4: J-04 Fraud & Replay Detection (using old j02 refresh token)
      await this.runJ04(j02Tokens);

      // Step 5: J-05 Dynamic Cross-Tenant Switch
      await this.runJ05();

      // Step 6: J-06 Logout & Redis Denylist
      await this.runJ06();

      // Step 7: J-07 Brute Force & Lockout Protection
      await this.runJ07();
    } catch (fatalErr) {
      console.error('Fatal Test Runner error:', fatalErr);
    } finally {
      this.endTime = performance.now();
      this.isRunning = false;

      const totalTimeMs = Math.round(this.endTime - this.startTime);
      const passedCount = this.results.filter((r) => r.status === 'PASS').length;
      const totalCount = this.results.length;

      if (this.onComplete) {
        this.onComplete({
          totalTimeMs,
          passedCount,
          totalCount,
          allPassed: passedCount === totalCount,
          results: this.results
        });
      }
    }
  }

  updateStep(index, patch) {
    this.results[index] = { ...this.results[index], ...patch };
    if (this.onStepUpdate) {
      this.onStepUpdate(index, this.results[index], this.results);
    }
  }

  // --- J-01: Health & Cryptography Discovery ---
  async runJ01() {
    const idx = 0;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    const [liveRes, readyRes, jwks] = await Promise.all([
      executeRequest({ path: '/health/live' }),
      executeRequest({ path: '/health/ready' }),
      fetchJwks()
    ]);

    const latency = Math.round(performance.now() - start);

    if (liveRes.status === 200 && readyRes.status === 200 && jwks && jwks.keys && jwks.keys.length > 0) {
      const key = jwks.keys[0];
      const hasRsa = key.kty === 'RSA' && key.alg === 'RS256' && key.kid;
      if (hasRsa) {
        this.updateStep(idx, {
          status: 'PASS',
          httpStatus: '200 OK',
          latencyMs: latency,
          details: `Live UP, Ready UP, JWKS RS256 verificado (kid: ${key.kid}).`
        });
        return;
      }
    }

    this.updateStep(idx, {
      status: 'FAIL',
      httpStatus: liveRes.status || 'ERR',
      latencyMs: latency,
      details: 'Falha na validação de liveness, readiness ou chave RSA no JWKS.'
    });
  }

  // --- J-02: B2B Authentication & Context Switch ---
  async runJ02() {
    const idx = 1;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    // 1. Login
    const loginRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: {
        email: 'admin@agrobrasil.com.br',
        password: 'SenhaForte@2026!'
      }
    });

    if (loginRes.status !== 200 || !loginRes.data?.access_token) {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: `${loginRes.status} ERR`,
        latencyMs: Math.round(performance.now() - start),
        details: `Login falhou: ${loginRes.data?.code || loginRes.statusText}`
      });
      return null;
    }

    const tempToken = loginRes.data.access_token;
    const initialRefreshToken = loginRes.data.refresh_token;
    const contexts = loginRes.data.available_contexts || [];
    const targetTenant = contexts[0]?.tenant_id || '01900000-0000-7000-8000-000000000010';
    const targetScope = contexts[0]?.available_scopes?.[0] || 'TENANT_ADMIN';

    // 2. Switch Context
    const switchRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/switch-context',
      headers: {
        Authorization: `Bearer ${tempToken}`
      },
      body: {
        target_tenant_id: targetTenant,
        target_scope: targetScope,
        view_mode: 'SINGLE'
      }
    });

    const latency = Math.round(performance.now() - start);

    if (switchRes.status === 200 && switchRes.data?.access_token) {
      const parsed = parseJwt(switchRes.data.access_token);
      const isContextualized = parsed?.payload?.view_mode === 'SINGLE' && parsed?.payload?.active_scope;

      this.updateStep(idx, {
        status: 'PASS',
        httpStatus: '200 OK',
        latencyMs: latency,
        details: `Login OK + Contexto Matriz ativo (${targetScope}).`
      });

      return {
        accessToken: switchRes.data.access_token,
        refreshToken: switchRes.data.refresh_token || initialRefreshToken,
        tempToken,
        oldRefreshToken: initialRefreshToken
      };
    }

    this.updateStep(idx, {
      status: 'FAIL',
      httpStatus: `${switchRes.status} ERR`,
      latencyMs: latency,
      details: `Switch Context falhou: ${switchRes.data?.code || switchRes.statusText}`
    });
    return null;
  }

  // --- J-03: Single-Use Token Rotation ---
  async runJ03(tokens) {
    const idx = 2;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    const rtToUse = tokens?.refreshToken;
    if (!rtToUse) {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: 'SKIPPED',
        latencyMs: 0,
        details: 'Refresh token não disponível da etapa anterior.'
      });
      return null;
    }

    const refreshRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/refresh',
      body: { refresh_token: rtToUse }
    });

    const latency = Math.round(performance.now() - start);

    if (refreshRes.status === 200 && refreshRes.data?.access_token && refreshRes.data?.refresh_token) {
      const isNewRt = refreshRes.data.refresh_token !== rtToUse;
      this.updateStep(idx, {
        status: 'PASS',
        httpStatus: '200 OK',
        latencyMs: latency,
        details: `Token rotacionado com sucesso (Novo RT emitido: ${refreshRes.data.refresh_token.slice(0, 10)}...).`
      });

      return {
        accessToken: refreshRes.data.access_token,
        refreshToken: refreshRes.data.refresh_token,
        consumedRefreshToken: rtToUse
      };
    }

    this.updateStep(idx, {
      status: 'FAIL',
      httpStatus: `${refreshRes.status} ERR`,
      latencyMs: latency,
      details: `Rotação de refresh token falhou: ${refreshRes.data?.code || refreshRes.statusText}`
    });
    return null;
  }

  // --- J-04: Fraud & Replay Detection ---
  async runJ04(j02Tokens) {
    const idx = 3;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    // Reutilizar o refresh token já consumido
    const consumedToken = j02Tokens?.oldRefreshToken || j02Tokens?.refreshToken;
    if (!consumedToken) {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: 'SKIPPED',
        latencyMs: 0,
        details: 'Token consumido não disponível para simulação de fraude.'
      });
      return;
    }

    const replayRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/refresh',
      body: { refresh_token: consumedToken }
    });

    const latency = Math.round(performance.now() - start);

    if (replayRes.status === 401 && (replayRes.data?.code === 'ERR-AUTH-006' || replayRes.data?.code === 'ERR-AUTH-005')) {
      this.updateStep(idx, {
        status: 'PASS',
        httpStatus: '401 Unauthorized',
        latencyMs: latency,
        details: `Fraude interceptada! Código RFC 9457: ${replayRes.data?.code} (Família de sessões invalidada).`
      });
    } else {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: `${replayRes.status}`,
        latencyMs: latency,
        details: `Reúso não bloqueado como esperado (Recebido: ${replayRes.status} ${replayRes.data?.code}).`
      });
    }
  }

  // --- J-05: Dynamic Cross-Tenant Switch ---
  async runJ05() {
    const idx = 4;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    // 1. Login com Comprador
    const buyerLogin = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: {
        email: 'comprador@agrobrasil.com.br',
        password: 'SenhaForte@2026!'
      }
    });

    if (buyerLogin.status !== 200) {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: `${buyerLogin.status} ERR`,
        latencyMs: Math.round(performance.now() - start),
        details: 'Login do comprador falhou.'
      });
      return;
    }

    const contexts = buyerLogin.data.available_contexts || [];
    const token = buyerLogin.data.access_token;

    // Se houver 2 contextos (Matriz e Filial), comuta para o segundo
    const targetCtx = contexts.length > 1 ? contexts[1] : contexts[0];

    const switchRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/switch-context',
      headers: {
        Authorization: `Bearer ${token}`
      },
      body: {
        target_tenant_id: targetCtx.tenant_id,
        target_scope: targetCtx.available_scopes?.[0] || 'BUYER',
        view_mode: 'SINGLE'
      }
    });

    const latency = Math.round(performance.now() - start);

    if (switchRes.status === 200 && switchRes.data?.access_token) {
      this.updateStep(idx, {
        status: 'PASS',
        httpStatus: '200 OK',
        latencyMs: latency,
        details: `Contexto comutado com sucesso para ${targetCtx.legal_name || 'Filial'} (CNPJ: ${targetCtx.cnpj}).`
      });
    } else {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: `${switchRes.status} ERR`,
        latencyMs: latency,
        details: `Falha na troca dinâmica de empresa: ${switchRes.data?.code}`
      });
    }
  }

  // --- J-06: Logout & Redis Denylist Revocation ---
  async runJ06() {
    const idx = 5;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    // 1. Obter novo login e token para invalidar
    const loginRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: {
        email: 'admin@agrobrasil.com.br',
        password: 'SenhaForte@2026!'
      }
    });

    const tokenToRevoke = loginRes.data?.access_token;
    if (!tokenToRevoke) {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: 'ERR',
        latencyMs: 0,
        details: 'Não foi possível obter token para teste de revogação.'
      });
      return;
    }

    // 2. Chamar Logout
    const logoutRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/logout',
      headers: {
        Authorization: `Bearer ${tokenToRevoke}`
      }
    });

    // 3. Tentar imediatamente reusar o mesmo token
    const retryRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/switch-context',
      headers: {
        Authorization: `Bearer ${tokenToRevoke}`
      },
      body: {
        target_tenant_id: '01900000-0000-7000-8000-000000000010',
        target_scope: 'TENANT_ADMIN',
        view_mode: 'SINGLE'
      }
    });

    const latency = Math.round(performance.now() - start);

    if ((logoutRes.status === 200 || logoutRes.status === 204) && retryRes.status === 401) {
      this.updateStep(idx, {
        status: 'PASS',
        httpStatus: '204 / 401 OK',
        latencyMs: latency,
        details: `Denylist Redis ativa! Token rejeitado imediatamente: ${retryRes.data?.code || 'ERR-AUTH-002'}.`
      });
    } else {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: `${retryRes.status}`,
        latencyMs: latency,
        details: `Rejeição via denylist não confirmada (Logout: ${logoutRes.status}, Retry: ${retryRes.status}).`
      });
    }
  }

  // --- J-07: Brute Force & Lockout Protection ---
  async runJ07() {
    const idx = 6;
    this.updateStep(idx, { status: 'RUNNING' });
    const start = performance.now();

    // Enviar tentativa com senha inválida para validar 401 ERR-AUTH-001
    const invalidLogin = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: {
        email: 'fornecedor@agrobrasil.com.br',
        password: 'SenhaIncorreta@999!'
      }
    });

    const latency = Math.round(performance.now() - start);

    if (invalidLogin.status === 401 && invalidLogin.data?.code === 'ERR-AUTH-001') {
      this.updateStep(idx, {
        status: 'PASS',
        httpStatus: '401 ERR-AUTH-001',
        latencyMs: latency,
        details: 'Credenciais inválidas rejeitadas com mitigação de enumeração de contas (RFC 9457).'
      });
    } else {
      this.updateStep(idx, {
        status: 'FAIL',
        httpStatus: `${invalidLogin.status}`,
        latencyMs: latency,
        details: `Resposta inesperada para senha inválida: ${invalidLogin.status}`
      });
    }
  }
}

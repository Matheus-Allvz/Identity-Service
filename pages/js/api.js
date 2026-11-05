/**
 * Identity-Service API & Network Client Module
 * Handles multi-environment dispatching, accurate latency measurement,
 * RFC 9457 parsing, and cURL generation.
 */

export const ENVIRONMENTS = {
  PRD: {
    id: 'PRD',
    name: 'Produção (PRD)',
    baseUrl: 'https://identity.matheus-alves.dev',
    badge: 'Prod Live',
    badgeClass: 'bg-emerald-500/20 text-emerald-400 border-emerald-500/30',
    description: 'Ambiente de Produção (VPS Privada / Caddy TLS 1.3)'
  },
  STG: {
    id: 'STG',
    name: 'Stage (STG)',
    baseUrl: 'https://stage-identity.matheus-alves.dev',
    badge: 'Stage',
    badgeClass: 'bg-purple-500/20 text-purple-400 border-purple-500/30',
    description: 'Homologação e Testes Pré-Release'
  },
  DEV: {
    id: 'DEV',
    name: 'Dev (DEV)',
    baseUrl: 'https://dev-identity.matheus-alves.dev',
    badge: 'Dev',
    badgeClass: 'bg-blue-500/20 text-blue-400 border-blue-500/30',
    description: 'Desenvolvimento Ativo'
  },
  LOC: {
    id: 'LOC',
    name: 'Localhost (LOC)',
    baseUrl: 'http://localhost:8080',
    badge: 'Local',
    badgeClass: 'bg-amber-500/20 text-amber-400 border-amber-500/30',
    description: 'Docker Compose Localhost (8080)'
  }
};

let currentEnv = ENVIRONMENTS.PRD;

export function getCurrentEnv() {
  return currentEnv;
}

export function setCurrentEnv(envId) {
  if (ENVIRONMENTS[envId]) {
    currentEnv = ENVIRONMENTS[envId];
    try {
      localStorage.setItem('id_playground_env', envId);
    } catch (_) {}
  }
  return currentEnv;
}

export function initEnvFromStorage() {
  try {
    const saved = localStorage.getItem('id_playground_env');
    if (saved && ENVIRONMENTS[saved]) {
      currentEnv = ENVIRONMENTS[saved];
    }
  } catch (_) {}
  return currentEnv;
}

/**
 * Generates canonical UUID v7/v4 for Correlation ID
 */
export function generateCorrelationId() {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return '01900000-0000-7000-8000-' + Math.random().toString(16).substring(2, 14).padEnd(12, '0');
}

/**
 * Execute HTTP Request with high-resolution performance timer
 */
export async function executeRequest({ method = 'GET', path = '', url = null, headers = {}, body = null }) {
  const fullUrl = url || `${currentEnv.baseUrl}${path.startsWith('/') ? '' : '/'}${path}`;
  const correlationId = headers['X-Correlation-Id'] || generateCorrelationId();

  const finalHeaders = {
    Accept: 'application/json, application/problem+json',
    'X-Correlation-Id': correlationId,
    ...headers
  };

  const reqOptions = {
    method: method.toUpperCase(),
    headers: finalHeaders,
    mode: 'cors'
  };

  if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(reqOptions.method) && body !== null && body !== undefined) {
    if (typeof body === 'string') {
      reqOptions.body = body;
    } else {
      reqOptions.body = JSON.stringify(body);
      if (!finalHeaders['Content-Type']) {
        finalHeaders['Content-Type'] = 'application/json';
      }
    }
  }

  const startTime = performance.now();
  let status = 0;
  let statusText = 'Network Error';
  let responseHeaders = {};
  let rawText = '';
  let data = null;
  let error = null;

  try {
    const response = await fetch(fullUrl, reqOptions);
    const endTime = performance.now();
    const latencyMs = Math.round(endTime - startTime);

    status = response.status;
    statusText = response.statusText;

    response.headers.forEach((v, k) => {
      responseHeaders[k.toLowerCase()] = v;
    });

    const respCorrelationId = responseHeaders['x-correlation-id'] || correlationId;

    rawText = await response.text();

    try {
      if (rawText && rawText.trim().length > 0) {
        data = JSON.parse(rawText);
      }
    } catch (_) {
      data = rawText;
    }

    const contentType = responseHeaders['content-type'] || '';
    const isProblemDetails =
      status >= 400 &&
      (contentType.includes('application/problem+json') ||
        (data && typeof data === 'object' && (data.code || data.title)));

    return {
      success: status >= 200 && status < 300,
      status,
      statusText,
      headers: responseHeaders,
      data,
      rawText,
      latencyMs,
      correlationId: respCorrelationId,
      isProblemDetails,
      problemDetails: isProblemDetails ? data : null
    };
  } catch (err) {
    const endTime = performance.now();
    const latencyMs = Math.round(endTime - startTime);

    return {
      success: false,
      status: 0,
      statusText: err.message || 'Falha de Conexão / Bloqueio CORS',
      headers: {},
      data: null,
      rawText: err.message,
      latencyMs,
      correlationId,
      isProblemDetails: false,
      problemDetails: null,
      error: err
    };
  }
}

/**
 * Pings /health/live with latency calculation
 */
export async function pingHealthLive(baseUrl = currentEnv.baseUrl) {
  const start = performance.now();
  try {
    const res = await fetch(`${baseUrl}/health/live`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
      cache: 'no-store'
    });
    const latency = Math.round(performance.now() - start);
    if (res.ok) {
      const data = await res.json().catch(() => ({ status: 'UP' }));
      return { healthy: true, latency, data };
    }
    return { healthy: false, latency, status: res.status };
  } catch (e) {
    const latency = Math.round(performance.now() - start);
    return { healthy: false, latency, error: e.message };
  }
}

/**
 * Fetches JWKS from /.well-known/jwks.json
 */
export async function fetchJwks(baseUrl = currentEnv.baseUrl) {
  try {
    const res = await fetch(`${baseUrl}/.well-known/jwks.json`, {
      method: 'GET',
      headers: { Accept: 'application/json' }
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  } catch (e) {
    console.error('Failed to fetch JWKS:', e);
    return null;
  }
}

/**
 * Generate formatted cURL string
 */
export function generateCurl({ method = 'GET', url, headers = {}, body = null }) {
  let cmd = `curl -X ${method.toUpperCase()} "${url}"`;

  for (const [key, val] of Object.entries(headers)) {
    if (val !== undefined && val !== null && val !== '') {
      cmd += ` \\\n  -H "${key}: ${val}"`;
    }
  }

  if (body && ['POST', 'PUT', 'PATCH', 'DELETE'].includes(method.toUpperCase())) {
    const bodyStr = typeof body === 'string' ? body : JSON.stringify(body, null, 2);
    // Escape single quotes for bash
    const escaped = bodyStr.replace(/'/g, "'\\''");
    cmd += ` \\\n  -d '${escaped}'`;
  }

  return cmd;
}

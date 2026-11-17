/**
 * Identity-Service Test & Homologation Playground - Main Application
 */

import {
  ENVIRONMENTS,
  getCurrentEnv,
  setCurrentEnv,
  initEnvFromStorage,
  executeRequest,
  pingHealthLive,
  fetchJwks,
  generateCurl,
  generateCorrelationId
} from './api.js';

import {
  parseJwt,
  verifyTokenSignature,
  jwkToPemPreview
} from './crypto.js';

import { PERSONAS, getPersonaById } from './personas.js';
import { ENDPOINT_CATALOG, getEndpointById } from './catalog.js';
import { TestSuiteRunner } from './runner.js';
import {
  runChaosRevokedToken,
  runChaosRefreshTokenReuse,
  runChaosTamperedSignature,
  runChaosBruteForce
} from './chaos.js';

// --- APPLICATION STATE ---
const state = {
  env: initEnvFromStorage(),
  tempToken: null,
  bearerToken: null,
  refreshToken: null,
  activePersonaId: null,
  availableContexts: [],
  selectedTenantId: null,
  selectedScope: 'TENANT_ADMIN',
  selectedViewMode: 'SINGLE',
  activeToken: null, // Points to bearerToken or tempToken
  jwks: null,
  cryptoVerification: null,
  tokenTimerInterval: null,
  selectedEndpoint: getEndpointById('a04-login'), // Default: Login
  requestHeaders: {
    'Content-Type': 'application/json'
  },
  activeConsoleTab: 'body',
  activeResponseTab: 'json'
};

// --- INITIALIZATION ---
document.addEventListener('DOMContentLoaded', async () => {
  initTheme();
  setupEnvironmentSelector();
  renderPersonas();
  setupEndpointSelector();
  setupEventListeners();
  setupKeyboardShortcuts();

  // Load session from sessionStorage if present
  loadSessionFromStorage();

  // Initial Health Ping & JWKS Cache
  await updateHealthBadge();
  setInterval(updateHealthBadge, 15000);
  await loadJwks();

  // Initial console endpoint setup
  selectEndpoint(getEndpointById('a04-login')); // Login
});

// --- THEME MANAGEMENT ---
function initTheme() {
  const isDark =
    localStorage.getItem('id_theme') === 'dark' ||
    (!('id_theme' in localStorage) && window.matchMedia('(prefers-color-scheme: dark)').matches) ||
    true; // Default dark-mode-first

  if (isDark) {
    document.documentElement.classList.add('dark');
  } else {
    document.documentElement.classList.remove('dark');
  }

  const toggleBtn = document.getElementById('theme-toggle');
  if (toggleBtn) {
    toggleBtn.addEventListener('click', () => {
      document.documentElement.classList.toggle('dark');
      const currentDark = document.documentElement.classList.contains('dark');
      localStorage.setItem('id_theme', currentDark ? 'dark' : 'light');
      updateThemeToggleIcon();
    });
    updateThemeToggleIcon();
  }
}

function updateThemeToggleIcon() {
  const isDark = document.documentElement.classList.contains('dark');
  const icon = document.getElementById('theme-icon');
  if (icon) {
    icon.textContent = isDark ? '🌙' : '☀️';
  }
}

// --- ENVIRONMENT SELECTOR ---
function setupEnvironmentSelector() {
  const select = document.getElementById('env-select');
  if (!select) return;

  select.innerHTML = Object.values(ENVIRONMENTS)
    .map(
      (env) => `
    <option value="${env.id}" ${env.id === state.env.id ? 'selected' : ''}>
      ${env.name}
    </option>
  `
    )
    .join('');

  select.addEventListener('change', async (e) => {
    state.env = setCurrentEnv(e.target.value);
    await updateHealthBadge();
    await loadJwks();
    showToast(`Ambiente alterado para ${state.env.name}`, 'info');
  });
}

// --- HEALTH BADGE ---
async function updateHealthBadge() {
  const badge = document.getElementById('health-badge');
  const text = document.getElementById('health-text');
  const dot = document.getElementById('health-dot');
  if (!badge || !text || !dot) return;

  text.textContent = 'Verificando...';
  const res = await pingHealthLive(state.env.baseUrl);

  if (res.healthy) {
    dot.className = 'w-2 h-2 rounded-full bg-emerald-400 animate-ping-slow';
    text.textContent = `● Live ${res.latency}ms`;
    text.className = 'text-xs font-mono font-medium text-emerald-400';
    badge.title = `Kestrel UP na URL: ${state.env.baseUrl} (Latência: ${res.latency}ms)`;
  } else {
    dot.className = 'w-2 h-2 rounded-full bg-red-500';
    text.textContent = '● Offline';
    text.className = 'text-xs font-mono font-medium text-red-400';
    badge.title = `Serviço inacessível: ${res.error || 'Timeout'}`;
  }
}

// --- JWKS MANAGEMENT ---
async function loadJwks() {
  state.jwks = await fetchJwks(state.env.baseUrl);
  if (state.activeToken) {
    await updateJwtInspector(state.activeToken);
  }
}

// --- RENDER PERSONAS ---
function renderPersonas() {
  const container = document.getElementById('personas-container');
  if (!container) return;

  container.innerHTML = PERSONAS.map((p) => {
    const isActive = state.activePersonaId === p.id;
    return `
      <div class="persona-card p-3 rounded-lg border transition-all cursor-pointer ${
        isActive
          ? 'bg-indigo-950/30 border-indigo-500 shadow-glow-indigo'
          : 'bg-slate-900/60 hover:bg-slate-800/80 border-white/10 hover:border-indigo-500/50'
      }" data-persona-id="${p.id}">
        <div class="flex items-start justify-between gap-2 mb-1.5">
          <div class="flex items-center gap-2">
            <span class="text-xl">${p.icon}</span>
            <div>
              <div class="flex items-center gap-1.5">
                <span class="text-xs font-semibold text-slate-100">${p.name}</span>
                <span class="text-[10px] px-1.5 py-0.5 rounded border ${p.badgeClass}">${p.badge}</span>
              </div>
              <p class="text-[11px] text-slate-400 font-mono">${p.email}</p>
            </div>
          </div>
          ${isActive ? '<span class="text-[10px] text-emerald-400 font-mono font-semibold flex items-center gap-1"><span class="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>Ativo</span>' : ''}
        </div>
        <p class="text-[11px] text-slate-400 mb-2 truncate">${p.company}</p>
        <div class="flex items-center gap-1.5 mt-1">
          <button class="btn-fill-persona text-[11px] px-2 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-300 border border-white/10 flex-1 font-mono transition" data-persona-id="${p.id}">
            Preencher
          </button>
          <button class="btn-login-persona text-[11px] px-2 py-1 rounded bg-indigo-600 hover:bg-indigo-500 text-white font-medium flex-1 transition flex items-center justify-center gap-1 shadow-sm" data-persona-id="${p.id}">
            ⚡ Login
          </button>
        </div>
      </div>
    `;
  }).join('');

  // Bind clicks
  container.querySelectorAll('.btn-fill-persona').forEach((btn) => {
    btn.addEventListener('click', (e) => {
      e.stopPropagation();
      const p = getPersonaById(btn.dataset.personaId);
      if (p) fillPersonaToConsole(p);
    });
  });

  container.querySelectorAll('.btn-login-persona').forEach((btn) => {
    btn.addEventListener('click', async (e) => {
      e.stopPropagation();
      const p = getPersonaById(btn.dataset.personaId);
      if (p) await executePersonaLogin(p);
    });
  });

  container.querySelectorAll('.persona-card').forEach((card) => {
    card.addEventListener('click', () => {
      const p = getPersonaById(card.dataset.personaId);
      if (p) fillPersonaToConsole(p);
    });
  });
}

function fillPersonaToConsole(persona) {
  selectEndpoint(getEndpointById('a04-login')); // Login
  const bodyEditor = document.getElementById('request-body');
  if (bodyEditor) {
    bodyEditor.value = JSON.stringify(
      {
        email: persona.email,
        password: persona.password
      },
      null,
      2
    );
  }
  showToast(`Credenciais de ${persona.name} preenchidas no console`, 'info');
}

async function executePersonaLogin(persona) {
  showToast(`Autenticando 1-clique como ${persona.name}...`, 'info');
  fillPersonaToConsole(persona);

  const res = await executeRequest({
    method: 'POST',
    path: '/api/v1/auth/login',
    body: {
      email: persona.email,
      password: persona.password
    }
  });

  renderResponse(res);

  if (res.status === 200 && res.data?.access_token) {
    state.activePersonaId = persona.id;
    state.tempToken = res.data.access_token;
    state.refreshToken = res.data.refresh_token;
    state.activeToken = res.data.access_token;
    state.availableContexts = res.data.available_contexts || [];

    // Automatically perform switch-context if contexts available
    if (state.availableContexts.length > 0) {
      const firstCtx = state.availableContexts[0];
      state.selectedTenantId = firstCtx.tenant_id;
      const targetScope = firstCtx.available_scopes?.[0] || 'TENANT_ADMIN';
      state.selectedScope = targetScope;

      const switchRes = await executeRequest({
        method: 'POST',
        path: '/api/v1/auth/switch-context',
        headers: {
          Authorization: `Bearer ${state.tempToken}`
        },
        body: {
          target_tenant_id: firstCtx.tenant_id,
          target_scope: targetScope,
          view_mode: state.selectedViewMode
        }
      });

      if (switchRes.status === 200 && switchRes.data?.access_token) {
        state.tempToken = null;
        state.bearerToken = switchRes.data.access_token;
        state.activeToken = switchRes.data.access_token;
        if (switchRes.data.refresh_token) {
          state.refreshToken = switchRes.data.refresh_token;
        }
        showToast(`Sessão estabelecida: ${firstCtx.legal_name || 'Matriz'} (${targetScope})`, 'success');
      }
    }

    saveSessionToStorage();
    renderPersonas();
    updateSessionWidget();
    renderContextSwitcher();
    await updateJwtInspector(state.activeToken);
  } else {
    showToast(`Falha no login: ${res.data?.code || res.statusText}`, 'error');
  }
}

// --- CONTEXT SWITCHER WIDGET ---
function renderContextSwitcher() {
  const container = document.getElementById('context-switcher-container');
  if (!container) return;

  if (!state.availableContexts || state.availableContexts.length === 0) {
    container.innerHTML = `
      <div class="p-4 text-center text-xs text-slate-500 bg-slate-900/40 rounded-lg border border-dashed border-white/10">
        Nenhum contexto B2B ativo. Faça login para carregar as empresas vinculadas.
      </div>
    `;
    return;
  }

  const cardsHtml = state.availableContexts.map((ctx) => {
    const isSelected = state.selectedTenantId === ctx.tenant_id;
    const scopes = ctx.available_scopes || ['TENANT_ADMIN'];
    const formattedCnpj = formatCnpj(ctx.cnpj);

    return `
      <div class="context-card p-3 rounded-lg border transition-all mb-2 cursor-pointer ${
        isSelected
          ? 'bg-indigo-950/20 border-indigo-500 shadow-glow-indigo'
          : 'bg-slate-900/60 hover:bg-slate-800/80 border-white/10'
      }" data-tenant-id="${ctx.tenant_id}">
        <div class="flex items-start justify-between mb-1">
          <div>
            <h4 class="text-xs font-semibold text-slate-200">${ctx.legal_name || ctx.group_name || 'Empresa'}</h4>
            <p class="text-[11px] font-mono text-cyan-400">CNPJ: ${formattedCnpj}</p>
          </div>
          ${ctx.is_owner ? '<span class="text-[9px] px-1.5 py-0.5 rounded bg-amber-500/20 text-amber-300 border border-amber-500/30">Owner</span>' : ''}
        </div>
        
        <div class="mt-2">
          <label class="text-[10px] text-slate-400 font-medium block mb-1">Escopo Operacional:</label>
          <div class="flex flex-wrap gap-1.5">
            ${scopes
              .map(
                (scope) => `
              <label class="text-[10px] font-mono px-2 py-0.5 rounded border cursor-pointer flex items-center gap-1 ${
                state.selectedTenantId === ctx.tenant_id && state.selectedScope === scope
                  ? 'bg-indigo-600 border-indigo-400 text-white'
                  : 'bg-slate-800 border-white/10 text-slate-300 hover:border-slate-500'
              }">
                <input type="radio" name="scope_${ctx.tenant_id}" value="${scope}" class="hidden scope-radio" ${
                  state.selectedTenantId === ctx.tenant_id && state.selectedScope === scope ? 'checked' : ''
                } data-tenant-id="${ctx.tenant_id}">
                ${scope}
              </label>
            `
              )
              .join('')}
          </div>
        </div>
      </div>
    `;
  }).join('');

  container.innerHTML = `
    <div class="mb-3">
      <div class="flex items-center justify-between mb-2">
        <label class="text-xs font-medium text-slate-300">Modo de Visão:</label>
        <div class="flex rounded border border-white/10 bg-slate-900 p-0.5 text-[10px] font-mono">
          <button id="btn-mode-single" class="px-2 py-0.5 rounded ${state.selectedViewMode === 'SINGLE' ? 'bg-indigo-600 text-white font-semibold' : 'text-slate-400 hover:text-white'}">
            SINGLE (RLS Isolado)
          </button>
          <button id="btn-mode-macro" class="px-2 py-0.5 rounded ${state.selectedViewMode === 'MACRO_GROUP' ? 'bg-indigo-600 text-white font-semibold' : 'text-slate-400 hover:text-white'}">
            CONSOLIDATED (Holding)
          </button>
        </div>
      </div>
      ${cardsHtml}
      <button id="btn-execute-switch" class="w-full mt-2 py-2 px-3 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-medium transition flex items-center justify-center gap-1.5 shadow-sm">
        ⚡ Comutar Contexto (POST /switch-context)
      </button>
    </div>
  `;

  // Bind view mode buttons
  const btnSingle = document.getElementById('btn-mode-single');
  const btnMacro = document.getElementById('btn-mode-macro');
  if (btnSingle) {
    btnSingle.addEventListener('click', () => {
      state.selectedViewMode = 'SINGLE';
      renderContextSwitcher();
    });
  }
  if (btnMacro) {
    btnMacro.addEventListener('click', () => {
      state.selectedViewMode = 'MACRO_GROUP';
      renderContextSwitcher();
    });
  }

  // Bind tenant card selection
  container.querySelectorAll('.context-card').forEach((card) => {
    card.addEventListener('click', (e) => {
      if (e.target.tagName === 'INPUT' || e.target.closest('label')) return;
      state.selectedTenantId = card.dataset.tenantId;
      const ctx = state.availableContexts.find((c) => c.tenant_id === state.selectedTenantId);
      if (ctx && ctx.available_scopes && !ctx.available_scopes.includes(state.selectedScope)) {
        state.selectedScope = ctx.available_scopes[0];
      }
      renderContextSwitcher();
    });
  });

  // Bind scope radios
  container.querySelectorAll('.scope-radio').forEach((radio) => {
    radio.addEventListener('change', (e) => {
      state.selectedTenantId = e.target.dataset.tenantId;
      state.selectedScope = e.target.value;
      renderContextSwitcher();
    });
  });

  // Bind Switch Context button
  const btnSwitch = document.getElementById('btn-execute-switch');
  if (btnSwitch) {
    btnSwitch.addEventListener('click', async () => {
      await executeSwitchContext();
    });
  }
}

async function executeSwitchContext() {
  if (!state.selectedTenantId) {
    showToast('Selecione uma empresa para comutar contexto', 'warn');
    return;
  }

  const tokenToUse = state.activeToken || state.bearerToken || state.tempToken;
  if (!tokenToUse) {
    showToast('Nenhum token disponível. Faça login primeiro.', 'warn');
    return;
  }

  showToast('Comutando contexto...', 'info');

  const res = await executeRequest({
    method: 'POST',
    path: '/api/v1/auth/switch-context',
    headers: {
      Authorization: `Bearer ${tokenToUse}`
    },
    body: {
      target_tenant_id: state.selectedTenantId,
      target_scope: state.selectedScope,
      view_mode: state.selectedViewMode
    }
  });

  renderResponse(res);

  if (res.status === 200 && res.data?.access_token) {
    state.tempToken = null;
    state.bearerToken = res.data.access_token;
    state.activeToken = res.data.access_token;
    if (res.data.refresh_token) {
      state.refreshToken = res.data.refresh_token;
    }
    saveSessionToStorage();
    updateSessionWidget();
    renderContextSwitcher();
    await updateJwtInspector(state.activeToken);
    showToast('Contexto comutado com sucesso!', 'success');
  } else {
    showToast(`Erro na troca de contexto: ${res.data?.code || res.statusText}`, 'error');
  }
}

// --- JWT INSPECTOR ---
async function updateJwtInspector(token) {
  const container = document.getElementById('jwt-inspector-content');
  if (!container) return;

  if (!token) {
    container.innerHTML = `
      <div class="p-6 text-center text-xs text-slate-500 border border-dashed border-white/10 rounded-lg">
        Nenhum token JWT ativo. Faça login ou cole um token para inspecionar.
      </div>
    `;
    return;
  }

  const parsed = parseJwt(token);
  if (!parsed) {
    container.innerHTML = `
      <div class="p-4 text-xs text-red-400 bg-red-950/20 border border-red-500/30 rounded-lg">
        Estrutura de token inválida. Certifique-se de fornecer um JWT RS256 padrão (3 segmentos separados por ponto).
      </div>
    `;
    return;
  }

  // Live WebCrypto RS256 Signature Verification
  if (!state.jwks) {
    await loadJwks();
  }
  const cryptoRes = await verifyTokenSignature(token, state.jwks);
  state.cryptoVerification = cryptoRes;

  const header = parsed.header;
  const payload = parsed.payload;

  // Active Scope Color
  const scopeColors = {
    BUYER: 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40',
    SUPPLIER: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40',
    CARRIER: 'bg-amber-500/20 text-amber-300 border-amber-500/40',
    TENANT_ADMIN: 'bg-indigo-500/20 text-indigo-300 border-indigo-500/40'
  };
  const scopeBadgeClass = scopeColors[payload.active_scope] || 'bg-slate-700 text-slate-300 border-slate-600';

  // Format Roles and Permissions
  const rolesHtml = (payload.roles || [])
    .map((r) => `<span class="px-1.5 py-0.5 rounded bg-slate-800 text-slate-300 border border-white/10 text-[10px] font-mono">${r}</span>`)
    .join(' ') || '<span class="text-slate-500 text-[11px]">Nenhuma</span>';

  const permissionsHtml = (payload.permissions || [])
    .map((p) => `<span class="px-1.5 py-0.5 rounded bg-slate-800 text-slate-300 border border-white/10 text-[10px] font-mono">${p}</span>`)
    .join(' ') || '<span class="text-slate-500 text-[11px]">Nenhuma</span>';

  // Expiration Progress & Countdown
  const nowSec = Math.floor(Date.now() / 1000);
  const expSec = payload.exp || 0;
  const iatSec = payload.iat || nowSec - 300;
  const totalDuration = Math.max(expSec - iatSec, 1);
  const remainingSec = Math.max(expSec - nowSec, 0);
  const percentRemaining = Math.min(Math.max(Math.round((remainingSec / totalDuration) * 100), 0), 100);
  const isExpiringSoon = remainingSec < 60;
  const isExpired = remainingSec <= 0;

  container.innerHTML = `
    <!-- 1. Header (RS256) -->
    <div class="mb-3 p-2.5 rounded-lg bg-slate-900/80 border border-white/10 font-mono text-[11px]">
      <div class="flex items-center justify-between text-slate-400 mb-1 border-b border-white/5 pb-1">
        <span class="text-[10px] uppercase tracking-wider font-semibold">1. Header (Criptografia)</span>
        <span class="text-indigo-400">${header.alg || 'RS256'}</span>
      </div>
      <div class="grid grid-cols-3 gap-1 text-[11px]">
        <div><span class="text-slate-500">alg:</span> <span class="text-emerald-400 font-semibold">${header.alg}</span></div>
        <div><span class="text-slate-500">typ:</span> <span class="text-indigo-300">${header.typ}</span></div>
        <div><span class="text-slate-500">kid:</span> <span class="text-cyan-300">${header.kid || '-'}</span></div>
      </div>
    </div>

    <!-- 2. Cryptographic Signature Badge -->
    <div class="mb-3 p-2.5 rounded-lg border flex items-center justify-between ${
      cryptoRes.valid
        ? 'bg-emerald-950/20 border-emerald-500/40 text-emerald-300 shadow-glow-emerald'
        : 'bg-red-950/20 border-red-500/40 text-red-300 shadow-glow-coral'
    }">
      <div class="flex items-center gap-2">
        <span class="text-base">${cryptoRes.valid ? '✓' : '✕'}</span>
        <div>
          <p class="text-xs font-semibold">${cryptoRes.valid ? 'Assinatura RS256 Verificada' : 'Assinatura Inválida'}</p>
          <p class="text-[10px] opacity-80">${cryptoRes.reason}</p>
        </div>
      </div>
      <span class="text-[10px] font-mono px-2 py-0.5 rounded bg-black/30 border border-white/10">WebCrypto API</span>
    </div>

    <!-- 3. Expiration Countdown -->
    <div class="mb-3 p-2.5 rounded-lg bg-slate-900/80 border border-white/10">
      <div class="flex items-center justify-between text-[11px] mb-1 font-mono">
        <span class="text-slate-400">Validade do Token:</span>
        <span id="countdown-text" class="font-semibold ${isExpired ? 'text-red-400' : isExpiringSoon ? 'text-amber-400 animate-pulse' : 'text-emerald-400'}">
          ${isExpired ? 'EXPIRADO' : formatTime(remainingSec) + ' restantes'}
        </span>
      </div>
      <div class="w-full bg-slate-800 rounded-full h-1.5 overflow-hidden">
        <div id="countdown-bar" class="h-full rounded-full transition-all duration-1000 ${
          isExpired ? 'bg-red-500' : isExpiringSoon ? 'bg-amber-400' : 'bg-emerald-500'
        }" style="width: ${percentRemaining}%"></div>
      </div>
    </div>

    <!-- 4. Payload Claims -->
    <div class="p-3 rounded-lg bg-slate-900/80 border border-white/10 text-xs">
      <div class="flex items-center justify-between text-slate-400 mb-2 border-b border-white/5 pb-1">
        <span class="text-[10px] uppercase tracking-wider font-semibold">2. Payload Claims B2B</span>
        <button id="btn-copy-token" class="text-[10px] font-mono text-indigo-400 hover:text-indigo-300">Copiar Token</button>
      </div>

      <div class="space-y-2">
        <div>
          <span class="text-[10px] text-slate-500 font-mono block">sub (Subject UUID):</span>
          <div class="flex items-center justify-between font-mono text-[11px] bg-slate-950/60 px-2 py-1 rounded border border-white/5">
            <span class="text-slate-300 truncate">${payload.sub || '-'}</span>
            <button class="text-[10px] text-indigo-400 hover:text-indigo-300 ml-2" onclick="navigator.clipboard.writeText('${payload.sub}')">Copiar</button>
          </div>
        </div>

        <div class="grid grid-cols-2 gap-2">
          <div>
            <span class="text-[10px] text-slate-500 font-mono block">active_scope:</span>
            <span class="inline-block mt-0.5 px-2 py-0.5 rounded text-[10px] font-mono border ${scopeBadgeClass}">
              ${payload.active_scope || 'NENHUM (tempToken)'}
            </span>
          </div>
          <div>
            <span class="text-[10px] text-slate-500 font-mono block">view_mode:</span>
            <span class="inline-block mt-0.5 px-2 py-0.5 rounded text-[10px] font-mono bg-slate-800 border border-white/10 text-slate-300">
              ${payload.view_mode || 'NONE'}
            </span>
          </div>
        </div>

        <div>
          <span class="text-[10px] text-slate-500 font-mono block">tenant_id:</span>
          <p class="font-mono text-[11px] text-slate-300 bg-slate-950/60 px-2 py-1 rounded border border-white/5 truncate">
            ${payload.tenant_id || '<span class="text-slate-500">Não contextualizado</span>'}
          </p>
        </div>

        <div>
          <span class="text-[10px] text-slate-500 font-mono block mb-1">roles:</span>
          <div class="flex flex-wrap gap-1">${rolesHtml}</div>
        </div>

        <div>
          <span class="text-[10px] text-slate-500 font-mono block mb-1">permissions:</span>
          <div class="flex flex-wrap gap-1 max-h-24 overflow-y-auto">${permissionsHtml}</div>
        </div>
      </div>
    </div>
  `;

  // Start real-time countdown interval
  if (state.tokenTimerInterval) clearInterval(state.tokenTimerInterval);
  state.tokenTimerInterval = setInterval(() => {
    const curNow = Math.floor(Date.now() / 1000);
    const left = Math.max(expSec - curNow, 0);
    const textEl = document.getElementById('countdown-text');
    const barEl = document.getElementById('countdown-bar');

    if (textEl && barEl) {
      if (left <= 0) {
        textEl.textContent = 'EXPIRADO';
        textEl.className = 'font-semibold text-red-400';
        barEl.style.width = '0%';
        barEl.className = 'h-full rounded-full bg-red-500';
      } else {
        textEl.textContent = formatTime(left) + ' restantes';
        const p = Math.min(Math.max(Math.round((left / totalDuration) * 100), 0), 100);
        barEl.style.width = `${p}%`;
        if (left < 60) {
          textEl.className = 'font-semibold text-amber-400 animate-pulse';
          barEl.className = 'h-full rounded-full bg-amber-400';
        }
      }
    }
  }, 1000);

  // Copy token button
  const copyBtn = document.getElementById('btn-copy-token');
  if (copyBtn) {
    copyBtn.addEventListener('click', () => {
      navigator.clipboard.writeText(token);
      showToast('Token JWT copiado para a área de transferência', 'info');
    });
  }
}

// --- CONSOLE & ENDPOINT SELECTOR ---
function setupEndpointSelector() {
  const select = document.getElementById('endpoint-select');
  if (!select) return;

  const categories = {};
  ENDPOINT_CATALOG.forEach((ep) => {
    if (!categories[ep.category]) categories[ep.category] = [];
    categories[ep.category].push(ep);
  });

  select.innerHTML = Object.entries(categories)
    .map(
      ([cat, endpoints]) => `
      <optgroup label="${cat}">
        ${endpoints
          .map(
            (ep) => `
          <option value="${ep.id}">[${ep.method}] ${ep.name}</option>
        `
          )
          .join('')}
      </optgroup>
    `
    )
    .join('');

  select.addEventListener('change', (e) => {
    const ep = getEndpointById(e.target.value);
    if (ep) selectEndpoint(ep);
  });
}

function selectEndpoint(ep) {
  state.selectedEndpoint = ep;

  const methodSelect = document.getElementById('request-method');
  const urlInput = document.getElementById('request-url');
  const bodyEditor = document.getElementById('request-body');

  if (methodSelect) {
    methodSelect.value = ep.method;
    updateMethodBadgeColor(ep.method);
  }
  if (urlInput) {
    urlInput.value = ep.path;
  }
  if (bodyEditor) {
    if (ep.body) {
      bodyEditor.value = JSON.stringify(ep.body, null, 2);
    } else {
      bodyEditor.value = '';
    }
  }

  // Update Headers
  updateRequestHeadersUI();
}

function updateMethodBadgeColor(method) {
  const badge = document.getElementById('request-method');
  if (!badge) return;

  const colors = {
    GET: 'bg-cyan-500/20 text-cyan-400 border-cyan-500/40',
    POST: 'bg-amber-500/20 text-amber-400 border-amber-500/40',
    PUT: 'bg-purple-500/20 text-purple-400 border-purple-500/40',
    PATCH: 'bg-indigo-500/20 text-indigo-400 border-indigo-500/40',
    DELETE: 'bg-red-500/20 text-red-400 border-red-500/40'
  };

  badge.className = `text-xs font-mono font-bold px-2.5 py-1 rounded border ${colors[method] || 'bg-slate-800 text-slate-300'}`;
}

function updateRequestHeadersUI() {
  const container = document.getElementById('headers-container');
  if (!container) return;

  const token = state.activeToken;
  const isAuthRequired = state.selectedEndpoint?.authRequired;

  container.innerHTML = `
    <div class="space-y-2 text-xs font-mono">
      <div class="flex items-center gap-2">
        <input type="text" value="Content-Type" class="bg-slate-900 border border-white/10 rounded px-2 py-1 w-1/3 text-slate-300" readonly>
        <input type="text" id="header-content-type" value="application/json" class="bg-slate-900 border border-white/10 rounded px-2 py-1 flex-1 text-slate-200">
      </div>
      <div class="flex items-center gap-2">
        <input type="text" value="Authorization" class="bg-slate-900 border border-white/10 rounded px-2 py-1 w-1/3 text-slate-300" readonly>
        <input type="text" id="header-auth" value="${token ? 'Bearer ' + token : ''}" placeholder="${isAuthRequired ? 'Bearer <token> (Obrigatório)' : 'Opcional'}" class="bg-slate-900 border border-white/10 rounded px-2 py-1 flex-1 text-slate-200">
      </div>
      <div class="flex items-center gap-2">
        <input type="text" value="X-Correlation-Id" class="bg-slate-900 border border-white/10 rounded px-2 py-1 w-1/3 text-slate-300" readonly>
        <input type="text" id="header-correlation" value="${generateCorrelationId()}" class="bg-slate-900 border border-white/10 rounded px-2 py-1 flex-1 text-slate-200">
      </div>
    </div>
  `;
}

// --- EXECUTE CONSOLE REQUEST ---
async function handleExecuteRequest() {
  const method = document.getElementById('request-method')?.value || 'GET';
  const path = document.getElementById('request-url')?.value || '';
  const bodyText = document.getElementById('request-body')?.value;

  const authHeader = document.getElementById('header-auth')?.value;
  const contentType = document.getElementById('header-content-type')?.value;
  const correlationId = document.getElementById('header-correlation')?.value;

  const headers = {};
  if (contentType) headers['Content-Type'] = contentType;
  if (authHeader) headers['Authorization'] = authHeader;
  if (correlationId) headers['X-Correlation-Id'] = correlationId;

  let body = null;
  if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(method) && bodyText && bodyText.trim().length > 0) {
    try {
      body = JSON.parse(bodyText);
    } catch (_) {
      body = bodyText;
    }
  }

  showToast('Enviando requisição...', 'info');

  const res = await executeRequest({
    method,
    path,
    headers,
    body
  });

  renderResponse(res);

  // If request was login or switch-context, update session state automatically!
  if (res.status === 200 && res.data?.access_token) {
    if (path.includes('/login')) {
      state.tempToken = res.data.access_token;
      state.refreshToken = res.data.refresh_token;
      state.activeToken = res.data.access_token;
      state.availableContexts = res.data.available_contexts || [];
      saveSessionToStorage();
      updateSessionWidget();
      renderContextSwitcher();
      await updateJwtInspector(state.activeToken);
      showToast('Login efetuado com sucesso!', 'success');
    } else if (path.includes('/switch-context')) {
      state.tempToken = null;
      state.bearerToken = res.data.access_token;
      state.activeToken = res.data.access_token;
      if (res.data.refresh_token) {
        state.refreshToken = res.data.refresh_token;
      }
      saveSessionToStorage();
      updateSessionWidget();
      renderContextSwitcher();
      await updateJwtInspector(state.activeToken);
      showToast('Contexto atualizado!', 'success');
    }
  }
}

// --- RENDER CONSOLE RESPONSE ---
function renderResponse(res) {
  const statusBadge = document.getElementById('resp-status');
  const latencyBadge = document.getElementById('resp-latency');
  const sizeBadge = document.getElementById('resp-size');
  const corrBadge = document.getElementById('resp-correlation');
  const jsonViewer = document.getElementById('resp-json');
  const headersViewer = document.getElementById('resp-headers');
  const rfcSection = document.getElementById('rfc-inspector');

  // Status Badge
  if (statusBadge) {
    const isSuccess = res.status >= 200 && res.status < 300;
    const isClientErr = res.status >= 400 && res.status < 500;
    const isServerErr = res.status >= 500;

    let badgeClass = 'bg-slate-800 text-slate-300 border-slate-700';
    if (isSuccess) badgeClass = 'bg-emerald-500/20 text-emerald-400 border-emerald-500/40 shadow-glow-emerald';
    else if (isClientErr) badgeClass = 'bg-red-500/20 text-red-400 border-red-500/40 shadow-glow-coral';
    else if (isServerErr) badgeClass = 'bg-amber-500/20 text-amber-400 border-amber-500/40';

    statusBadge.className = `px-2 py-0.5 rounded border text-xs font-mono font-bold ${badgeClass}`;
    statusBadge.textContent = `${res.status} ${res.statusText || ''}`;
  }

  // Latency Badge
  if (latencyBadge) {
    let latClass = 'text-emerald-400';
    if (res.latencyMs > 300) latClass = 'text-red-400';
    else if (res.latencyMs > 100) latClass = 'text-amber-400';

    latencyBadge.textContent = `${res.latencyMs}ms`;
    latencyBadge.className = `text-xs font-mono ${latClass}`;
  }

  // Size Badge
  if (sizeBadge) {
    const bytes = new Blob([res.rawText || '']).size;
    const sizeStr = bytes > 1024 ? `${(bytes / 1024).toFixed(2)} KB` : `${bytes} B`;
    sizeBadge.textContent = sizeStr;
  }

  // Correlation ID Badge
  if (corrBadge) {
    corrBadge.innerHTML = `
      <span class="text-slate-500 text-[10px]">CID:</span>
      <span class="text-indigo-300 font-mono text-[11px] truncate max-w-[130px] inline-block align-middle">${res.correlationId}</span>
      <button class="text-[10px] text-slate-400 hover:text-white ml-1" onclick="navigator.clipboard.writeText('${res.correlationId}')" title="Copiar Correlation ID">📋</button>
    `;
  }

  // JSON Viewer
  if (jsonViewer) {
    if (res.data) {
      jsonViewer.innerHTML = syntaxHighlightJson(res.data);
    } else {
      jsonViewer.textContent = res.rawText || '(Sem corpo de resposta)';
    }
  }

  // Headers Viewer
  if (headersViewer) {
    headersViewer.innerHTML = Object.entries(res.headers || {})
      .map(
        ([k, v]) => `
      <div class="flex items-start justify-between py-1 border-b border-white/5 font-mono text-xs">
        <span class="text-indigo-400">${k}:</span>
        <span class="text-slate-300 break-all ml-2">${v}</span>
      </div>
    `
      )
      .join('');
  }

  // RFC 9457 Inspector
  if (rfcSection) {
    if (res.isProblemDetails && res.problemDetails) {
      rfcSection.classList.remove('hidden');
      const pd = res.problemDetails;
      rfcSection.innerHTML = `
        <div class="p-3.5 rounded-lg bg-red-950/30 border border-red-500/40 text-xs shadow-glow-coral">
          <div class="flex items-center justify-between mb-2">
            <span class="px-2 py-0.5 rounded bg-red-500/20 text-red-300 border border-red-500/30 font-mono font-bold text-[11px]">
              ${pd.code || 'RFC 9457 PROBLEM DETAILS'}
            </span>
            <span class="text-[10px] font-mono text-red-400">HTTP ${pd.status || res.status}</span>
          </div>
          <h4 class="font-bold text-red-200 text-sm mb-1">${pd.title || 'Erro na Requisição'}</h4>
          <p class="text-slate-300 mb-2 leading-relaxed">${pd.detail || 'Nenhum detalhe adicional fornecido pelo servidor.'}</p>
          <div class="flex flex-wrap items-center gap-2 pt-2 border-t border-red-500/20 text-[10px] font-mono text-slate-400">
            <span>Type: <a href="${pd.type || '#'}" target="_blank" class="text-indigo-400 hover:underline">${pd.type || 'N/A'}</a></span>
            <span>•</span>
            <span>Correlation ID: <span class="text-slate-300">${pd.correlation_id || res.correlationId}</span></span>
          </div>
        </div>
      `;

      // Auto-switch to RFC tab or highlight
      const rfcTabBtn = document.getElementById('tab-btn-rfc');
      if (rfcTabBtn) rfcTabBtn.classList.remove('hidden');
    } else {
      rfcSection.classList.add('hidden');
      const rfcTabBtn = document.getElementById('tab-btn-rfc');
      if (rfcTabBtn) rfcTabBtn.classList.add('hidden');
    }
  }
}

// --- SESSION STORAGE ---
function saveSessionToStorage() {
  try {
    sessionStorage.setItem(
      'id_session',
      JSON.stringify({
        tempToken: state.tempToken,
        bearerToken: state.bearerToken,
        refreshToken: state.refreshToken,
        activePersonaId: state.activePersonaId,
        availableContexts: state.availableContexts,
        selectedTenantId: state.selectedTenantId,
        selectedScope: state.selectedScope,
        selectedViewMode: state.selectedViewMode
      })
    );
  } catch (_) {}
}

function loadSessionFromStorage() {
  try {
    const raw = sessionStorage.getItem('id_session');
    if (raw) {
      const data = JSON.parse(raw);
      state.tempToken = data.tempToken;
      state.bearerToken = data.bearerToken;
      state.refreshToken = data.refreshToken;
      state.activePersonaId = data.activePersonaId;
      state.availableContexts = data.availableContexts || [];
      state.selectedTenantId = data.selectedTenantId;
      state.selectedScope = data.selectedScope || 'TENANT_ADMIN';
      state.selectedViewMode = data.selectedViewMode || 'SINGLE';
      state.activeToken = data.bearerToken || data.tempToken;

      updateSessionWidget();
      renderContextSwitcher();
      if (state.activeToken) {
        updateJwtInspector(state.activeToken);
      }
    }
  } catch (_) {}
}

function clearSession() {
  state.tempToken = null;
  state.bearerToken = null;
  state.refreshToken = null;
  state.activeToken = null;
  state.activePersonaId = null;
  state.availableContexts = [];
  state.selectedTenantId = null;

  try {
    sessionStorage.removeItem('id_session');
  } catch (_) {}

  renderPersonas();
  updateSessionWidget();
  renderContextSwitcher();
  updateJwtInspector(null);
  showToast('Sessão encerrada e dados limpos.', 'info');
}

function updateSessionWidget() {
  const tokenPill = document.getElementById('session-token-pill');
  const userPill = document.getElementById('session-user-pill');
  if (!tokenPill || !userPill) return;

  if (state.activeToken) {
    const isContextualized = !!state.bearerToken;
    tokenPill.innerHTML = `
      <span class="w-2 h-2 rounded-full ${isContextualized ? 'bg-emerald-400' : 'bg-cyan-400'}"></span>
      <span>${isContextualized ? 'Token Contextualizado' : 'Token Temporário'}</span>
    `;
    tokenPill.className = `px-2 py-0.5 rounded text-[10px] font-mono border flex items-center gap-1.5 ${
      isContextualized ? 'bg-emerald-500/20 text-emerald-300 border-emerald-500/30' : 'bg-cyan-500/20 text-cyan-300 border-cyan-500/30'
    }`;
  } else {
    tokenPill.innerHTML = '<span class="w-1.5 h-1.5 rounded-full bg-slate-500"></span><span>Sem Token</span>';
    tokenPill.className = 'px-2 py-0.5 rounded text-[10px] font-mono border bg-slate-800 text-slate-400 border-white/10';
  }

  const p = getPersonaById(state.activePersonaId);
  userPill.textContent = p ? `${p.icon} ${p.name}` : 'Nenhuma Persona';
}

// --- TEST SUITE RUNNER (J-08) ---
let activeRunner = null;

function setupRunner() {
  const btnRun = document.getElementById('btn-run-tests');
  if (!btnRun) return;

  btnRun.addEventListener('click', async () => {
    if (activeRunner && activeRunner.isRunning) {
      showToast('Suíte de testes já em execução...', 'warn');
      return;
    }

    const modal = document.getElementById('test-runner-modal');
    if (modal) modal.classList.remove('hidden');

    activeRunner = new TestSuiteRunner({
      onStepUpdate: (idx, step, all) => {
        renderRunnerProgress(idx, step, all);
      },
      onComplete: (summary) => {
        renderRunnerComplete(summary);
      }
    });

    await activeRunner.run();
  });
}

function renderRunnerProgress(currentIdx, step, allSteps) {
  const container = document.getElementById('runner-steps-container');
  const progressBar = document.getElementById('runner-progress-bar');
  const percentText = document.getElementById('runner-percent');

  if (!container) return;

  const total = allSteps.length;
  const completed = allSteps.filter((s) => s.status === 'PASS' || s.status === 'FAIL').length;
  const percent = Math.round((completed / total) * 100);

  if (progressBar) progressBar.style.width = `${percent}%`;
  if (percentText) percentText.textContent = `${percent}%`;

  container.innerHTML = allSteps
    .map((s) => {
      let statusBadge = '<span class="text-slate-500 font-mono text-[10px]">PENDING</span>';
      if (s.status === 'RUNNING') {
        statusBadge = '<span class="text-indigo-400 font-mono text-[10px] animate-pulse">● RUNNING</span>';
      } else if (s.status === 'PASS') {
        statusBadge = '<span class="px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-400 border border-emerald-500/30 font-mono font-bold text-[10px]">PASS</span>';
      } else if (s.status === 'FAIL') {
        statusBadge = '<span class="px-1.5 py-0.5 rounded bg-red-500/20 text-red-400 border border-red-500/30 font-mono font-bold text-[10px]">FAIL</span>';
      }

      return `
        <div class="p-2.5 rounded-lg border ${
          s.status === 'RUNNING' ? 'bg-indigo-950/20 border-indigo-500' : 'bg-slate-900/60 border-white/5'
        } mb-2">
          <div class="flex items-center justify-between text-xs mb-1">
            <div class="flex items-center gap-2">
              <span class="font-mono font-bold text-slate-300">${s.code}</span>
              <span class="font-medium text-slate-200">${s.title}</span>
            </div>
            <div class="flex items-center gap-2">
              ${s.latencyMs ? `<span class="text-[10px] font-mono text-slate-400">${s.latencyMs}ms</span>` : ''}
              ${statusBadge}
            </div>
          </div>
          <p class="text-[11px] text-slate-400 font-mono truncate">${s.endpoint}</p>
          ${s.details ? `<p class="text-[11px] text-slate-300 mt-1 pl-2 border-l-2 border-indigo-500/50">${s.details}</p>` : ''}
        </div>
      `;
    })
    .join('');
}

function renderRunnerComplete(summary) {
  const footer = document.getElementById('runner-footer');
  if (!footer) return;

  footer.innerHTML = `
    <div class="flex items-center justify-between w-full pt-2">
      <div class="text-xs">
        <span class="font-bold ${summary.allPassed ? 'text-emerald-400' : 'text-amber-400'}">
          ${summary.allPassed ? '✓ 100% CONFORME' : 'CONCLUÍDO COM RESSALVAS'}
        </span>
        <span class="text-slate-400 ml-2">(${summary.passedCount}/${summary.totalCount} aprovados em ${summary.totalTimeMs}ms)</span>
      </div>
      <div class="flex items-center gap-2">
        <button id="btn-export-report" class="px-3 py-1.5 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 border border-white/10 text-xs font-mono">
          Exportar Relatório Markdown
        </button>
        <button onclick="document.getElementById('test-runner-modal').classList.add('hidden')" class="px-3 py-1.5 rounded bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-medium">
          Fechar
        </button>
      </div>
    </div>
  `;

  document.getElementById('btn-export-report')?.addEventListener('click', () => {
    exportTestReport(summary);
  });
}

function exportTestReport(summary) {
  const md = [
    `# Relatório de Execução de Suíte de Testes (J-08)`,
    `**Serviço:** Identity-Service B2B Multi-Tenant`,
    `**Ambiente:** ${state.env.name} (${state.env.baseUrl})`,
    `**Data:** ${new Date().toISOString()}`,
    `**Tempo Total:** ${summary.totalTimeMs}ms`,
    `**Resultado:** ${summary.passedCount}/${summary.totalCount} (${summary.allPassed ? 'PASS 100%' : 'FAIL'})`,
    '',
    `## Resultados Detalhados por Etapa:`,
    '',
    ...summary.results.map(
      (r) =>
        `- **[${r.status}] ${r.code} — ${r.title}** (${r.latencyMs}ms | ${r.httpStatus})\n  *Endpoint:* \`${r.endpoint}\`\n  *Detalhes:* ${r.details}`
    )
  ].join('\n');

  const blob = new Blob([md], { type: 'text/markdown' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `identity-service-test-report-${Date.now()}.md`;
  a.click();
  URL.revokeObjectURL(url);
}

// --- CHAOS & SECURITY PLAYGROUND ---
function setupChaosPlayground() {
  const logEl = document.getElementById('chaos-log');
  function appendLog(msg, type = 'info') {
    if (!logEl) return;
    const time = new Date().toLocaleTimeString();
    const colors = {
      info: 'text-slate-300',
      success: 'text-emerald-400 font-semibold',
      warn: 'text-amber-400',
      error: 'text-red-400 font-semibold'
    };
    const line = document.createElement('div');
    line.className = `text-[11px] font-mono py-0.5 ${colors[type] || 'text-slate-300'}`;
    line.innerHTML = `<span class="text-slate-500">[${time}]</span> ${msg}`;
    logEl.appendChild(line);
    logEl.scrollTop = logEl.scrollHeight;
  }

  document.getElementById('btn-chaos-revoked')?.addEventListener('click', async () => {
    await runChaosRevokedToken(state.activeToken, appendLog);
  });

  document.getElementById('btn-chaos-refresh-reuse')?.addEventListener('click', async () => {
    await runChaosRefreshTokenReuse(state.refreshToken, appendLog);
  });

  document.getElementById('btn-chaos-tamper')?.addEventListener('click', async () => {
    await runChaosTamperedSignature(state.activeToken, appendLog);
  });

  document.getElementById('btn-chaos-bruteforce')?.addEventListener('click', async () => {
    await runChaosBruteForce(appendLog);
  });

  document.getElementById('btn-chaos-clear-log')?.addEventListener('click', () => {
    if (logEl) logEl.innerHTML = '';
  });
}

// --- EVENT LISTENERS & MODALS ---
function setupEventListeners() {
  // Execute Request Button
  document.getElementById('btn-execute-request')?.addEventListener('click', handleExecuteRequest);

  // Copy cURL Button
  document.getElementById('btn-copy-curl')?.addEventListener('click', () => {
    const method = document.getElementById('request-method')?.value || 'GET';
    const path = document.getElementById('request-url')?.value || '';
    const bodyText = document.getElementById('request-body')?.value;
    const fullUrl = `${state.env.baseUrl}${path.startsWith('/') ? '' : '/'}${path}`;

    const headers = {};
    const ct = document.getElementById('header-content-type')?.value;
    const auth = document.getElementById('header-auth')?.value;
    const corr = document.getElementById('header-correlation')?.value;

    if (ct) headers['Content-Type'] = ct;
    if (auth) headers['Authorization'] = auth;
    if (corr) headers['X-Correlation-Id'] = corr;

    let body = null;
    if (bodyText && bodyText.trim().length > 0) {
      try {
        body = JSON.parse(bodyText);
      } catch (_) {
        body = bodyText;
      }
    }

    const curl = generateCurl({ method, url: fullUrl, headers, body });
    navigator.clipboard.writeText(curl);
    showToast('Comando cURL copiado com sucesso!', 'info');
  });

  // Format JSON Button
  document.getElementById('btn-format-json')?.addEventListener('click', () => {
    const bodyEditor = document.getElementById('request-body');
    if (bodyEditor && bodyEditor.value) {
      try {
        const parsed = JSON.parse(bodyEditor.value);
        bodyEditor.value = JSON.stringify(parsed, null, 2);
        showToast('JSON formatado.', 'info');
      } catch (e) {
        showToast('JSON malformado.', 'error');
      }
    }
  });

  // Clear Session Button
  document.getElementById('btn-clear-session')?.addEventListener('click', clearSession);

  // Request Tab Switching
  document.querySelectorAll('.req-tab-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.req-tab-btn').forEach((b) => b.classList.remove('active', 'border-indigo-500', 'text-indigo-400'));
      btn.classList.add('active', 'border-indigo-500', 'text-indigo-400');
      const target = btn.dataset.tab;
      document.querySelectorAll('.req-tab-content').forEach((c) => c.classList.add('hidden'));
      document.getElementById(`tab-content-${target}`)?.classList.remove('hidden');
    });
  });

  // Response Tab Switching
  document.querySelectorAll('.resp-tab-btn').forEach((btn) => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.resp-tab-btn').forEach((b) => b.classList.remove('active', 'border-indigo-500', 'text-indigo-400'));
      btn.classList.add('active', 'border-indigo-500', 'text-indigo-400');
      const target = btn.dataset.tab;
      document.querySelectorAll('.resp-tab-content').forEach((c) => c.classList.add('hidden'));
      document.getElementById(`tab-content-resp-${target}`)?.classList.remove('hidden');
    });
  });

  // JWKS Modal Open / Close
  const jwksModal = document.getElementById('jwks-modal');
  document.getElementById('btn-open-jwks')?.addEventListener('click', async () => {
    if (!state.jwks) await loadJwks();
    renderJwksModal();
    if (jwksModal) jwksModal.classList.remove('hidden');
  });

  document.getElementById('btn-close-jwks')?.addEventListener('click', () => {
    if (jwksModal) jwksModal.classList.add('hidden');
  });

  // Close Modals on Backdrop Click
  window.addEventListener('click', (e) => {
    if (e.target === jwksModal) jwksModal.classList.add('hidden');
    const runnerModal = document.getElementById('test-runner-modal');
    if (e.target === runnerModal) runnerModal.classList.add('hidden');
  });

  setupRunner();
  setupChaosPlayground();
}

function renderJwksModal() {
  const container = document.getElementById('jwks-modal-content');
  if (!container || !state.jwks) return;

  const key = state.jwks.keys?.[0] || {};
  const pem = jwkToPemPreview(key);

  container.innerHTML = `
    <div class="space-y-3 text-xs font-mono">
      <div class="grid grid-cols-2 gap-2 p-3 bg-slate-900 rounded-lg border border-white/10">
        <div><span class="text-slate-500">Key ID (kid):</span> <span class="text-cyan-400 font-bold">${key.kid || '-'}</span></div>
        <div><span class="text-slate-500">Algorithm (alg):</span> <span class="text-emerald-400 font-bold">${key.alg || 'RS256'}</span></div>
        <div><span class="text-slate-500">Key Type (kty):</span> <span class="text-indigo-400 font-bold">${key.kty || 'RSA'}</span></div>
        <div><span class="text-slate-500">Exponent (e):</span> <span class="text-amber-400 font-bold">${key.e || 'AQAB'} (65537)</span></div>
      </div>

      <div>
        <div class="flex items-center justify-between mb-1">
          <span class="text-slate-400 text-[11px]">RSA Modulus (n) — Base64URL:</span>
          <button class="text-indigo-400 hover:text-indigo-300 text-[10px]" onclick="navigator.clipboard.writeText('${key.n}')">Copiar n</button>
        </div>
        <div class="p-2 bg-slate-950 rounded border border-white/5 text-[10px] text-slate-300 break-all max-h-24 overflow-y-auto">
          ${key.n}
        </div>
      </div>

      <div>
        <div class="flex items-center justify-between mb-1">
          <span class="text-slate-400 text-[11px]">PEM Preview:</span>
        </div>
        <pre class="p-2 bg-slate-950 rounded border border-white/5 text-[10px] text-emerald-400">${pem}</pre>
      </div>

      <div>
        <div class="flex items-center justify-between mb-1">
          <span class="text-slate-400 text-[11px]">JSON Web Key Set Completo:</span>
          <button class="text-indigo-400 hover:text-indigo-300 text-[10px]" onclick="navigator.clipboard.writeText(JSON.stringify(state.jwks, null, 2))">Copiar JSON</button>
        </div>
        <pre class="p-2.5 bg-slate-950 rounded border border-white/5 text-[10px] text-slate-300 overflow-x-auto max-h-36">${JSON.stringify(state.jwks, null, 2)}</pre>
      </div>
    </div>
  `;
}

// --- KEYBOARD SHORTCUTS ---
function setupKeyboardShortcuts() {
  window.addEventListener('keydown', (e) => {
    // Ctrl + Enter or Cmd + Enter -> Execute Request
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
      e.preventDefault();
      handleExecuteRequest();
      return;
    }

    // Ctrl + Shift + C -> Copy cURL
    if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === 'C' || e.key === 'c')) {
      e.preventDefault();
      document.getElementById('btn-copy-curl')?.click();
      return;
    }

    // Alt + 1..4 -> Quick Personas
    if (e.altKey && ['1', '2', '3', '4'].includes(e.key)) {
      e.preventDefault();
      const idx = parseInt(e.key, 10) - 1;
      const persona = PERSONAS[idx];
      if (persona) {
        executePersonaLogin(persona);
      }
      return;
    }

    // Esc -> Close modals
    if (e.key === 'Escape') {
      document.getElementById('jwks-modal')?.classList.add('hidden');
      document.getElementById('test-runner-modal')?.classList.add('hidden');
    }
  });
}

// --- UTILITIES ---
function formatTime(seconds) {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

function formatCnpj(cnpj) {
  if (!cnpj) return '-';
  const clean = cnpj.replace(/\D/g, '');
  if (clean.length === 14) {
    return clean.replace(/(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})/, '$1.$2.$3/$4-$5');
  }
  return cnpj;
}

function syntaxHighlightJson(json) {
  if (typeof json !== 'string') {
    json = JSON.stringify(json, null, 2);
  }
  json = json.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  return json.replace(
    /("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g,
    function (match) {
      let cls = 'json-number';
      if (/^"/.test(match)) {
        if (/:$/.test(match)) {
          cls = 'json-key';
        } else {
          cls = 'json-string';
        }
      } else if (/true|false/.test(match)) {
        cls = 'json-boolean';
      } else if (/null/.test(match)) {
        cls = 'json-null';
      }
      return '<span class="' + cls + '">' + match + '</span>';
    }
  );
}

function showToast(msg, type = 'info') {
  const toastContainer = document.getElementById('toast-container');
  if (!toastContainer) return;

  const toast = document.createElement('div');
  const colors = {
    info: 'bg-slate-800 text-slate-100 border-white/20',
    success: 'bg-emerald-950 text-emerald-200 border-emerald-500/40 shadow-glow-emerald',
    warn: 'bg-amber-950 text-amber-200 border-amber-500/40',
    error: 'bg-red-950 text-red-200 border-red-500/40 shadow-glow-coral'
  };

  toast.className = `p-3 rounded-lg border text-xs shadow-lg flex items-center gap-2 transform transition-all duration-300 translate-y-2 opacity-0 font-medium ${colors[type] || colors.info}`;
  toast.innerHTML = `<span>${type === 'success' ? '✓' : type === 'error' ? '✕' : 'ℹ'}</span><span>${msg}</span>`;

  toastContainer.appendChild(toast);

  requestAnimationFrame(() => {
    toast.classList.remove('translate-y-2', 'opacity-0');
  });

  setTimeout(() => {
    toast.classList.add('opacity-0', 'translate-y-2');
    setTimeout(() => toast.remove(), 300);
  }, 3500);
}

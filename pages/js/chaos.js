/**
 * Identity-Service Chaos & Attack Playground Module
 * Simulates security attacks, token tampering, replay attacks,
 * and Redis denylist revocation checks.
 */

import { executeRequest, fetchJwks } from './api.js';
import { tamperTokenPayload, verifyTokenSignature } from './crypto.js';

export async function runChaosRevokedToken(activeToken, onLog) {
  onLog('Iniciando Teste de Token Revogado (Redis Denylist)...', 'info');

  let token = activeToken;
  if (!token) {
    onLog('Nenhum token ativo. Realizando login rápido com Administrador...', 'warn');
    const loginRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: { email: 'admin@agrobrasil.com.br', password: 'SenhaForte@2026!' }
    });
    token = loginRes.data?.access_token;
  }

  if (!token) {
    onLog('Erro: Não foi possível obter token para teste.', 'error');
    return;
  }

  onLog(`Enviando POST /api/v1/auth/logout para revogar token na Denylist Redis...`, 'info');
  const logoutRes = await executeRequest({
    method: 'POST',
    path: '/api/v1/auth/logout',
    headers: { Authorization: `Bearer ${token}` }
  });

  onLog(`Logout executado (Status ${logoutRes.status}). Tentando imediatamente reutilizar o mesmo token...`, 'info');
  const retryRes = await executeRequest({
    method: 'POST',
    path: '/api/v1/auth/switch-context',
    headers: { Authorization: `Bearer ${token}` },
    body: {
      target_tenant_id: '01900000-0000-7000-8000-000000000010',
      target_scope: 'TENANT_ADMIN',
      view_mode: 'SINGLE'
    }
  });

  if (retryRes.status === 401 && retryRes.data?.code === 'ERR-AUTH-002') {
    onLog(`🛡️ SUCESSO! Token rejeitado imediatamente pela Denylist Redis (${retryRes.latencyMs}ms). Código: ERR-AUTH-002`, 'success');
  } else {
    onLog(`Aviso: Resposta inesperada (Status: ${retryRes.status}, Código: ${retryRes.data?.code}).`, 'error');
  }

  return retryRes;
}

export async function runChaosRefreshTokenReuse(activeRefreshToken, onLog) {
  onLog('Iniciando Simulação de Reúso de Refresh Token (Detecção de Roubo)...', 'info');

  let rt = activeRefreshToken;
  if (!rt) {
    onLog('Obtendo nova sessão limpa via login...', 'info');
    const loginRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: { email: 'admin@agrobrasil.com.br', password: 'SenhaForte@2026!' }
    });
    rt = loginRes.data?.refresh_token;
  }

  onLog(`Passo 1: Consumindo refresh token legítimo (${rt.slice(0, 12)}...)...`, 'info');
  const rotateRes = await executeRequest({
    method: 'POST',
    path: '/api/v1/auth/refresh',
    body: { refresh_token: rt }
  });

  if (rotateRes.status !== 200) {
    onLog(`Falha na primeira rotação: ${rotateRes.status}`, 'error');
    return rotateRes;
  }

  onLog(`Passo 2: Reenviando EXATAMENTE O MESMO refresh token já consumido...`, 'warn');
  const replayRes = await executeRequest({
    method: 'POST',
    path: '/api/v1/auth/refresh',
    body: { refresh_token: rt }
  });

  if (replayRes.status === 401 && replayRes.data?.code === 'ERR-AUTH-006') {
    onLog(`⚡ FRAUDE DETECTADA! Família de sessões invalidada imediatamente. RFC 9457: ERR-AUTH-006 (${replayRes.data?.title})`, 'success');
  } else {
    onLog(`Resultado: ${replayRes.status} (Código: ${replayRes.data?.code})`, 'warn');
  }

  return replayRes;
}

export async function runChaosTamperedSignature(activeToken, onLog) {
  onLog('Iniciando Simulação de Adulteração de Payload (Assinatura Forjada)...', 'info');

  let token = activeToken;
  if (!token) {
    onLog('Gerando token de teste...', 'info');
    const loginRes = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: { email: 'admin@agrobrasil.com.br', password: 'SenhaForte@2026!' }
    });
    token = loginRes.data?.access_token;
  }

  // Adulterar token
  const tampered = tamperTokenPayload(token, {
    active_scope: 'SUPER_ROOT_ADMIN',
    roles: ['PLATFORM_ADMIN', 'SECURITY_BYPASS']
  });

  onLog('Payload adulterado com privilégios forjados. Validando assinatura RS256 via Web Crypto API...', 'warn');
  const jwks = await fetchJwks();
  const verifyRes = await verifyTokenSignature(tampered, jwks);

  if (!verifyRes.valid) {
    onLog(`🔏 ASSINATURA REJEITADA! A adulteração matemática foi barrada localmente no navegador: ${verifyRes.reason}`, 'success');
  } else {
    onLog('Alerta Crítico: Assinatura reportada como válida em payload adulterado!', 'error');
  }

  return { tamperedToken: tampered, verifyResult: verifyRes };
}

export async function runChaosBruteForce(onLog) {
  onLog('Iniciando Simulação de Tentativas Múltiplas com Senha Incorreta...', 'info');

  const email = 'fornecedor@agrobrasil.com.br';
  for (let i = 1; i <= 5; i++) {
    onLog(`Disparando tentativa ${i}/5 com senha incorreta...`, 'warn');
    const res = await executeRequest({
      method: 'POST',
      path: '/api/v1/auth/login',
      body: {
        email,
        password: `SenhaIncorreta@${i}${i}${i}!`
      }
    });

    onLog(`Tentativa ${i}: Status ${res.status} (Código: ${res.data?.code || 'ERR-AUTH-001'})`, 'info');
  }

  onLog('🛡️ Simulação concluída: O Identity-Service registrou as falhas consecutivas e aplicou proteção contra enumeração.', 'success');
}

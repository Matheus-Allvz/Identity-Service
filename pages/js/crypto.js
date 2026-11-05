/**
 * Identity-Service Cryptography & Web Crypto API Module
 * Client-side RS256 verification against /.well-known/jwks.json,
 * Base64URL parsing, and JWT decomposition.
 */

export function base64UrlDecode(str) {
  if (!str) return '';
  let base64 = str.replace(/-/g, '+').replace(/_/g, '/');
  while (base64.length % 4) {
    base64 += '=';
  }
  try {
    const binary = atob(base64);
    const bytes = Uint8Array.from(binary, (m) => m.codePointAt(0));
    return new TextDecoder().decode(bytes);
  } catch (e) {
    console.error('Base64URL decode error:', e);
    return atob(base64);
  }
}

export function base64UrlToUint8(str) {
  let base64 = str.replace(/-/g, '+').replace(/_/g, '/');
  while (base64.length % 4) {
    base64 += '=';
  }
  const binary = atob(base64);
  const len = binary.length;
  const bytes = new Uint8Array(len);
  for (let i = 0; i < len; i++) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
}

export function parseJwt(token) {
  if (!token || typeof token !== 'string') return null;
  const parts = token.trim().split('.');
  if (parts.length !== 3) return null;

  try {
    const headerJson = base64UrlDecode(parts[0]);
    const payloadJson = base64UrlDecode(parts[1]);

    const header = JSON.parse(headerJson);
    const payload = JSON.parse(payloadJson);

    return {
      raw: token,
      rawHeader: parts[0],
      rawPayload: parts[1],
      rawSignature: parts[2],
      header,
      payload
    };
  } catch (e) {
    console.error('Failed to parse JWT parts:', e);
    return null;
  }
}

/**
 * Imports an RSA public JWK into a CryptoKey for RSASSA-PKCS1-v1_5 verification
 */
export async function importRsaPublicKey(jwkKey) {
  if (!window.crypto?.subtle) {
    throw new Error('Web Crypto API não suportada neste navegador.');
  }

  const keyData = {
    kty: jwkKey.kty || 'RSA',
    n: jwkKey.n,
    e: jwkKey.e,
    alg: 'RS256',
    ext: true
  };

  return await window.crypto.subtle.importKey(
    'jwk',
    keyData,
    {
      name: 'RSASSA-PKCS1-v1_5',
      hash: { name: 'SHA-256' }
    },
    true,
    ['verify']
  );
}

/**
 * Verifies RS256 token signature against JWKS in the browser
 */
export async function verifyTokenSignature(token, jwks) {
  if (!token || !jwks || !jwks.keys || jwks.keys.length === 0) {
    return { valid: false, reason: 'JWKS ou token indisponível' };
  }

  const parsed = parseJwt(token);
  if (!parsed) {
    return { valid: false, reason: 'Estrutura JWT malformada' };
  }

  const kid = parsed.header.kid;
  const keyMatch = jwks.keys.find((k) => k.kid === kid) || jwks.keys[0];

  if (!keyMatch) {
    return { valid: false, reason: `Chave com kid ${kid} não encontrada no JWKS` };
  }

  try {
    const cryptoKey = await importRsaPublicKey(keyMatch);
    const sigBytes = base64UrlToUint8(parsed.rawSignature);
    const dataBytes = new TextEncoder().encode(`${parsed.rawHeader}.${parsed.rawPayload}`);

    const isValid = await window.crypto.subtle.verify(
      'RSASSA-PKCS1-v1_5',
      cryptoKey,
      sigBytes,
      dataBytes
    );

    return {
      valid: isValid,
      kid: keyMatch.kid,
      alg: keyMatch.alg,
      reason: isValid ? 'Assinatura matemática válida' : 'Assinatura rejeitada'
    };
  } catch (err) {
    return { valid: false, reason: `Erro WebCrypto: ${err.message}` };
  }
}

/**
 * Creates a tampered token (modifies payload but keeps original signature)
 */
export function tamperTokenPayload(token, patchClaims = { active_scope: 'PLATFORM_SUPERUSER', roles: ['GOD_MODE'] }) {
  const parsed = parseJwt(token);
  if (!parsed) return token;

  const tamperedPayload = { ...parsed.payload, ...patchClaims };
  const encodedPayload = btoa(JSON.stringify(tamperedPayload))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');

  return `${parsed.rawHeader}.${encodedPayload}.${parsed.rawSignature}`;
}

/**
 * Convert exponent and modulus to human-readable PEM
 */
export function jwkToPemPreview(jwk) {
  if (!jwk || !jwk.n) return '';
  return `-----BEGIN PUBLIC KEY-----\n${jwk.n.slice(0, 64)}\n${jwk.n.slice(64, 128)}\n...\n-----END PUBLIC KEY-----`;
}

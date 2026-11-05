/**
 * Identity-Service Quick Personas Catalog
 * Pre-configured corporate seed personas for 1-click authentication and testing.
 */

export const PERSONAS = [
  {
    id: 'admin',
    name: 'Administrador',
    icon: '👑',
    email: 'admin@agrobrasil.com.br',
    password: 'SenhaForte@2026!',
    roleTitle: 'GROUP_MANAGER (Holding)',
    company: 'AgroBrasil Matriz (11.222.333/0001-81)',
    scopes: ['TENANT_ADMIN', 'BUYER', 'SUPPLIER'],
    badge: 'Holding Owner',
    badgeClass: 'bg-indigo-500/20 text-indigo-300 border-indigo-500/40',
    description: 'Gestor geral do grupo econômico. Acesso irrestrito a filiais, gestão de tenants e usuários.'
  },
  {
    id: 'fornecedor',
    name: 'Fornecedor',
    icon: '🌾',
    email: 'fornecedor@agrobrasil.com.br',
    password: 'SenhaForte@2026!',
    roleTitle: 'SUPPLIER_SALES (Vendas)',
    company: 'AgroBrasil Matriz (11.222.333/0001-81)',
    scopes: ['SUPPLIER'],
    badge: 'Fornecedor',
    badgeClass: 'bg-emerald-500/20 text-emerald-300 border-emerald-500/40',
    description: 'Operador comercial para fornecimento de grãos, insumos e negociações com compradores.'
  },
  {
    id: 'comprador',
    name: 'Comprador',
    icon: '🛒',
    email: 'comprador@agrobrasil.com.br',
    password: 'SenhaForte@2026!',
    roleTitle: 'BUYER_OPERATOR (Compras)',
    company: 'Matriz e Filial Sul (Multi-Tenant)',
    scopes: ['BUYER'],
    badge: 'Multi-Tenant',
    badgeClass: 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40',
    description: 'Comprador multi-filial com vínculos cruzados. Ideal para testar switch-context entre tenants.'
  },
  {
    id: 'transportador',
    name: 'Transportador',
    icon: '🚚',
    email: 'transportador@agrobrasil.com.br',
    password: 'SenhaForte@2026!',
    roleTitle: 'CARRIER_DISPATCHER (Logística)',
    company: 'AgroLog Transportes (77.888.999/0001-03)',
    scopes: ['CARRIER'],
    badge: 'Logística',
    badgeClass: 'bg-amber-500/20 text-amber-300 border-amber-500/40',
    description: 'Despachante logístico para emissão de fretes, aceite de cargas e rotas de entrega.'
  }
];

export function getPersonaById(id) {
  return PERSONAS.find((p) => p.id === id);
}

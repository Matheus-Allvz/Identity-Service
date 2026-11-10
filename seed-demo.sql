-- Seed de Demonstração - B2B Identity Service
-- Grupo Econômico: AgroBrasil Participações S.A.
-- Data de Criação: 2026-11-04

SET app.auth_flow = 'true';

-- 1. Grupo Econômico
INSERT INTO economic_group (id, name, status, created_at, updated_at)
VALUES (
    '01900000-0000-7000-8000-000000000001',
    'AgroBrasil Participações S.A.',
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z'
) ON CONFLICT (id) DO UPDATE SET
    name = EXCLUDED.name,
    status = EXCLUDED.status,
    updated_at = NOW();

-- 2. Tenants
-- 2.1 Matriz (Fornecedor e Comprador)
INSERT INTO tenant (id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at)
VALUES (
    '01900000-0000-7000-8000-000000000010',
    '01900000-0000-7000-8000-000000000001',
    '11222333000181',
    'AgroBrasil Matriz e Exportadora S.A.',
    'HQ',
    -15.794229,
    -47.882166,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z'
) ON CONFLICT (cnpj) DO UPDATE SET
    legal_name = EXCLUDED.legal_name,
    status = EXCLUDED.status,
    updated_at = NOW();

-- 2.2 Filial (Comprador de Insumos)
INSERT INTO tenant (id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at)
VALUES (
    '01900000-0000-7000-8000-000000000020',
    '01900000-0000-7000-8000-000000000001',
    '44555666000192',
    'Cooperativa AgroBrasil Sul',
    'BRANCH',
    -25.428400,
    -49.273300,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z'
) ON CONFLICT (cnpj) DO UPDATE SET
    legal_name = EXCLUDED.legal_name,
    status = EXCLUDED.status,
    updated_at = NOW();

-- 2.3 Transportadora
INSERT INTO tenant (id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at)
VALUES (
    '01900000-0000-7000-8000-000000000030',
    '01900000-0000-7000-8000-000000000001',
    '77888999000103',
    'AgroLog Transportes e Cargas Ltda',
    'HQ',
    -23.550520,
    -46.633308,
    'ACTIVE',
    '2026-01-01T00:00:00Z',
    '2026-01-01T00:00:00Z'
) ON CONFLICT (cnpj) DO UPDATE SET
    legal_name = EXCLUDED.legal_name,
    status = EXCLUDED.status,
    updated_at = NOW();

-- 3. Tenant Scopes
-- 3.1 Matriz Scopes (BUYER, SUPPLIER, TENANT_ADMIN)
INSERT INTO tenant_scope (id, tenant_id, scope, is_active, created_at)
VALUES
    ('01900000-0000-7000-8000-000000000011', '01900000-0000-7000-8000-000000000010', 'BUYER', true, '2026-01-01T00:00:00Z'),
    ('01900000-0000-7000-8000-000000000012', '01900000-0000-7000-8000-000000000010', 'SUPPLIER', true, '2026-01-01T00:00:00Z'),
    ('01900000-0000-7000-8000-000000000013', '01900000-0000-7000-8000-000000000010', 'TENANT_ADMIN', true, '2026-01-01T00:00:00Z')
ON CONFLICT (tenant_id, scope) DO NOTHING;

-- 3.2 Filial Scope (BUYER)
INSERT INTO tenant_scope (id, tenant_id, scope, is_active, created_at)
VALUES
    ('01900000-0000-7000-8000-000000000021', '01900000-0000-7000-8000-000000000020', 'BUYER', true, '2026-01-01T00:00:00Z')
ON CONFLICT (tenant_id, scope) DO NOTHING;

-- 3.3 Transportadora Scope (CARRIER)
INSERT INTO tenant_scope (id, tenant_id, scope, is_active, created_at)
VALUES
    ('01900000-0000-7000-8000-000000000031', '01900000-0000-7000-8000-000000000030', 'CARRIER', true, '2026-01-01T00:00:00Z')
ON CONFLICT (tenant_id, scope) DO NOTHING;

-- 4. Usuários (Senha Padrão: SenhaForte@2026!)
-- Argon2id Hash validado com Konscious.Security.Cryptography:
-- $argon2id$v=19$m=19456,t=2,p=1$7m2IQEyIfIzFPA0zM1SDWg==$7Z8NHKBD22ZikghTgYpEIHOR2kfHNYnJTr6wniv5InM=
INSERT INTO user_account (id, email, password_hash, name, status, platform_role, failed_login_attempts, created_at, updated_at)
VALUES
    ('01900000-0000-7000-8000-000000000101', 'admin@agrobrasil.com.br', '$argon2id$v=19$m=19456,t=2,p=1$7m2IQEyIfIzFPA0zM1SDWg==$7Z8NHKBD22ZikghTgYpEIHOR2kfHNYnJTr6wniv5InM=', 'Administrador AgroBrasil', 'ACTIVE', 'NONE', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    ('01900000-0000-7000-8000-000000000102', 'fornecedor@agrobrasil.com.br', '$argon2id$v=19$m=19456,t=2,p=1$7m2IQEyIfIzFPA0zM1SDWg==$7Z8NHKBD22ZikghTgYpEIHOR2kfHNYnJTr6wniv5InM=', 'Vendedor AgroBrasil Matriz', 'ACTIVE', 'NONE', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    ('01900000-0000-7000-8000-000000000103', 'comprador@agrobrasil.com.br', '$argon2id$v=19$m=19456,t=2,p=1$7m2IQEyIfIzFPA0zM1SDWg==$7Z8NHKBD22ZikghTgYpEIHOR2kfHNYnJTr6wniv5InM=', 'Comprador AgroBrasil Grupo', 'ACTIVE', 'NONE', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    ('01900000-0000-7000-8000-000000000104', 'transportador@agrobrasil.com.br', '$argon2id$v=19$m=19456,t=2,p=1$7m2IQEyIfIzFPA0zM1SDWg==$7Z8NHKBD22ZikghTgYpEIHOR2kfHNYnJTr6wniv5InM=', 'Despachante AgroLog', 'ACTIVE', 'NONE', 0, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
ON CONFLICT (email) DO UPDATE SET
    password_hash = EXCLUDED.password_hash,
    name = EXCLUDED.name,
    status = EXCLUDED.status,
    failed_login_attempts = 0,
    locked_until = NULL,
    updated_at = NOW();

-- 5. Tenant Members
INSERT INTO tenant_member (id, tenant_id, user_id, is_owner, can_view_group, status, created_at, updated_at)
VALUES
    -- Admin na Matriz (GROUP_MANAGER)
    ('01900000-0000-7000-8000-000000000201', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000101', true, true, 'ACTIVE', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    -- Fornecedor na Matriz (SUPPLIER_SALES)
    ('01900000-0000-7000-8000-000000000202', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000102', false, false, 'ACTIVE', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    -- Comprador na Matriz (BUYER_OPERATOR)
    ('01900000-0000-7000-8000-000000000203', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000103', false, true, 'ACTIVE', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    -- Comprador na Filial (BUYER_OPERATOR)
    ('01900000-0000-7000-8000-000000000204', '01900000-0000-7000-8000-000000000020', '01900000-0000-7000-8000-000000000103', false, true, 'ACTIVE', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    -- Transportador na Transportadora (CARRIER_DISPATCHER)
    ('01900000-0000-7000-8000-000000000205', '01900000-0000-7000-8000-000000000030', '01900000-0000-7000-8000-000000000104', false, false, 'ACTIVE', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
ON CONFLICT (tenant_id, user_id) DO UPDATE SET
    is_owner = EXCLUDED.is_owner,
    can_view_group = EXCLUDED.can_view_group,
    status = EXCLUDED.status,
    updated_at = NOW();

-- 6. Member Roles (Associando Membros aos Roles Pré-Configurados)
-- Roles existentes no banco:
-- 01900000-0000-7000-8000-000000000001 = BUYER_OPERATOR
-- 01900000-0000-7000-8000-000000000002 = SUPPLIER_SALES
-- 01900000-0000-7000-8000-000000000003 = CARRIER_DISPATCHER
-- 01900000-0000-7000-8000-000000000004 = GROUP_MANAGER
INSERT INTO member_role (id, tenant_id, tenant_member_id, role_id, created_at)
VALUES
    -- Admin -> GROUP_MANAGER em Matriz
    ('01900000-0000-7000-8000-000000000301', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000201', '01900000-0000-7000-8000-000000000004', '2026-01-01T00:00:00Z'),
    -- Fornecedor -> SUPPLIER_SALES em Matriz
    ('01900000-0000-7000-8000-000000000302', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000202', '01900000-0000-7000-8000-000000000002', '2026-01-01T00:00:00Z'),
    -- Comprador -> BUYER_OPERATOR em Matriz
    ('01900000-0000-7000-8000-000000000303', '01900000-0000-7000-8000-000000000010', '01900000-0000-7000-8000-000000000203', '01900000-0000-7000-8000-000000000001', '2026-01-01T00:00:00Z'),
    -- Comprador -> BUYER_OPERATOR em Filial
    ('01900000-0000-7000-8000-000000000304', '01900000-0000-7000-8000-000000000020', '01900000-0000-7000-8000-000000000204', '01900000-0000-7000-8000-000000000001', '2026-01-01T00:00:00Z'),
    -- Transportador -> CARRIER_DISPATCHER em Transportadora
    ('01900000-0000-7000-8000-000000000305', '01900000-0000-7000-8000-000000000030', '01900000-0000-7000-8000-000000000205', '01900000-0000-7000-8000-000000000003', '2026-01-01T00:00:00Z')
ON CONFLICT (tenant_member_id, role_id) DO NOTHING;

-- 7. Afinação de Permissões das Personas de Demonstração (D-ECO-11)
-- Garante que BUYER_OPERATOR receba a permissão produtos:criar para cadastro de commodities
UPDATE role
SET permissions = array_cat(permissions, '{produtos:criar}'::text[])
WHERE name = 'BUYER_OPERATOR' AND NOT (permissions @> '{produtos:criar}');

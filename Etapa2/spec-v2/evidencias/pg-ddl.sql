-- DDL de referência de 30 §3, §5, §6 e testes de rejeição (EXT-01 n.º 6 a 9). Executar: medir-postgres.sh
\set ON_ERROR_STOP 1
CREATE TABLE economic_group(
  id uuid CONSTRAINT pk_economic_group PRIMARY KEY,
  name varchar(160) NOT NULL CONSTRAINT ck_economic_group_name CHECK (char_length(btrim(name)) >= 2),
  status varchar(16) NOT NULL DEFAULT 'ACTIVE' CONSTRAINT ck_economic_group_status CHECK (status IN ('ACTIVE','SUSPENDED')),
  created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL);
CREATE TABLE tenant(
  id uuid CONSTRAINT pk_tenant PRIMARY KEY,
  group_id uuid NOT NULL CONSTRAINT fk_tenant_group_id REFERENCES economic_group(id) ON DELETE RESTRICT,
  cnpj char(14) NOT NULL CONSTRAINT ck_tenant_cnpj CHECK (cnpj ~ '^[0-9]{14}$'),
  legal_name varchar(200) NOT NULL CONSTRAINT ck_tenant_legal_name CHECK (char_length(btrim(legal_name)) >= 2),
  branch_type varchar(16) NOT NULL CONSTRAINT ck_tenant_branch_type CHECK (branch_type IN ('HQ','BRANCH')),
  geo_lat numeric(9,6) NOT NULL CONSTRAINT ck_tenant_geo_lat CHECK (geo_lat BETWEEN -90 AND 90),
  geo_lng numeric(9,6) NOT NULL CONSTRAINT ck_tenant_geo_lng CHECK (geo_lng BETWEEN -180 AND 180),
  status varchar(16) NOT NULL DEFAULT 'ACTIVE' CONSTRAINT ck_tenant_status CHECK (status IN ('ACTIVE','SUSPENDED')),
  created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
  CONSTRAINT uq_tenant_cnpj UNIQUE (cnpj));
CREATE INDEX ix_tenant_group_id ON tenant(group_id);
CREATE TABLE tenant_scope(
  id uuid CONSTRAINT pk_tenant_scope PRIMARY KEY,
  tenant_id uuid NOT NULL CONSTRAINT fk_tenant_scope_tenant_id REFERENCES tenant(id) ON DELETE RESTRICT,
  scope varchar(16) NOT NULL CONSTRAINT ck_tenant_scope_scope CHECK (scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')),
  is_active boolean NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL,
  CONSTRAINT uq_tenant_scope_tenant_id_scope UNIQUE (tenant_id, scope));
CREATE TABLE user_account(
  id uuid CONSTRAINT pk_user_account PRIMARY KEY,
  email varchar(320) NOT NULL CONSTRAINT ck_user_account_email CHECK (email = lower(btrim(email)) AND char_length(email) >= 5),
  password_hash varchar(255) NOT NULL CONSTRAINT ck_user_account_password_hash CHECK (starts_with(password_hash, '$argon2id$v=19$m=19456,t=2,p=1$')),
  name varchar(160) NOT NULL CONSTRAINT ck_user_account_name CHECK (char_length(btrim(name)) >= 2),
  status varchar(16) NOT NULL DEFAULT 'ACTIVE' CONSTRAINT ck_user_account_status CHECK (status IN ('ACTIVE','LOCKED','SUSPENDED')),
  platform_role varchar(24) NOT NULL DEFAULT 'NONE' CONSTRAINT ck_user_account_platform_role CHECK (platform_role IN ('NONE','SUPPORT_AGENT','PLATFORM_ADMIN')),
  failed_login_attempts integer NOT NULL DEFAULT 0 CONSTRAINT ck_user_account_failed_login_attempts CHECK (failed_login_attempts BETWEEN 0 AND 5),
  locked_until timestamptz NULL,
  created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
  CONSTRAINT ck_user_account_locked_until CHECK ((status = 'LOCKED') = (locked_until IS NOT NULL)),
  CONSTRAINT uq_user_account_email UNIQUE (email));
CREATE TABLE tenant_member(
  id uuid CONSTRAINT pk_tenant_member PRIMARY KEY,
  tenant_id uuid NOT NULL CONSTRAINT fk_tenant_member_tenant_id REFERENCES tenant(id) ON DELETE RESTRICT,
  user_id uuid NOT NULL CONSTRAINT fk_tenant_member_user_id REFERENCES user_account(id) ON DELETE RESTRICT,
  is_owner boolean NOT NULL DEFAULT false,
  can_view_group boolean NOT NULL DEFAULT false,
  status varchar(16) NOT NULL DEFAULT 'ACTIVE' CONSTRAINT ck_tenant_member_status CHECK (status IN ('ACTIVE','SUSPENDED')),
  created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
  CONSTRAINT uq_tenant_member_id_tenant_id UNIQUE (id, tenant_id),
  CONSTRAINT uq_tenant_member_tenant_user UNIQUE (tenant_id, user_id));
CREATE INDEX ix_tenant_member_user_id ON tenant_member(user_id);
CREATE TABLE role(
  id uuid CONSTRAINT pk_role PRIMARY KEY,
  name varchar(64) NOT NULL CONSTRAINT ck_role_name CHECK (name ~ '^[A-Z0-9_]{3,64}$'),
  scope varchar(16) NOT NULL CONSTRAINT ck_role_scope CHECK (scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')),
  permissions text[] NOT NULL CONSTRAINT ck_role_permissions CHECK (
    cardinality(permissions) >= 1
    AND array_to_string(permissions, ',') ~ '^[a-z_]+:[a-z_]+(,[a-z_]+:[a-z_]+)*$'
    AND char_length(array_to_string(permissions, ',')) - char_length(replace(array_to_string(permissions, ','), ',', '')) = cardinality(permissions) - 1),
  created_at timestamptz NOT NULL,
  CONSTRAINT uq_role_name UNIQUE (name));
CREATE INDEX ix_role_scope ON role(scope);
CREATE TABLE member_role(
  id uuid CONSTRAINT pk_member_role PRIMARY KEY,
  tenant_id uuid NOT NULL CONSTRAINT fk_member_role_tenant_id REFERENCES tenant(id) ON DELETE RESTRICT,
  tenant_member_id uuid NOT NULL,
  role_id uuid NOT NULL CONSTRAINT fk_member_role_role_id REFERENCES role(id) ON DELETE RESTRICT,
  created_at timestamptz NOT NULL,
  CONSTRAINT fk_member_role_tenant_member FOREIGN KEY (tenant_member_id, tenant_id) REFERENCES tenant_member(id, tenant_id) ON DELETE RESTRICT,
  CONSTRAINT uq_member_role_member_role UNIQUE (tenant_member_id, role_id));
CREATE INDEX ix_member_role_tenant_id ON member_role(tenant_id);
CREATE TABLE refresh_token(
  id uuid CONSTRAINT pk_refresh_token PRIMARY KEY,
  family_id uuid NOT NULL,
  user_id uuid NOT NULL CONSTRAINT fk_refresh_token_user_id REFERENCES user_account(id) ON DELETE RESTRICT,
  token_hash char(64) NOT NULL CONSTRAINT ck_refresh_token_token_hash CHECK (token_hash ~ '^[0-9a-f]{64}$'),
  status varchar(16) NOT NULL DEFAULT 'ACTIVE' CONSTRAINT ck_refresh_token_status CHECK (status IN ('ACTIVE','CONSUMED','REVOKED')),
  replaced_by_id uuid NULL CONSTRAINT fk_refresh_token_replaced_by_id REFERENCES refresh_token(id) ON DELETE RESTRICT,
  view_mode varchar(16) NOT NULL DEFAULT 'NONE' CONSTRAINT ck_refresh_token_view_mode CHECK (view_mode IN ('NONE','SINGLE','MACRO_GROUP')),
  group_id uuid NULL CONSTRAINT fk_refresh_token_group_id REFERENCES economic_group(id) ON DELETE RESTRICT,
  active_tenant_id uuid NULL CONSTRAINT fk_refresh_token_active_tenant_id REFERENCES tenant(id) ON DELETE RESTRICT,
  allowed_tenants uuid[] NOT NULL DEFAULT '{}',
  active_scope varchar(16) NULL CONSTRAINT ck_refresh_token_active_scope CHECK (active_scope IS NULL OR active_scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')),
  expires_at timestamptz NOT NULL,
  consumed_at timestamptz NULL,
  revoked_at timestamptz NULL,
  created_at timestamptz NOT NULL,
  CONSTRAINT uq_refresh_token_token_hash UNIQUE (token_hash),
  CONSTRAINT ck_refresh_token_expires_at CHECK (expires_at > created_at),
  CONSTRAINT ck_refresh_token_context CHECK (
    (view_mode = 'NONE' AND group_id IS NULL AND active_tenant_id IS NULL AND active_scope IS NULL AND cardinality(allowed_tenants) = 0)
    OR (view_mode = 'SINGLE' AND group_id IS NOT NULL AND active_tenant_id IS NOT NULL AND active_scope IS NOT NULL AND allowed_tenants = ARRAY[active_tenant_id])
    OR (view_mode = 'MACRO_GROUP' AND group_id IS NOT NULL AND active_tenant_id IS NULL AND active_scope IS NOT NULL AND cardinality(allowed_tenants) BETWEEN 1 AND 50)),
  CONSTRAINT ck_refresh_token_status_dates CHECK (
    (status <> 'ACTIVE' OR (consumed_at IS NULL AND revoked_at IS NULL))
    AND (status <> 'CONSUMED' OR (consumed_at IS NOT NULL AND revoked_at IS NULL))
    AND (status <> 'REVOKED' OR revoked_at IS NOT NULL)));
CREATE UNIQUE INDEX uq_refresh_token_family_active ON refresh_token(family_id) WHERE status = 'ACTIVE';
CREATE INDEX ix_refresh_token_family_id ON refresh_token(family_id);
CREATE INDEX ix_refresh_token_user_id_status ON refresh_token(user_id, status);
CREATE INDEX ix_refresh_token_allowed_tenants ON refresh_token USING GIN (allowed_tenants);
CREATE TABLE auth_audit_log(
  id uuid CONSTRAINT pk_auth_audit_log PRIMARY KEY,
  event_type varchar(32) NOT NULL CONSTRAINT ck_auth_audit_log_event_type CHECK (event_type IN ('LOGIN_SUCCESS','LOGIN_FAILED','USER_LOCKED','CONTEXT_SWITCHED','TOKEN_REFRESHED','TOKEN_REUSE_DETECTED','LOGOUT','IMPERSONATION_STARTED','TENANT_CREATED','MEMBER_ADDED','TENANT_STATUS_CHANGED','USER_STATUS_CHANGED')),
  user_id uuid NULL CONSTRAINT fk_auth_audit_log_user_id REFERENCES user_account(id) ON DELETE RESTRICT,
  actor_user_id uuid NULL CONSTRAINT fk_auth_audit_log_actor_user_id REFERENCES user_account(id) ON DELETE RESTRICT,
  tenant_id uuid NULL CONSTRAINT fk_auth_audit_log_tenant_id REFERENCES tenant(id) ON DELETE RESTRICT,
  group_id uuid NULL CONSTRAINT fk_auth_audit_log_group_id REFERENCES economic_group(id) ON DELETE RESTRICT,
  ticket_id varchar(32) NULL CONSTRAINT ck_auth_audit_log_ticket_id CHECK (ticket_id IS NULL OR ticket_id ~ '^[A-Z]{2,10}-[0-9]{1,10}$'),
  ip_address varchar(45) NOT NULL CONSTRAINT ck_auth_audit_log_ip_address CHECK (char_length(ip_address) BETWEEN 2 AND 45),
  correlation_id uuid NOT NULL,
  metadata jsonb NOT NULL DEFAULT '{}' CONSTRAINT ck_auth_audit_log_metadata CHECK (jsonb_typeof(metadata) = 'object'),
  created_at timestamptz NOT NULL);
CREATE INDEX ix_auth_audit_log_user_id_created_at ON auth_audit_log(user_id, created_at);
CREATE INDEX ix_auth_audit_log_tenant_id_created_at ON auth_audit_log(tenant_id, created_at);
CREATE TABLE outbox_message(
  id uuid CONSTRAINT pk_outbox_message PRIMARY KEY,
  topic varchar(64) NOT NULL CONSTRAINT ck_outbox_message_topic CHECK (topic IN ('empresa_cadastrada','tenant_bloqueado','usuario_bloqueado','sessao_comprometida')),
  partition_key varchar(36) NOT NULL CONSTRAINT ck_outbox_message_partition_key CHECK (partition_key ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'),
  headers jsonb NOT NULL CONSTRAINT ck_outbox_message_headers CHECK (jsonb_typeof(headers) = 'object'),
  payload jsonb NOT NULL CONSTRAINT ck_outbox_message_payload CHECK (jsonb_typeof(payload) = 'object'),
  status varchar(16) NOT NULL DEFAULT 'PENDING' CONSTRAINT ck_outbox_message_status CHECK (status IN ('PENDING','PUBLISHED','FAILED')),
  retry_count integer NOT NULL DEFAULT 0 CONSTRAINT ck_outbox_message_retry_count CHECK (retry_count BETWEEN 0 AND 5),
  created_at timestamptz NOT NULL,
  processed_at timestamptz NULL,
  CONSTRAINT ck_outbox_message_processed_at CHECK ((status = 'PENDING') = (processed_at IS NULL)),
  CONSTRAINT ck_outbox_message_failed_retry CHECK ((status = 'FAILED') = (retry_count = 5)));
CREATE INDEX ix_outbox_message_pending ON outbox_message(created_at) WHERE status = 'PENDING';
-- 30 §5
DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identity_app') THEN CREATE ROLE identity_app NOLOGIN; END IF; END $$;
GRANT identity_app TO CURRENT_USER;
GRANT SELECT, INSERT, UPDATE ON economic_group, tenant, tenant_scope, user_account, tenant_member, role, member_role, refresh_token, outbox_message TO identity_app;
GRANT SELECT, INSERT ON auth_audit_log TO identity_app;
ALTER TABLE tenant_scope ENABLE ROW LEVEL SECURITY;  ALTER TABLE tenant_scope FORCE ROW LEVEL SECURITY;
ALTER TABLE tenant_member ENABLE ROW LEVEL SECURITY; ALTER TABLE tenant_member FORCE ROW LEVEL SECURITY;
ALTER TABLE member_role ENABLE ROW LEVEL SECURITY;   ALTER TABLE member_role FORCE ROW LEVEL SECURITY;
CREATE POLICY p_tenant_scope_isolation ON tenant_scope
  USING (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]) OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
CREATE POLICY p_tenant_member_isolation ON tenant_member
  USING (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]) OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
CREATE POLICY p_member_role_isolation ON member_role
  USING (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]) OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
-- 30 §6
INSERT INTO role VALUES
 ('01900000-0000-7000-8000-000000000001','BUYER_OPERATOR','BUYER','{demanda:criar,demanda:listar,leilao:participar,pedido:visualizar}','2026-01-01T00:00:00Z'),
 ('01900000-0000-7000-8000-000000000002','SUPPLIER_SALES','SUPPLIER','{estoque:atualizar,fornecimento:publicar,leilao:lance,pedido:confirmar}','2026-01-01T00:00:00Z'),
 ('01900000-0000-7000-8000-000000000003','CARRIER_DISPATCHER','CARRIER','{frete:cotar,frete:visualizar,transportadora:configurar}','2026-01-01T00:00:00Z'),
 ('01900000-0000-7000-8000-000000000004','GROUP_MANAGER','TENANT_ADMIN','{mapa:visualizar,membro:listar,membro:vincular,tenant:criar,tenant:listar,usuario:suspender}','2026-01-01T00:00:00Z')
 ON CONFLICT (name) DO NOTHING;
-- dados mínimos
INSERT INTO economic_group VALUES ('00000000-0000-0000-0000-000000000001','Grupo','ACTIVE',now(),now());
INSERT INTO tenant VALUES ('00000000-0000-0000-0000-0000000000a1','00000000-0000-0000-0000-000000000001','11222333000181','AgroBrasil','HQ',-16.5,-49.2,'ACTIVE',now(),now());
INSERT INTO tenant VALUES ('00000000-0000-0000-0000-0000000000a2','00000000-0000-0000-0000-000000000001','11444777000161','Filial','BRANCH',0,0,'ACTIVE',now(),now());
INSERT INTO user_account VALUES ('00000000-0000-0000-0000-0000000000c1','a@b.com','$argon2id$v=19$m=19456,t=2,p=1$x$y','Ana','ACTIVE','NONE',0,NULL,now(),now());
INSERT INTO tenant_member VALUES ('00000000-0000-0000-0000-0000000000d1','00000000-0000-0000-0000-0000000000a1','00000000-0000-0000-0000-0000000000c1',false,true,'ACTIVE',now(),now());
INSERT INTO refresh_token(id,family_id,user_id,token_hash,status,view_mode,group_id,active_tenant_id,allowed_tenants,active_scope,expires_at,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000f1','00000000-0000-0000-0000-0000000000c1',repeat('a',64),'ACTIVE','SINGLE','00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-0000000000a1','{00000000-0000-0000-0000-0000000000a1}','BUYER',now()+interval '7 days',now());
\echo M7_DDL_SEED_E_DADOS_OK
CREATE OR REPLACE FUNCTION test_m(label text, stmt text, expected text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE got text; n bigint; err_c text; err_s text;
BEGIN
  BEGIN
    IF stmt ILIKE 'SELECT count%' THEN
      EXECUTE stmt INTO n; got := n::text;
    ELSIF stmt ILIKE 'SELECT %' THEN
      EXECUTE stmt INTO got;
    ELSE
      EXECUTE stmt; GET DIAGNOSTICS n = ROW_COUNT; got := 'ok:' || n;
    END IF;
  EXCEPTION WHEN OTHERS THEN
    GET STACKED DIAGNOSTICS err_c = CONSTRAINT_NAME, err_s = RETURNED_SQLSTATE;
    got := coalesce(nullif(err_c, ''), err_s);
  END;
  RAISE NOTICE '% | % | esperado % | obtido %', CASE WHEN got = expected THEN 'OK' ELSE 'FALHA' END, label, expected, got;
END $$;
GRANT EXECUTE ON FUNCTION test_m TO identity_app;

SELECT test_m('M7a_permissao_fora_do_formato', $q$INSERT INTO role VALUES (gen_random_uuid(),'BAD_ROLE','BUYER','{Bad:x}',now())$q$, 'ck_role_permissions');
SELECT test_m('M7b_permissao_com_virgula', $q$INSERT INTO role VALUES (gen_random_uuid(),'BAD_ROLE2','BUYER','{"a:b,c:d"}',now())$q$, 'ck_role_permissions');
SELECT test_m('M7c_permissao_null', $q$INSERT INTO role VALUES (gen_random_uuid(),'BAD_ROLE3','BUYER','{a:b,NULL}',now())$q$, 'ck_role_permissions');
SELECT test_m('M7d_locked_sem_locked_until', $q$INSERT INTO user_account VALUES ('00000000-0000-0000-0000-0000000000c2','c@b.com','$argon2id$v=19$m=19456,t=2,p=1$x$y','Ana','LOCKED','NONE',5,NULL,now(),now())$q$, 'ck_user_account_locked_until');
SELECT test_m('M7e_single_incoerente', $q$INSERT INTO refresh_token(id,family_id,user_id,token_hash,status,view_mode,group_id,active_tenant_id,allowed_tenants,active_scope,expires_at,created_at) VALUES (gen_random_uuid(),gen_random_uuid(),'00000000-0000-0000-0000-0000000000c1',repeat('b',64),'ACTIVE','SINGLE','00000000-0000-0000-0000-000000000001',NULL,'{}','BUYER',now()+interval '7 days',now())$q$, 'ck_refresh_token_context');
SELECT test_m('M6_segundo_active_na_familia', $q$INSERT INTO refresh_token(id,family_id,user_id,token_hash,status,view_mode,allowed_tenants,expires_at,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000f1','00000000-0000-0000-0000-0000000000c1',repeat('c',64),'ACTIVE','NONE','{}',now()+interval '7 days',now())$q$, 'uq_refresh_token_family_active');
SELECT test_m('M7f_member_role_tenant_trocado', $q$INSERT INTO member_role VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000a2','00000000-0000-0000-0000-0000000000d1','01900000-0000-7000-8000-000000000004',now())$q$, 'fk_member_role_tenant_member');
SELECT test_m('M7g_hash_nao_argon2id', $q$INSERT INTO user_account VALUES ('00000000-0000-0000-0000-0000000000c3','d@b.com','$2a$10$abc','Ana','ACTIVE','NONE',0,NULL,now(),now())$q$, 'ck_user_account_password_hash');
SELECT test_m('M7h_outbox_failed_com_retry_4', $q$INSERT INTO outbox_message VALUES (gen_random_uuid(),'tenant_bloqueado','00000000-0000-0000-0000-0000000000a1','{}','{}','FAILED',4,now(),now())$q$, 'ck_outbox_message_failed_retry');

SET ROLE identity_app;
SELECT set_config('app.auth_flow','false',false), set_config('app.allowed_tenants','00000000-0000-0000-0000-0000000000a1',false), set_config('app.write_tenants','00000000-0000-0000-0000-0000000000a1,00000000-0000-0000-0000-0000000000a2',false);
SELECT test_m('M9a_insert_em_tenant_de_write_tenants', $q$INSERT INTO tenant_scope VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000a2','BUYER',true,now())$q$, 'ok:1');
SELECT set_config('app.write_tenants','00000000-0000-0000-0000-0000000000a1',false);
SELECT test_m('M9b_insert_fora_do_conjunto_de_escrita', $q$INSERT INTO tenant_scope VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000a2','SUPPLIER',true,now())$q$, '42501');
SELECT test_m('M8a_update_auditoria_como_identity_app', $q$UPDATE auth_audit_log SET metadata = '{}'$q$, '42501');
SELECT test_m('M8b_delete_como_identity_app', 'DELETE FROM tenant', '42501');
RESET ROLE;


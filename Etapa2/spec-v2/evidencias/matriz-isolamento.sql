-- Matriz de isolamento (30 §5): cada EP que escreve em tabelas ITenantScoped e cada leitura cruzada,
-- nos modos SINGLE, MACRO_GROUP e bypass, contra a RLS real, com e sem RETURNING.
-- Pré-requisito: pg-ddl.sql na mesma BD. Executar: medir-postgres.sh. Cada linha imprime OK ou FALHA.
\set ON_ERROR_STOP 1
RESET ROLE;
-- Tenants: A1 e A2 do grupo 1; B1 do grupo 2; N (novo, criado em EP-06)
INSERT INTO economic_group VALUES ('00000000-0000-0000-0000-000000000002','Grupo 2','ACTIVE',now(),now());
INSERT INTO tenant VALUES ('00000000-0000-0000-0000-0000000000b1','00000000-0000-0000-0000-000000000002','99000001000101','Tenant B1','HQ',0,0,'ACTIVE',now(),now());
INSERT INTO tenant VALUES ('00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-000000000001','99000002000101','Tenant Novo','BRANCH',0,0,'ACTIVE',now(),now());
INSERT INTO user_account VALUES ('00000000-0000-0000-0000-0000000000c9','u9@example.test','$argon2id$v=19$m=19456,t=2,p=1$x$y','Utilizador Teste 09','ACTIVE','NONE',0,NULL,now(),now());
INSERT INTO tenant_member VALUES ('00000000-0000-0000-0000-0000000000d2','00000000-0000-0000-0000-0000000000a2','00000000-0000-0000-0000-0000000000c1',true,true,'ACTIVE',now(),now());
INSERT INTO tenant_member VALUES ('00000000-0000-0000-0000-0000000000d3','00000000-0000-0000-0000-0000000000b1','00000000-0000-0000-0000-0000000000c9',false,false,'ACTIVE',now(),now());
INSERT INTO tenant_scope VALUES ('00000000-0000-0000-0000-0000000000f1','00000000-0000-0000-0000-0000000000a1','TENANT_ADMIN',true,now());
INSERT INTO tenant_scope VALUES ('00000000-0000-0000-0000-0000000000f2','00000000-0000-0000-0000-0000000000b1','BUYER',true,now());

CREATE FUNCTION pg_temp.ctx(allowed text, write_ text, flow text) RETURNS void LANGUAGE sql AS $$
  SELECT set_config('app.allowed_tenants', allowed, false), set_config('app.write_tenants', write_, false), set_config('app.auth_flow', flow, false); $$;
CREATE FUNCTION pg_temp.t(label text, stmt text, expected text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE n bigint; got text;
BEGIN
  BEGIN
    IF stmt ILIKE 'SELECT count%' THEN EXECUTE stmt INTO n; got := n::text;
    ELSIF stmt ILIKE 'SELECT %' THEN EXECUTE stmt INTO got;
    ELSE EXECUTE stmt; GET DIAGNOSTICS n = ROW_COUNT; got := 'ok:' || n; END IF;
  EXCEPTION WHEN insufficient_privilege THEN got := '42501';
            WHEN unique_violation THEN got := '23505';
  END;
  RAISE NOTICE '% | % | esperado % | obtido %', CASE WHEN got = expected THEN 'OK' ELSE 'FALHA' END, label, expected, got;
END $$;

SET ROLE identity_app;
\set A1 '00000000-0000-0000-0000-0000000000a1'
\set A2 '00000000-0000-0000-0000-0000000000a2'
\set B1 '00000000-0000-0000-0000-0000000000b1'
\set N  '00000000-0000-0000-0000-0000000000e1'

-- Bypass: EP-01 a EP-05 (fluxos de autenticação) e PLATFORM_ADMIN
SELECT pg_temp.ctx('', '', 'true');
SELECT pg_temp.t('I01 bypass: leitura cruzada de tenant_member (todos os grupos)', 'SELECT count(*) FROM tenant_member', '3');
SELECT pg_temp.t('I02 bypass PLATFORM_ADMIN EP-06: tenant_scope em N com RETURNING', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000e1','BUYER',now()) RETURNING is_active$q$, 'ok:1');
SELECT pg_temp.t('I03 bypass PLATFORM_ADMIN EP-07: tenant_member em B1 com RETURNING', $q$INSERT INTO tenant_member(id,tenant_id,user_id,created_at,updated_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','00000000-0000-0000-0000-0000000000c1',now(),now()) RETURNING is_owner, status$q$, 'ok:1');

-- SINGLE em A1 (allowed = write = {A1})
SELECT pg_temp.ctx(:'A1', :'A1', 'false');
SELECT pg_temp.t('I04 SINGLE A1: lê só tenant_member de A1', 'SELECT count(*) FROM tenant_member', '1');
SELECT pg_temp.t('I05 SINGLE A1: tenant_scope de B1 invisível', $q$SELECT count(*) FROM tenant_scope WHERE tenant_id = '00000000-0000-0000-0000-0000000000b1'$q$, '0');
SELECT pg_temp.t('I06 SINGLE A1 EP-07: tenant_member em A1 com RETURNING', $q$INSERT INTO tenant_member(id,tenant_id,user_id,created_at,updated_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000a1','00000000-0000-0000-0000-0000000000c9',now(),now()) RETURNING is_owner$q$, 'ok:1');
SELECT pg_temp.t('I07 SINGLE A1 EP-07: member_role em A1 sem RETURNING', $q$INSERT INTO member_role VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000a1','00000000-0000-0000-0000-0000000000d1','01900000-0000-7000-8000-000000000004',now())$q$, 'ok:1');
SELECT pg_temp.t('I08 SINGLE A1: INSERT em B1 rejeitado (RG-09)', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','CARRIER',now())$q$, '42501');
SELECT pg_temp.t('I09 SINGLE A1: UPDATE em B1 não afeta linhas', $q$UPDATE tenant_scope SET is_active = true WHERE tenant_id = '00000000-0000-0000-0000-0000000000b1'$q$, 'ok:0');
SELECT pg_temp.t('I10 SINGLE A1: UPDATE que move linha para B1 rejeitado', $q$UPDATE tenant_scope SET tenant_id = '00000000-0000-0000-0000-0000000000b1' WHERE tenant_id = '00000000-0000-0000-0000-0000000000a1'$q$, '42501');

-- EP-06 por TENANT_ADMIN em SINGLE A1: conjunto de escrita = {A1, N}
SELECT pg_temp.ctx(:'A1', :'A1' || ',' || :'N', 'false');
SELECT pg_temp.t('I11 EP-06 TENANT_ADMIN: tenant_scope em N com RETURNING', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000e1','TENANT_ADMIN',now()) RETURNING is_active$q$, 'ok:1');
SELECT pg_temp.t('I12 EP-06 TENANT_ADMIN: tenant_scope em N sem RETURNING', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000e1','SUPPLIER',now())$q$, 'ok:1');
SELECT pg_temp.t('I13 EP-06 TENANT_ADMIN: tenant_scope em B1 rejeitado', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','SUPPLIER',now()) RETURNING is_active$q$, '42501');

-- MACRO_GROUP {A1, A2}
SELECT pg_temp.ctx(:'A1' || ',' || :'A2', :'A1' || ',' || :'A2', 'false');
SELECT pg_temp.t('I14 MACRO A1+A2: lê tenant_member de A1 e A2', 'SELECT count(*) FROM tenant_member', '3');
SELECT pg_temp.t('I15 MACRO A1+A2: INSERT em A2 com RETURNING', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000a2','CARRIER',now()) RETURNING is_active$q$, 'ok:1');
SELECT pg_temp.t('I16 MACRO A1+A2: INSERT em B1 rejeitado', $q$INSERT INTO tenant_scope(id,tenant_id,scope,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','BUYER',now())$q$, '42501');

-- EP-08 (M-12, C8): is_owner do chamador no tenant do path, que está em allowed
SELECT pg_temp.ctx(:'A2', :'A2', 'false');
SELECT pg_temp.t('I17 EP-08 C8: tenant_member(A2, chamador) visível', $q$SELECT count(*) FROM tenant_member WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2' AND user_id = '00000000-0000-0000-0000-0000000000c1' AND is_owner$q$, '1');

-- EP-09 (M-10, C9): pertença do alvo a um tenant do grupo (IF-09.7 GetGroupMembershipAsync); sem bypass falha (ve apenas A1 e da MEMBER), com bypass da IF-09.7 ve A2 onde e dono e devolve OWNER
SELECT pg_temp.ctx(:'A1', :'A1', 'false');
SELECT pg_temp.t('I18 EP-09 C9 sem bypass: IF-09.7 devolve MEMBER (alvo dono em A2 invisivel)', $q$SELECT CASE WHEN count(*) = 0 THEN 'NONE' WHEN bool_or(tm.is_owner) THEN 'OWNER' ELSE 'MEMBER' END FROM tenant_member tm JOIN tenant t ON t.id = tm.tenant_id WHERE tm.user_id = '00000000-0000-0000-0000-0000000000c1' AND t.group_id = '00000000-0000-0000-0000-000000000001'$q$, 'MEMBER');
SELECT pg_temp.ctx(:'A1', :'A1', 'true');
SELECT pg_temp.t('I19 EP-09 C9 com IF-09.7 (bypass): alvo dono em A2 devolve OWNER', $q$SELECT CASE WHEN count(*) = 0 THEN 'NONE' WHEN bool_or(tm.is_owner) THEN 'OWNER' ELSE 'MEMBER' END FROM tenant_member tm JOIN tenant t ON t.id = tm.tenant_id WHERE tm.user_id = '00000000-0000-0000-0000-0000000000c1' AND t.group_id = '00000000-0000-0000-0000-000000000001'$q$, 'OWNER');

-- Regressão do achado A-33: a política antiga (USING só com allowed) rejeitava INSERT com RETURNING em N
RESET ROLE;
CREATE TABLE pg_temp.antiga(id int, tenant_id uuid, is_active boolean NOT NULL DEFAULT true);
ALTER TABLE pg_temp.antiga ENABLE ROW LEVEL SECURITY; ALTER TABLE pg_temp.antiga FORCE ROW LEVEL SECURITY;
CREATE POLICY p ON pg_temp.antiga
  USING (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
GRANT SELECT, INSERT ON pg_temp.antiga TO identity_app;
SET ROLE identity_app;
SELECT pg_temp.ctx(:'A1', :'A1' || ',' || :'N', 'false');
SELECT pg_temp.t('I20 política antiga: INSERT em N sem RETURNING', $q$INSERT INTO pg_temp.antiga(id,tenant_id) VALUES (1,'00000000-0000-0000-0000-0000000000e1')$q$, 'ok:1');
SELECT pg_temp.t('I21 política antiga: INSERT em N com RETURNING falha (motivo da correção)', $q$INSERT INTO pg_temp.antiga(id,tenant_id) VALUES (2,'00000000-0000-0000-0000-0000000000e1') RETURNING is_active$q$, '42501');
RESET ROLE;

-- Casos A-60: EP-09 (M-10) e ExecuteWithBypassAsync (IF-11.3)
-- Todos como identity_app com contexto TENANT_ADMIN (allowed = write = [A1], auth_flow = 'false')
SET ROLE identity_app;
SELECT pg_temp.ctx(:'A1', :'A1', 'false');

-- (a) UPDATE tenant_member do alvo em A2 (mesmo grupo) sem bypass -> 0 linhas
SELECT pg_temp.t('I22 EP-09 C9 sem bypass: UPDATE tenant_member em A2 afeta 0 linhas', $q$UPDATE tenant_member SET status = 'SUSPENDED', updated_at = now() WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2' AND user_id = '00000000-0000-0000-0000-0000000000c1'$q$, 'ok:0');

-- (b) set_config auth_flow='true', repetir o UPDATE -> 1 linha; repor 'false' -> o mesmo UPDATE dá 0 e a leitura volta a ver só A1
SELECT set_config('app.auth_flow', 'true', false);
SELECT pg_temp.t('I23 EP-09 C9 sob bypass: UPDATE tenant_member em A2 afeta 1 linha', $q$UPDATE tenant_member SET status = 'SUSPENDED', updated_at = now() WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2' AND user_id = '00000000-0000-0000-0000-0000000000c1'$q$, 'ok:1');
SELECT set_config('app.auth_flow', 'false', false);
SELECT pg_temp.t('I24 EP-09 C9 apos reposicao false: UPDATE tenant_member em A2 afeta 0 linhas', $q$UPDATE tenant_member SET status = 'ACTIVE', updated_at = now() WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2' AND user_id = '00000000-0000-0000-0000-0000000000c1'$q$, 'ok:0');
SELECT pg_temp.t('I25 EP-09 C9 apos reposicao false: leitura ve so tenant_member de A1', $q$SELECT count(*) FROM tenant_member WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2'$q$, '0');

-- (c) BEGIN; ligar 'true'; forçar um erro; reposição na transação abortada falha com 25P02; ROLLBACK -> current_setting('app.auth_flow') = 'false'
\set ON_ERROR_STOP 0
BEGIN;
SELECT set_config('app.auth_flow', 'true', false);
SELECT 1/0;
SELECT set_config('app.auth_flow', 'false', false);
\set abort_sqlstate :SQLSTATE
ROLLBACK;
\set ON_ERROR_STOP 1
SELECT pg_temp.t('I26 ExecuteWithBypassAsync rollback apos erro: auth_flow e false', 'SELECT current_setting(''app.auth_flow'', true)', 'false');

-- (d) mesmo fluxo com reposição explícita ANTES do commit -> 'false', e transação abortada onde reposição falha com 25P02
BEGIN;
SELECT set_config('app.auth_flow', 'true', false);
UPDATE tenant_member SET updated_at = now() WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2' AND user_id = '00000000-0000-0000-0000-0000000000c1';
SELECT set_config('app.auth_flow', 'false', false);
COMMIT;
SELECT pg_temp.t('I27a ExecuteWithBypassAsync reposicao antes do commit: auth_flow e false', 'SELECT current_setting(''app.auth_flow'', true)', 'false');
SELECT pg_temp.t('I27b ExecuteWithBypassAsync transacao abortada: reposicao falha com 25P02', 'SELECT ' || quote_literal(:'abort_sqlstate'), '25P02');

-- (e) ligação/sessão nova sem set_config -> UPDATE cruzado dá 0 linhas
DISCARD ALL;
CREATE FUNCTION pg_temp.t(label text, stmt text, expected text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE n bigint; got text;
BEGIN
  BEGIN
    IF stmt ILIKE 'SELECT count%' THEN EXECUTE stmt INTO n; got := n::text;
    ELSIF stmt ILIKE 'SELECT %' THEN EXECUTE stmt INTO got;
    ELSE EXECUTE stmt; GET DIAGNOSTICS n = ROW_COUNT; got := 'ok:' || n; END IF;
  EXCEPTION WHEN insufficient_privilege THEN got := '42501';
            WHEN unique_violation THEN got := '23505';
  END;
  RAISE NOTICE '% | % | esperado % | obtido %', CASE WHEN got = expected THEN 'OK' ELSE 'FALHA' END, label, expected, got;
END $$;
GRANT EXECUTE ON FUNCTION pg_temp.t TO identity_app;
SET ROLE identity_app;
SELECT pg_temp.t('I28 sessao nova sem set_config: UPDATE cruzado em A2 afeta 0 linhas', $q$UPDATE tenant_member SET updated_at = now() WHERE tenant_id = '00000000-0000-0000-0000-0000000000a2' AND user_id = '00000000-0000-0000-0000-0000000000c1'$q$, 'ok:0');
RESET ROLE;

-- Casos A-102: EP-06 por TENANT_ADMIN vincula criador em N (só no conjunto de escrita) com RETURNING (D-04, IF-12.1)
CREATE FUNCTION pg_temp.ctx(allowed text, write_ text, flow text) RETURNS void LANGUAGE sql AS $$
  SELECT set_config('app.allowed_tenants', allowed, false), set_config('app.write_tenants', write_, false), set_config('app.auth_flow', flow, false); $$;
GRANT EXECUTE ON FUNCTION pg_temp.ctx TO identity_app;
SET ROLE identity_app;
SELECT pg_temp.ctx(:'A1', :'A1' || ',' || :'N', 'false');
SELECT pg_temp.t('I29 EP-06 TENANT_ADMIN: tenant_member em N com RETURNING', $q$INSERT INTO tenant_member(id,tenant_id,user_id,is_owner,can_view_group,status,created_at,updated_at) VALUES ('00000000-0000-0000-0000-0000000000d4','00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000c1',false,false,'ACTIVE',now(),now()) RETURNING is_owner, status$q$, 'ok:1');
SELECT pg_temp.t('I30 EP-06 TENANT_ADMIN: member_role em N com RETURNING', $q$INSERT INTO member_role(id,tenant_id,tenant_member_id,role_id,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000e1','00000000-0000-0000-0000-0000000000d4','01900000-0000-7000-8000-000000000004',now()) RETURNING id$q$, 'ok:1');
SELECT pg_temp.t('I31 EP-06 TENANT_ADMIN: tenant_member em B1 fora de write_tenants rejeitado', $q$INSERT INTO tenant_member(id,tenant_id,user_id,is_owner,can_view_group,status,created_at,updated_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','00000000-0000-0000-0000-0000000000c1',false,false,'ACTIVE',now(),now()) RETURNING is_owner$q$, '42501');
SELECT pg_temp.t('I32 EP-06 TENANT_ADMIN: member_role em B1 fora de write_tenants rejeitado', $q$INSERT INTO member_role(id,tenant_id,tenant_member_id,role_id,created_at) VALUES (gen_random_uuid(),'00000000-0000-0000-0000-0000000000b1','00000000-0000-0000-0000-0000000000d3','01900000-0000-7000-8000-000000000004',now()) RETURNING id$q$, '42501');
RESET ROLE;



-- Medição EXT-01 n.º 1 a 5 (RLS, SET LOCAL, DISCARD ALL). Executar: medir-postgres.sh
SELECT version();
CREATE TABLE t(id int, tenant_id uuid);
INSERT INTO t VALUES (1,'00000000-0000-0000-0000-00000000000a'),(2,'00000000-0000-0000-0000-00000000000b');
ALTER TABLE t ENABLE ROW LEVEL SECURITY;
ALTER TABLE t FORCE ROW LEVEL SECURITY;
CREATE POLICY p ON t
  USING (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true' OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]));
CREATE ROLE identity_app NOLOGIN;
GRANT SELECT, INSERT, UPDATE ON t TO identity_app;
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

SELECT test_m('M1_superutilizador_sem_variaveis', 'SELECT count(*) FROM t', '2');
SET ROLE identity_app;
SELECT test_m('M2a_identity_app_sem_variaveis', 'SELECT count(*) FROM t', '0');
SELECT set_config('app.allowed_tenants','00000000-0000-0000-0000-00000000000a',false), set_config('app.auth_flow','false',false);
SELECT test_m('M2b_allowed_um_tenant', 'SELECT count(*) FROM t', '1');
SELECT set_config('app.allowed_tenants','',false);
SELECT test_m('M2c_allowed_vazio', 'SELECT count(*) FROM t', '0');
SELECT set_config('app.auth_flow','true',false);
SELECT test_m('M2d_auth_flow_true', 'SELECT count(*) FROM t', '2');
SELECT set_config('app.auth_flow','false',false), set_config('app.allowed_tenants','00000000-0000-0000-0000-00000000000a',false);
SELECT test_m('M3_insert_fora_da_lista', $q$INSERT INTO t VALUES (3,'00000000-0000-0000-0000-00000000000b')$q$, '42501');
SELECT test_m('M3b_delete_sem_grant', 'DELETE FROM t', '42501');
RESET ROLE;
SET LOCAL app.x = '1';
SELECT test_m('M4_set_local_fora_de_transacao', 'SELECT coalesce(current_setting(''app.x'', true), '''')', '');
SET ROLE identity_app;
SELECT set_config('app.auth_flow','true',false);
DISCARD ALL;
SELECT test_m('M5_discard_all', 'SELECT current_user || '':'' || coalesce(current_setting(''app.auth_flow'', true), '''')', 'postgres:');


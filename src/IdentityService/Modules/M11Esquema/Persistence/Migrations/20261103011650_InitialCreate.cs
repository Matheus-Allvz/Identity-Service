using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Modules.M11Esquema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "economic_group",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_economic_group", x => x.id);
                    table.CheckConstraint("ck_economic_group_name", "char_length(btrim(name)) >= 2");
                    table.CheckConstraint("ck_economic_group_status", "status IN ('ACTIVE','SUSPENDED')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    partition_key = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    headers = table.Column<string>(type: "jsonb", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "PENDING"),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                    table.CheckConstraint("ck_outbox_message_failed_retry", "(status = 'FAILED') = (retry_count = 5)");
                    table.CheckConstraint("ck_outbox_message_headers", "jsonb_typeof(headers) = 'object'");
                    table.CheckConstraint("ck_outbox_message_partition_key", "partition_key ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'");
                    table.CheckConstraint("ck_outbox_message_payload", "jsonb_typeof(payload) = 'object'");
                    table.CheckConstraint("ck_outbox_message_processed_at", "(status = 'PENDING') = (processed_at IS NULL)");
                    table.CheckConstraint("ck_outbox_message_retry_count", "retry_count BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_outbox_message_status", "status IN ('PENDING','PUBLISHED','FAILED')");
                    table.CheckConstraint("ck_outbox_message_topic", "topic IN ('empresa_cadastrada','tenant_bloqueado','usuario_bloqueado','sessao_comprometida')");
                });

            migrationBuilder.CreateTable(
                name: "role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    permissions = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role", x => x.id);
                    table.CheckConstraint("ck_role_name", "name ~ '^[A-Z0-9_]{3,64}$'");
                    table.CheckConstraint("ck_role_permissions", "cardinality(permissions) >= 1 AND array_to_string(permissions, ',') ~ '^[a-z_]+:[a-z_]+(,[a-z_]+:[a-z_]+)*$' AND char_length(array_to_string(permissions, ',')) - char_length(replace(array_to_string(permissions, ','), ',', '')) = cardinality(permissions) - 1");
                    table.CheckConstraint("ck_role_scope", "scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')");
                });

            migrationBuilder.CreateTable(
                name: "user_account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    platform_role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false, defaultValue: "NONE"),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_account", x => x.id);
                    table.CheckConstraint("ck_user_account_email", "email = lower(btrim(email)) AND char_length(email) >= 5");
                    table.CheckConstraint("ck_user_account_failed_login_attempts", "failed_login_attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_user_account_locked_until", "(status = 'LOCKED') = (locked_until IS NOT NULL)");
                    table.CheckConstraint("ck_user_account_name", "char_length(btrim(name)) >= 2");
                    table.CheckConstraint("ck_user_account_password_hash", "starts_with(password_hash, '$argon2id$v=19$m=19456,t=2,p=1$')");
                    table.CheckConstraint("ck_user_account_platform_role", "platform_role IN ('NONE','SUPPORT_AGENT','PLATFORM_ADMIN')");
                    table.CheckConstraint("ck_user_account_status", "status IN ('ACTIVE','LOCKED','SUSPENDED')");
                });

            migrationBuilder.CreateTable(
                name: "tenant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cnpj = table.Column<string>(type: "char(14)", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    branch_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    geo_lat = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    geo_lng = table.Column<decimal>(type: "numeric(9,6)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant", x => x.id);
                    table.CheckConstraint("ck_tenant_branch_type", "branch_type IN ('HQ','BRANCH')");
                    table.CheckConstraint("ck_tenant_cnpj", "cnpj ~ '^[0-9]{14}$'");
                    table.CheckConstraint("ck_tenant_geo_lat", "geo_lat BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_tenant_geo_lng", "geo_lng BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_tenant_legal_name", "char_length(btrim(legal_name)) >= 2");
                    table.CheckConstraint("ck_tenant_status", "status IN ('ACTIVE','SUSPENDED')");
                    table.ForeignKey(
                        name: "fk_tenant_group_id",
                        column: x => x.group_id,
                        principalTable: "economic_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "auth_audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ticket_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_audit_log", x => x.id);
                    table.CheckConstraint("ck_auth_audit_log_event_type", "event_type IN ('LOGIN_SUCCESS','LOGIN_FAILED','USER_LOCKED','CONTEXT_SWITCHED','TOKEN_REFRESHED','TOKEN_REUSE_DETECTED','LOGOUT','IMPERSONATION_STARTED','TENANT_CREATED','MEMBER_ADDED','TENANT_STATUS_CHANGED','USER_STATUS_CHANGED')");
                    table.CheckConstraint("ck_auth_audit_log_ip_address", "char_length(ip_address) BETWEEN 2 AND 45");
                    table.CheckConstraint("ck_auth_audit_log_metadata", "jsonb_typeof(metadata) = 'object'");
                    table.CheckConstraint("ck_auth_audit_log_ticket_id", "ticket_id IS NULL OR ticket_id ~ '^[A-Z]{2,10}-[0-9]{1,10}$'");
                    table.ForeignKey(
                        name: "fk_auth_audit_log_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_auth_audit_log_group_id",
                        column: x => x.group_id,
                        principalTable: "economic_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_auth_audit_log_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_auth_audit_log_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "char(64)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    view_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "NONE"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active_tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    allowed_tenants = table.Column<Guid[]>(type: "uuid[]", nullable: false, defaultValue: new Guid[0]),
                    active_scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.CheckConstraint("ck_refresh_token_active_scope", "active_scope IS NULL OR active_scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')");
                    table.CheckConstraint("ck_refresh_token_context", "(view_mode = 'NONE' AND group_id IS NULL AND active_tenant_id IS NULL AND active_scope IS NULL AND cardinality(allowed_tenants) = 0) OR (view_mode = 'SINGLE' AND group_id IS NOT NULL AND active_tenant_id IS NOT NULL AND active_scope IS NOT NULL AND allowed_tenants = ARRAY[active_tenant_id]) OR (view_mode = 'MACRO_GROUP' AND group_id IS NOT NULL AND active_tenant_id IS NULL AND active_scope IS NOT NULL AND cardinality(allowed_tenants) BETWEEN 1 AND 50)");
                    table.CheckConstraint("ck_refresh_token_expires_at", "expires_at > created_at");
                    table.CheckConstraint("ck_refresh_token_status", "status IN ('ACTIVE','CONSUMED','REVOKED')");
                    table.CheckConstraint("ck_refresh_token_status_dates", "(status <> 'ACTIVE' OR (consumed_at IS NULL AND revoked_at IS NULL)) AND (status <> 'CONSUMED' OR (consumed_at IS NOT NULL AND revoked_at IS NULL)) AND (status <> 'REVOKED' OR revoked_at IS NOT NULL)");
                    table.CheckConstraint("ck_refresh_token_token_hash", "token_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_refresh_token_view_mode", "view_mode IN ('NONE','SINGLE','MACRO_GROUP')");
                    table.ForeignKey(
                        name: "fk_refresh_token_active_tenant_id",
                        column: x => x.active_tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refresh_token_group_id",
                        column: x => x.group_id,
                        principalTable: "economic_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refresh_token_replaced_by_id",
                        column: x => x.replaced_by_id,
                        principalTable: "refresh_token",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refresh_token_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_member",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_owner = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    can_view_group = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_member", x => x.id);
                    table.UniqueConstraint("ak_tenant_member_id_tenant_id", x => new { x.id, x.tenant_id });
                    table.CheckConstraint("ck_tenant_member_status", "status IN ('ACTIVE','SUSPENDED')");
                    table.ForeignKey(
                        name: "fk_tenant_member_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_member_user_id",
                        column: x => x.user_id,
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_scope",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_scope", x => x.id);
                    table.CheckConstraint("ck_tenant_scope_scope", "scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')");
                    table.ForeignKey(
                        name: "fk_tenant_scope_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "member_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_member_role", x => x.id);
                    table.ForeignKey(
                        name: "fk_member_role_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_member_role_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_member_role_tenant_member",
                        columns: x => new { x.tenant_member_id, x.tenant_id },
                        principalTable: "tenant_member",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auth_audit_log_actor_user_id",
                table: "auth_audit_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_audit_log_group_id",
                table: "auth_audit_log",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_auth_audit_log_tenant_id_created_at",
                table: "auth_audit_log",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_auth_audit_log_user_id_created_at",
                table: "auth_audit_log",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_member_role_role_id",
                table: "member_role",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_role_tenant_id",
                table: "member_role",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_member_role_tenant_member_id_tenant_id",
                table: "member_role",
                columns: new[] { "tenant_member_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "uq_member_role_member_role",
                table: "member_role",
                columns: new[] { "tenant_member_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_pending",
                table: "outbox_message",
                column: "created_at",
                filter: "status = 'PENDING'");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_active_tenant_id",
                table: "refresh_token",
                column: "active_tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_allowed_tenants",
                table: "refresh_token",
                column: "allowed_tenants")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_group_id",
                table: "refresh_token",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_replaced_by_id",
                table: "refresh_token",
                column: "replaced_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user_id_status",
                table: "refresh_token",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_refresh_token_family_active",
                table: "refresh_token",
                column: "family_id",
                unique: true,
                filter: "status = 'ACTIVE'");

            migrationBuilder.CreateIndex(
                name: "uq_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_scope",
                table: "role",
                column: "scope");

            migrationBuilder.CreateIndex(
                name: "uq_role_name",
                table: "role",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_group_id",
                table: "tenant",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "uq_tenant_cnpj",
                table: "tenant",
                column: "cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_member_user_id",
                table: "tenant_member",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_tenant_member_id_tenant_id",
                table: "tenant_member",
                columns: new[] { "id", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_tenant_member_tenant_user",
                table: "tenant_member",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_tenant_scope_tenant_id_scope",
                table: "tenant_scope",
                columns: new[] { "tenant_id", "scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_user_account_email",
                table: "user_account",
                column: "email",
                unique: true);
            // 30 §5 - Role and Row-Level Security
            migrationBuilder.Sql(@"
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'identity_app') THEN
    CREATE ROLE identity_app NOLOGIN;
  END IF;
END $$;
GRANT identity_app TO CURRENT_USER;
GRANT SELECT, INSERT, UPDATE ON economic_group, tenant, tenant_scope, user_account, tenant_member,
  role, member_role, refresh_token, outbox_message TO identity_app;
GRANT SELECT, INSERT ON auth_audit_log TO identity_app;
ALTER TABLE tenant_scope ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenant_scope FORCE ROW LEVEL SECURITY;
ALTER TABLE tenant_member ENABLE ROW LEVEL SECURITY;
ALTER TABLE tenant_member FORCE ROW LEVEL SECURITY;
ALTER TABLE member_role ENABLE ROW LEVEL SECURITY;
ALTER TABLE member_role FORCE ROW LEVEL SECURITY;
CREATE POLICY p_tenant_scope_isolation ON tenant_scope
  USING (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[])
         OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
CREATE POLICY p_tenant_member_isolation ON tenant_member
  USING (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[])
         OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
CREATE POLICY p_member_role_isolation ON member_role
  USING (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[])
         OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]))
  WITH CHECK (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.write_tenants', true), ',')::uuid[]));
");

            // 30 §6 - Role Seed
            migrationBuilder.Sql(@"
INSERT INTO role (id, name, scope, permissions, created_at) VALUES
 ('01900000-0000-7000-8000-000000000001','BUYER_OPERATOR','BUYER','{demanda:criar,demanda:listar,leilao:participar,pedido:visualizar}','2026-01-01T00:00:00Z'),
 ('01900000-0000-7000-8000-000000000002','SUPPLIER_SALES','SUPPLIER','{estoque:atualizar,fornecimento:publicar,leilao:lance,pedido:confirmar}','2026-01-01T00:00:00Z'),
 ('01900000-0000-7000-8000-000000000003','CARRIER_DISPATCHER','CARRIER','{frete:cotar,frete:visualizar,transportadora:configurar}','2026-01-01T00:00:00Z'),
 ('01900000-0000-7000-8000-000000000004','GROUP_MANAGER','TENANT_ADMIN','{mapa:visualizar,membro:listar,membro:vincular,tenant:criar,tenant:listar,usuario:suspender}','2026-01-01T00:00:00Z')
 ON CONFLICT (name) DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS p_member_role_isolation ON member_role;
DROP POLICY IF EXISTS p_tenant_member_isolation ON tenant_member;
DROP POLICY IF EXISTS p_tenant_scope_isolation ON tenant_scope;
");
            migrationBuilder.DropTable(
                name: "auth_audit_log");

            migrationBuilder.DropTable(
                name: "member_role");

            migrationBuilder.DropTable(
                name: "outbox_message");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "tenant_scope");

            migrationBuilder.DropTable(
                name: "role");

            migrationBuilder.DropTable(
                name: "tenant_member");

            migrationBuilder.DropTable(
                name: "tenant");

            migrationBuilder.DropTable(
                name: "user_account");

            migrationBuilder.DropTable(
                name: "economic_group");
        }
    }
}

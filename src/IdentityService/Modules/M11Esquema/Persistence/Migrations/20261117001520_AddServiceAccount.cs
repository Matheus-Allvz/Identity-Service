using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Modules.M11Esquema.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_auth_audit_log_event_type",
                table: "auth_audit_log");

            migrationBuilder.CreateTable(
                name: "service_account",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    client_secret_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "ACTIVE"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_account", x => x.id);
                    table.CheckConstraint("ck_service_account_client_id", "client_id ~ '^[a-z0-9][a-z0-9-]{2,63}$'");
                    table.CheckConstraint("ck_service_account_client_secret_hash", "starts_with(client_secret_hash, '$argon2id$v=19$m=19456,t=2,p=1$')");
                    table.CheckConstraint("ck_service_account_status", "status IN ('ACTIVE','SUSPENDED')");
                    table.ForeignKey(
                        name: "fk_service_account_role_id",
                        column: x => x.role_id,
                        principalTable: "role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_account_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenant",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_auth_audit_log_event_type",
                table: "auth_audit_log",
                sql: "event_type IN ('LOGIN_SUCCESS','LOGIN_FAILED','USER_LOCKED','CONTEXT_SWITCHED','TOKEN_REFRESHED','TOKEN_REUSE_DETECTED','LOGOUT','IMPERSONATION_STARTED','TENANT_CREATED','MEMBER_ADDED','TENANT_STATUS_CHANGED','USER_STATUS_CHANGED','SERVICE_TOKEN_ISSUED')");

            migrationBuilder.CreateIndex(
                name: "ix_service_account_role_id",
                table: "service_account",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_account_tenant_id",
                table: "service_account",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_service_account_client_id",
                table: "service_account",
                column: "client_id",
                unique: true);

            // NEW-06: a aplicação só lê contas de serviço (provisionamento é administrativo).
            // Mesma política das tabelas com tenant_id: visível no fluxo de autenticação ou para o tenant ativo.
            migrationBuilder.Sql(@"
GRANT SELECT ON service_account TO identity_app;
ALTER TABLE service_account ENABLE ROW LEVEL SECURITY;
ALTER TABLE service_account FORCE ROW LEVEL SECURITY;
CREATE POLICY p_service_account_isolation ON service_account
  USING (current_setting('app.auth_flow', true) = 'true'
         OR tenant_id = ANY (string_to_array(current_setting('app.allowed_tenants', true), ',')::uuid[]));
");        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_account");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auth_audit_log_event_type",
                table: "auth_audit_log");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auth_audit_log_event_type",
                table: "auth_audit_log",
                sql: "event_type IN ('LOGIN_SUCCESS','LOGIN_FAILED','USER_LOCKED','CONTEXT_SWITCHED','TOKEN_REFRESHED','TOKEN_REUSE_DETECTED','LOGOUT','IMPERSONATION_STARTED','TENANT_CREATED','MEMBER_ADDED','TENANT_STATUS_CHANGED','USER_STATUS_CHANGED')");
        }
    }
}

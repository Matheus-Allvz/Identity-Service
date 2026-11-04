using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Npgsql;
using StackExchange.Redis;

namespace IdentityService.Acceptance.Infrastructure;

public static class DbHelper
{
    public static async Task LimparAsync(string connectionString, IConnectionMultiplexer redis)
    {
        // Limpeza PostgreSQL
        await using (var conn = new NpgsqlConnection(connectionString))
        {
            await conn.OpenAsync();
            var sql = "TRUNCATE economic_group, tenant, tenant_scope, user_account, tenant_member, member_role, refresh_token, auth_audit_log, outbox_message CASCADE;";
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // Limpeza Redis (FLUSHDB)
        var endpoints = redis.GetEndPoints();
        if (endpoints.Length > 0)
        {
            var server = redis.GetServer(endpoints[0]);
            await server.FlushDatabaseAsync();
        }
    }

    public static async Task<string> GerarPasswordHashArgon2idAsync(string password, byte[]? salt = null)
    {
        var saltBytes = salt ?? RandomNumberGenerator.GetBytes(16);
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            MemorySize = 19456,
            Iterations = 2,
            DegreeOfParallelism = 1,
            Salt = saltBytes
        };

        var hashBytes = await argon2.GetBytesAsync(32);
        var saltB64 = Convert.ToBase64String(saltBytes).TrimEnd('=');
        var hashB64 = Convert.ToBase64String(hashBytes).TrimEnd('=');
        return $"$argon2id$v=19$m=19456,t=2,p=1${saltB64}${hashB64}";
    }

    public static async Task InserirGrupoAsync(string cs, Guid id, string name, string status = "ACTIVE", DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = "INSERT INTO economic_group(id, name, status, created_at, updated_at) VALUES (@id, @name, @status, @now, @now);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirTenantAsync(
        string cs,
        Guid id,
        Guid groupId,
        string cnpj,
        string legalName,
        string branchType = "HQ",
        decimal geoLat = 0m,
        decimal geoLng = 0m,
        string status = "ACTIVE",
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = @"INSERT INTO tenant(id, group_id, cnpj, legal_name, branch_type, geo_lat, geo_lng, status, created_at, updated_at)
                    VALUES (@id, @groupId, @cnpj, @legalName, @branchType, @geoLat, @geoLng, @status, @now, @now);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("groupId", groupId);
        cmd.Parameters.AddWithValue("cnpj", cnpj);
        cmd.Parameters.AddWithValue("legalName", legalName);
        cmd.Parameters.AddWithValue("branchType", branchType);
        cmd.Parameters.AddWithValue("geoLat", geoLat);
        cmd.Parameters.AddWithValue("geoLng", geoLng);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirTenantScopeAsync(string cs, Guid id, Guid tenantId, string scope, bool isActive = true, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = "INSERT INTO tenant_scope(id, tenant_id, scope, is_active, created_at) VALUES (@id, @tenantId, @scope, @isActive, @now);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("scope", scope);
        cmd.Parameters.AddWithValue("isActive", isActive);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirUserAccountAsync(
        string cs,
        Guid id,
        string email,
        string passwordHash,
        string name,
        string status = "ACTIVE",
        string platformRole = "NONE",
        int failedLoginAttempts = 0,
        DateTimeOffset? lockedUntil = null,
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        if (!passwordHash.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$"))
        {
            passwordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2hkYXRhMTIzNDU2Nzg5MDEyMzQ1Njc4OTA";
        }
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = @"INSERT INTO user_account(id, email, password_hash, name, status, platform_role, failed_login_attempts, locked_until, created_at, updated_at)
                    VALUES (@id, @email, @passwordHash, @name, @status, @platformRole, @failedLoginAttempts, @lockedUntil, @now, @now);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("platformRole", platformRole);
        cmd.Parameters.AddWithValue("failedLoginAttempts", failedLoginAttempts);
        cmd.Parameters.AddWithValue("lockedUntil", (object?)lockedUntil ?? DBNull.Value);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirTenantMemberAsync(
        string cs,
        Guid id,
        Guid tenantId,
        Guid userId,
        bool isOwner = false,
        bool canViewGroup = false,
        string status = "ACTIVE",
        DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = @"INSERT INTO tenant_member(id, tenant_id, user_id, is_owner, can_view_group, status, created_at, updated_at)
                    VALUES (@id, @tenantId, @userId, @isOwner, @canViewGroup, @status, @now, @now);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("isOwner", isOwner);
        cmd.Parameters.AddWithValue("canViewGroup", canViewGroup);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirMemberRoleAsync(string cs, Guid id, Guid tenantId, Guid tenantMemberId, Guid roleId, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = "INSERT INTO member_role(id, tenant_id, tenant_member_id, role_id, created_at) VALUES (@id, @tenantId, @tenantMemberId, @roleId, @now);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("tenantMemberId", tenantMemberId);
        cmd.Parameters.AddWithValue("roleId", roleId);
        cmd.Parameters.AddWithValue("now", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirRefreshTokenAsync(
        string cs,
        Guid id,
        Guid familyId,
        Guid userId,
        string tokenHash,
        string status,
        string viewMode,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        Guid? replacedById = null,
        Guid? groupId = null,
        Guid? activeTenantId = null,
        Guid[]? allowedTenants = null,
        string? activeScope = null,
        DateTimeOffset? consumedAt = null,
        DateTimeOffset? revokedAt = null)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = @"INSERT INTO refresh_token(id, family_id, user_id, token_hash, status, replaced_by_id, view_mode, group_id, active_tenant_id, allowed_tenants, active_scope, expires_at, consumed_at, revoked_at, created_at)
                    VALUES (@id, @familyId, @userId, @tokenHash, @status, @replacedById, @viewMode, @groupId, @activeTenantId, @allowedTenants, @activeScope, @expiresAt, @consumedAt, @revokedAt, @createdAt);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("familyId", familyId);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("replacedById", (object?)replacedById ?? DBNull.Value);
        cmd.Parameters.AddWithValue("viewMode", viewMode);
        cmd.Parameters.AddWithValue("groupId", (object?)groupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("activeTenantId", (object?)activeTenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("allowedTenants", allowedTenants ?? Array.Empty<Guid>());
        cmd.Parameters.AddWithValue("activeScope", (object?)activeScope ?? DBNull.Value);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        cmd.Parameters.AddWithValue("consumedAt", (object?)consumedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("revokedAt", (object?)revokedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task InserirOutboxMessageAsync(
        string cs,
        Guid id,
        string topic,
        string partitionKey,
        string headersJson,
        string payloadJson,
        string status = "PENDING",
        int retryCount = 0,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? processedAt = null)
    {
        var now = createdAt ?? DateTimeOffset.UtcNow;
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var sql = @"INSERT INTO outbox_message(id, topic, partition_key, headers, payload, status, retry_count, created_at, processed_at)
                    VALUES (@id, @topic, @partitionKey, @headers::jsonb, @payload::jsonb, @status, @retryCount, @now, @processedAt);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("topic", topic);
        cmd.Parameters.AddWithValue("partitionKey", partitionKey);
        cmd.Parameters.AddWithValue("headers", headersJson);
        cmd.Parameters.AddWithValue("payload", payloadJson);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("retryCount", retryCount);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("processedAt", (object?)processedAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task ExecutarComoIdentityAppAsync(string connectionString, string? allowedTenants, string? writeTenants, bool authFlow, Func<NpgsqlConnection, Task> action)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmdInit = new NpgsqlCommand(@"
            SET ROLE identity_app;
            SELECT set_config('app.allowed_tenants', @allowed, false),
                   set_config('app.write_tenants', @write, false),
                   set_config('app.auth_flow', @auth, false);", conn);
        cmdInit.Parameters.AddWithValue("allowed", allowedTenants ?? "");
        cmdInit.Parameters.AddWithValue("write", writeTenants ?? "");
        cmdInit.Parameters.AddWithValue("auth", authFlow ? "true" : "false");
        await cmdInit.ExecuteNonQueryAsync();

        await action(conn);
    }
}

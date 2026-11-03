using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento.Interface;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Modules.M11Esquema.Persistence;

public class IdentityDbContext : DbContext
{
    private readonly ITenantIsolation? _tenantIsolation;

    public IAuthContext AuthContext { get; }
    public bool TenantIsolationBypass { get; set; }

    public DbSet<EconomicGroup> EconomicGroups => Set<EconomicGroup>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantScope> TenantScopes => Set<TenantScope>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<TenantMember> TenantMembers => Set<TenantMember>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<MemberRole> MemberRoles => Set<MemberRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuthAuditLog> AuthAuditLogs => Set<AuthAuditLog>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        IAuthContext? authContext = null,
        ITenantIsolation? tenantIsolation = null)
        : base(options)
    {
        AuthContext = authContext ?? new DefaultAuthContext();
        _tenantIsolation = tenantIsolation;
        TenantIsolationBypass = false;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ENT-01 economic_group
        modelBuilder.Entity<EconomicGroup>(entity =>
        {
            entity.ToTable("economic_group");
            entity.HasKey(e => e.Id).HasName("pk_economic_group");
            entity.Property(e => e.Name).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(EntityStatus.ACTIVE).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_economic_group_name", "char_length(btrim(name)) >= 2");
                t.HasCheckConstraint("ck_economic_group_status", "status IN ('ACTIVE','SUSPENDED')");
            });
        });

        // ENT-02 tenant
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenant");
            entity.HasKey(e => e.Id).HasName("pk_tenant");
            entity.Property(e => e.GroupId).IsRequired();
            entity.Property(e => e.Cnpj).HasColumnType("char(14)").IsRequired();
            entity.Property(e => e.LegalName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.BranchType).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(e => e.GeoLat).HasColumnType("numeric(9,6)").IsRequired();
            entity.Property(e => e.GeoLng).HasColumnType("numeric(9,6)").IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(EntityStatus.ACTIVE).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Tenants)
                .HasForeignKey(e => e.GroupId)
                .HasConstraintName("fk_tenant_group_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Cnpj).IsUnique().HasDatabaseName("uq_tenant_cnpj");
            entity.HasIndex(e => e.GroupId).HasDatabaseName("ix_tenant_group_id");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_tenant_cnpj", "cnpj ~ '^[0-9]{14}$'");
                t.HasCheckConstraint("ck_tenant_legal_name", "char_length(btrim(legal_name)) >= 2");
                t.HasCheckConstraint("ck_tenant_branch_type", "branch_type IN ('HQ','BRANCH')");
                t.HasCheckConstraint("ck_tenant_geo_lat", "geo_lat BETWEEN -90 AND 90");
                t.HasCheckConstraint("ck_tenant_geo_lng", "geo_lng BETWEEN -180 AND 180");
                t.HasCheckConstraint("ck_tenant_status", "status IN ('ACTIVE','SUSPENDED')");
            });
        });

        // ENT-03 tenant_scope
        modelBuilder.Entity<TenantScope>(entity =>
        {
            entity.ToTable("tenant_scope");
            entity.HasKey(e => e.Id).HasName("pk_tenant_scope");
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Scopes)
                .HasForeignKey(e => e.TenantId)
                .HasConstraintName("fk_tenant_scope_tenant_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.TenantId, e.Scope }).IsUnique().HasDatabaseName("uq_tenant_scope_tenant_id_scope");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_tenant_scope_scope", "scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')");
            });
        });

        // ENT-04 user_account
        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.ToTable("user_account");
            entity.HasKey(e => e.Id).HasName("pk_user_account");
            entity.Property(e => e.Email).HasMaxLength(320).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(UserStatus.ACTIVE).IsRequired();
            entity.Property(e => e.PlatformRole).HasConversion<string>().HasMaxLength(24).HasDefaultValue(PlatformRole.NONE).IsRequired();
            entity.Property(e => e.FailedLoginAttempts).HasDefaultValue(0).IsRequired();
            entity.Property(e => e.LockedUntil).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("uq_user_account_email");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_user_account_email", "email = lower(btrim(email)) AND char_length(email) >= 5");
                t.HasCheckConstraint("ck_user_account_password_hash", "starts_with(password_hash, '$argon2id$v=19$m=19456,t=2,p=1$')");
                t.HasCheckConstraint("ck_user_account_name", "char_length(btrim(name)) >= 2");
                t.HasCheckConstraint("ck_user_account_status", "status IN ('ACTIVE','LOCKED','SUSPENDED')");
                t.HasCheckConstraint("ck_user_account_platform_role", "platform_role IN ('NONE','SUPPORT_AGENT','PLATFORM_ADMIN')");
                t.HasCheckConstraint("ck_user_account_failed_login_attempts", "failed_login_attempts BETWEEN 0 AND 5");
                t.HasCheckConstraint("ck_user_account_locked_until", "(status = 'LOCKED') = (locked_until IS NOT NULL)");
            });
        });

        // ENT-05 tenant_member
        modelBuilder.Entity<TenantMember>(entity =>
        {
            entity.ToTable("tenant_member");
            entity.HasKey(e => e.Id).HasName("pk_tenant_member");
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.IsOwner).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.CanViewGroup).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(EntityStatus.ACTIVE).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.Members)
                .HasForeignKey(e => e.TenantId)
                .HasConstraintName("fk_tenant_member_tenant_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_tenant_member_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.Id, e.TenantId }).IsUnique().HasDatabaseName("uq_tenant_member_id_tenant_id");
            entity.HasIndex(e => new { e.TenantId, e.UserId }).IsUnique().HasDatabaseName("uq_tenant_member_tenant_user");
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_tenant_member_user_id");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_tenant_member_status", "status IN ('ACTIVE','SUSPENDED')");
            });
        });

        // ENT-06 role
        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("role");
            entity.HasKey(e => e.Id).HasName("pk_role");
            entity.Property(e => e.Name).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(e => e.Permissions).HasColumnType("text[]").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("uq_role_name");
            entity.HasIndex(e => e.Scope).HasDatabaseName("ix_role_scope");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_role_name", "name ~ '^[A-Z0-9_]{3,64}$'");
                t.HasCheckConstraint("ck_role_scope", "scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')");
                t.HasCheckConstraint("ck_role_permissions",
                    "cardinality(permissions) >= 1 " +
                    "AND array_to_string(permissions, ',') ~ '^[a-z_]+:[a-z_]+(,[a-z_]+:[a-z_]+)*$' " +
                    "AND char_length(array_to_string(permissions, ',')) - char_length(replace(array_to_string(permissions, ','), ',', '')) = cardinality(permissions) - 1");
            });
        });

        // ENT-07 member_role
        modelBuilder.Entity<MemberRole>(entity =>
        {
            entity.ToTable("member_role");
            entity.HasKey(e => e.Id).HasName("pk_member_role");
            entity.Property(e => e.TenantId).IsRequired();
            entity.Property(e => e.TenantMemberId).IsRequired();
            entity.Property(e => e.RoleId).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(e => e.Role)
                .WithMany(r => r.MemberRoles)
                .HasForeignKey(e => e.RoleId)
                .HasConstraintName("fk_member_role_role_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .HasConstraintName("fk_member_role_tenant_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.TenantMember)
                .WithMany(tm => tm.MemberRoles)
                .HasPrincipalKey(tm => new { tm.Id, tm.TenantId })
                .HasForeignKey(e => new { e.TenantMemberId, e.TenantId })
                .HasConstraintName("fk_member_role_tenant_member")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.TenantMemberId, e.RoleId }).IsUnique().HasDatabaseName("uq_member_role_member_role");
            entity.HasIndex(e => e.TenantId).HasDatabaseName("ix_member_role_tenant_id");
        });

        // ENT-08 refresh_token
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_token");
            entity.HasKey(e => e.Id).HasName("pk_refresh_token");
            entity.Property(e => e.FamilyId).IsRequired();
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.TokenHash).HasColumnType("char(64)").IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(RefreshTokenStatus.ACTIVE).IsRequired();
            entity.Property(e => e.ReplacedById);
            entity.Property(e => e.ViewMode).HasConversion<string>().HasMaxLength(16).HasDefaultValue(ViewMode.NONE).IsRequired();
            entity.Property(e => e.GroupId);
            entity.Property(e => e.ActiveTenantId);
            entity.Property(e => e.AllowedTenants).HasColumnType("uuid[]").HasDefaultValue(Array.Empty<Guid>()).IsRequired();
            entity.Property(e => e.ActiveScope).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz").IsRequired();
            entity.Property(e => e.ConsumedAt).HasColumnType("timestamptz");
            entity.Property(e => e.RevokedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_refresh_token_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ReplacedBy)
                .WithMany()
                .HasForeignKey(e => e.ReplacedById)
                .HasConstraintName("fk_refresh_token_replaced_by_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Group)
                .WithMany()
                .HasForeignKey(e => e.GroupId)
                .HasConstraintName("fk_refresh_token_group_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ActiveTenant)
                .WithMany()
                .HasForeignKey(e => e.ActiveTenantId)
                .HasConstraintName("fk_refresh_token_active_tenant_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("uq_refresh_token_token_hash");
            entity.HasIndex(e => e.FamilyId).HasDatabaseName("ix_refresh_token_family_id");
            entity.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("ix_refresh_token_user_id_status");
            entity.HasIndex(e => e.FamilyId)
                .IsUnique()
                .HasFilter("status = 'ACTIVE'")
                .HasDatabaseName("uq_refresh_token_family_active");
            entity.HasIndex(e => e.AllowedTenants)
                .HasMethod("GIN")
                .HasDatabaseName("ix_refresh_token_allowed_tenants");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_refresh_token_token_hash", "token_hash ~ '^[0-9a-f]{64}$'");
                t.HasCheckConstraint("ck_refresh_token_status", "status IN ('ACTIVE','CONSUMED','REVOKED')");
                t.HasCheckConstraint("ck_refresh_token_view_mode", "view_mode IN ('NONE','SINGLE','MACRO_GROUP')");
                t.HasCheckConstraint("ck_refresh_token_active_scope", "active_scope IS NULL OR active_scope IN ('BUYER','SUPPLIER','CARRIER','TENANT_ADMIN')");
                t.HasCheckConstraint("ck_refresh_token_expires_at", "expires_at > created_at");
                t.HasCheckConstraint("ck_refresh_token_context",
                    "(view_mode = 'NONE' AND group_id IS NULL AND active_tenant_id IS NULL AND active_scope IS NULL AND cardinality(allowed_tenants) = 0) " +
                    "OR (view_mode = 'SINGLE' AND group_id IS NOT NULL AND active_tenant_id IS NOT NULL AND active_scope IS NOT NULL AND allowed_tenants = ARRAY[active_tenant_id]) " +
                    "OR (view_mode = 'MACRO_GROUP' AND group_id IS NOT NULL AND active_tenant_id IS NULL AND active_scope IS NOT NULL AND cardinality(allowed_tenants) BETWEEN 1 AND 50)");
                t.HasCheckConstraint("ck_refresh_token_status_dates",
                    "(status <> 'ACTIVE' OR (consumed_at IS NULL AND revoked_at IS NULL)) " +
                    "AND (status <> 'CONSUMED' OR (consumed_at IS NOT NULL AND revoked_at IS NULL)) " +
                    "AND (status <> 'REVOKED' OR revoked_at IS NOT NULL)");
            });
        });

        // ENT-09 auth_audit_log
        modelBuilder.Entity<AuthAuditLog>(entity =>
        {
            entity.ToTable("auth_audit_log");
            entity.HasKey(e => e.Id).HasName("pk_auth_audit_log");
            entity.Property(e => e.EventType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.UserId);
            entity.Property(e => e.ActorUserId);
            entity.Property(e => e.TenantId);
            entity.Property(e => e.GroupId);
            entity.Property(e => e.TicketId).HasMaxLength(32);
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
            entity.Property(e => e.CorrelationId).IsRequired();
            entity.Property(e => e.Metadata).HasColumnType("jsonb").HasDefaultValue("{}").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .HasConstraintName("fk_auth_audit_log_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ActorUser)
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .HasConstraintName("fk_auth_audit_log_actor_user_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .HasConstraintName("fk_auth_audit_log_tenant_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Group)
                .WithMany()
                .HasForeignKey(e => e.GroupId)
                .HasConstraintName("fk_auth_audit_log_group_id")
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.UserId, e.CreatedAt }).HasDatabaseName("ix_auth_audit_log_user_id_created_at");
            entity.HasIndex(e => new { e.TenantId, e.CreatedAt }).HasDatabaseName("ix_auth_audit_log_tenant_id_created_at");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_auth_audit_log_event_type", "event_type IN ('LOGIN_SUCCESS','LOGIN_FAILED','USER_LOCKED','CONTEXT_SWITCHED','TOKEN_REFRESHED','TOKEN_REUSE_DETECTED','LOGOUT','IMPERSONATION_STARTED','TENANT_CREATED','MEMBER_ADDED','TENANT_STATUS_CHANGED','USER_STATUS_CHANGED')");
                t.HasCheckConstraint("ck_auth_audit_log_ticket_id", "ticket_id IS NULL OR ticket_id ~ '^[A-Z]{2,10}-[0-9]{1,10}$'");
                t.HasCheckConstraint("ck_auth_audit_log_ip_address", "char_length(ip_address) BETWEEN 2 AND 45");
                t.HasCheckConstraint("ck_auth_audit_log_metadata", "jsonb_typeof(metadata) = 'object'");
            });
        });

        // ENT-10 outbox_message
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_message");
            entity.HasKey(e => e.Id).HasName("pk_outbox_message");
            entity.Property(e => e.Topic).HasMaxLength(64).IsRequired();
            entity.Property(e => e.PartitionKey).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Headers).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).HasDefaultValue(OutboxStatus.PENDING).IsRequired();
            entity.Property(e => e.RetryCount).HasDefaultValue(0).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
            entity.Property(e => e.ProcessedAt).HasColumnType("timestamptz");

            entity.HasIndex(e => e.CreatedAt)
                .HasFilter("status = 'PENDING'")
                .HasDatabaseName("ix_outbox_message_pending");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_outbox_message_topic", "topic IN ('empresa_cadastrada','tenant_bloqueado','usuario_bloqueado','sessao_comprometida')");
                t.HasCheckConstraint("ck_outbox_message_partition_key", "partition_key ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'");
                t.HasCheckConstraint("ck_outbox_message_headers", "jsonb_typeof(headers) = 'object'");
                t.HasCheckConstraint("ck_outbox_message_payload", "jsonb_typeof(payload) = 'object'");
                t.HasCheckConstraint("ck_outbox_message_status", "status IN ('PENDING','PUBLISHED','FAILED')");
                t.HasCheckConstraint("ck_outbox_message_retry_count", "retry_count BETWEEN 0 AND 5");
                t.HasCheckConstraint("ck_outbox_message_processed_at", "(status = 'PENDING') = (processed_at IS NULL)");
                t.HasCheckConstraint("ck_outbox_message_failed_retry", "(status = 'FAILED') = (retry_count = 5)");
            });
        });

        // Layer 2 Isolation Filters
        _tenantIsolation?.ApplyQueryFilters(modelBuilder, this);
    }

    private sealed class DefaultAuthContext : IAuthContext
    {
        public string? Jti => null;
        public string? TicketId => null;
        public Guid? UserId => null;
        public Guid? FamilyId => null;
        public Guid? GroupId => null;
        public Guid? ActiveTenantId => null;
        public Guid? ActorUserId => null;
        public long? Iat => null;
        public long? Exp => null;
        public PlatformRole? PlatformRole => null;
        public ViewMode? ViewMode => null;
        public OperationalScope? ActiveScope => null;
        public IReadOnlyList<Guid> AllowedTenants { get; } = [];
        public IReadOnlyList<string> Roles { get; } = [];
        public IReadOnlyList<string> Permissions { get; } = [];
        public bool IsAuthenticated => false;
        public bool BypassTenantFilter => true;
        public Guid CorrelationId { get; } = Guid.Empty;
        public string IpAddress => "0.0.0.0";
        public IReadOnlySet<Guid> WriteTenants { get; } = new HashSet<Guid>();
        public void AuthorizeTenantWrite(Guid tenantId) { }
    }
}

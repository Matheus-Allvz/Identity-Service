using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Redis;
using IdentityService.Modules.M08Acessos;
using IdentityService.Modules.M08Acessos.Interface;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Xunit;
using RoleEntity = IdentityService.Modules.M11Esquema.Persistence.Entities.Role;

namespace IdentityService.Tests;

[Trait("Fase", "F00")]
[Collection("SharedContainers")]
public class M08AcessosTests
{
    private const string ValidPasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g";
    private readonly TestcontainersFixture _fixture;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public M08AcessosTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private AccessQueries CreateQueries(IdentityDbContext dbContext, IAuthContext authContext)
    {
        var redisMux = ConnectionMultiplexer.Connect(_fixture.GetRedisConnectionString());
        var denylist = new DenylistService(redisMux, _timeProvider);
        var isolation = new TenantIsolation();
        return new AccessQueries(dbContext, denylist, isolation);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_18_1_Borda_ResolucaoPapeisEPermissoes_ProprietarioTenant_Single()
    {
        await _fixture.CleanDatabaseAsync();

        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var tenantId = Guid.Parse("01900000-0000-7000-8000-000000000100");

        var authContext = new AuthContext
        {
            BypassTenantFilter = true,
            UserId = userId,
            ActiveTenantId = tenantId,
            AllowedTenants = new[] { tenantId }
        };

        await using var db = _fixture.CreateDbContext(authContext);

        // Seed data
        var group = new EconomicGroup { Id = groupId, Name = "Grupo Teste", Status = EntityStatus.ACTIVE };
        var tenant = new Tenant
        {
            Id = tenantId,
            GroupId = groupId,
            Cnpj = "99000001000101",
            LegalName = "Tenant Teste 01",
            BranchType = BranchType.HQ,
            GeoLat = 0,
            GeoLng = 0,
            Status = EntityStatus.ACTIVE
        };
        var scope = new TenantScope { TenantId = tenantId, Scope = OperationalScope.BUYER, IsActive = true };
        var user = new UserAccount
        {
            Id = userId,
            Email = "user01@example.test",
            PasswordHash = ValidPasswordHash,
            Name = "Utilizador Teste 01",
            Status = UserStatus.ACTIVE,
            PlatformRole = PlatformRole.NONE
        };
        var member = new TenantMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            IsOwner = true,
            CanViewGroup = false,
            Status = EntityStatus.ACTIVE
        };

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BUYER_OPERATOR");
        if (role == null)
        {
            role = new RoleEntity
            {
                Id = Guid.NewGuid(),
                Name = "BUYER_OPERATOR",
                Scope = OperationalScope.BUYER,
                Permissions = new[] { "demanda:criar", "demanda:listar", "leilao:participar", "pedido:visualizar" }
            };
            db.Roles.Add(role);
        }

        db.EconomicGroups.Add(group);
        db.Tenants.Add(tenant);
        db.TenantScopes.Add(scope);
        db.UserAccounts.Add(user);
        db.TenantMembers.Add(member);
        await db.SaveChangesAsync();

        var queries = CreateQueries(db, authContext);
        var result = await queries.ResolveRolesAsync(userId, tenantId, OperationalScope.BUYER);

        Assert.True(result.Ok);
        Assert.NotNull(result.Value);
        Assert.Contains("BUYER_OPERATOR", result.Value.Roles);
        Assert.Equal(4, result.Value.Permissions.Count);
        Assert.Equal(
            new[] { "demanda:criar", "demanda:listar", "leilao:participar", "pedido:visualizar" },
            result.Value.Permissions);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_18_2_Borda_ResolucaoContextoMacroGroup_IntersecaoPermissoesEBordaVazio()
    {
        await _fixture.CleanDatabaseAsync();

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var ten1 = Guid.NewGuid();
        var ten2 = Guid.NewGuid();
        var ten3 = Guid.NewGuid();

        var authContext = new AuthContext { BypassTenantFilter = true, UserId = userId };
        await using var db = _fixture.CreateDbContext(authContext);

        var group = new EconomicGroup { Id = groupId, Name = "Grupo Macro", Status = EntityStatus.ACTIVE };
        var t1 = new Tenant { Id = ten1, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, Status = EntityStatus.ACTIVE };
        var t2 = new Tenant { Id = ten2, GroupId = groupId, Cnpj = "99000001000102", LegalName = "Tenant 2", BranchType = BranchType.BRANCH, Status = EntityStatus.ACTIVE };
        var t3 = new Tenant { Id = ten3, GroupId = groupId, Cnpj = "99000001000103", LegalName = "Tenant 3", BranchType = BranchType.BRANCH, Status = EntityStatus.ACTIVE };

        var u = new UserAccount { Id = userId, Email = "macro@example.test", PasswordHash = ValidPasswordHash, Name = "Utilizador Teste 02", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE };

        var roleBuyer = await db.Roles.FirstOrDefaultAsync(r => r.Name == "BUYER_OPERATOR");
        if (roleBuyer == null)
        {
            roleBuyer = new RoleEntity
            {
                Id = Guid.NewGuid(),
                Name = "BUYER_OPERATOR",
                Scope = OperationalScope.BUYER,
                Permissions = new[] { "demanda:criar", "demanda:listar", "leilao:participar", "pedido:visualizar" }
            };
            db.Roles.Add(roleBuyer);
        }

        var m1 = new TenantMember { Id = Guid.NewGuid(), TenantId = ten1, UserId = userId, IsOwner = true, CanViewGroup = true, Status = EntityStatus.ACTIVE };
        var m2 = new TenantMember { Id = Guid.NewGuid(), TenantId = ten2, UserId = userId, IsOwner = false, CanViewGroup = true, Status = EntityStatus.ACTIVE };
        var m3 = new TenantMember { Id = Guid.NewGuid(), TenantId = ten3, UserId = userId, IsOwner = false, CanViewGroup = true, Status = EntityStatus.ACTIVE };

        var mr2 = new MemberRole { Id = Guid.NewGuid(), TenantId = ten2, TenantMemberId = m2.Id, RoleId = roleBuyer.Id };

        db.EconomicGroups.Add(group);
        db.Tenants.AddRange(t1, t2, t3);
        db.TenantScopes.AddRange(
            new TenantScope { TenantId = ten1, Scope = OperationalScope.BUYER, IsActive = true },
            new TenantScope { TenantId = ten2, Scope = OperationalScope.BUYER, IsActive = true },
            new TenantScope { TenantId = ten3, Scope = OperationalScope.BUYER, IsActive = true }
        );
        db.UserAccounts.Add(u);
        db.TenantMembers.AddRange(m1, m2, m3);
        db.MemberRoles.Add(mr2);
        await db.SaveChangesAsync();

        var queries = CreateQueries(db, authContext);

        // Caso 1: [TEN-1, TEN-2] -> Interseção válida
        var resSuccess = await queries.ResolveMacroGroupAsync(userId, groupId, new[] { ten1, ten2 }, OperationalScope.BUYER);
        Assert.True(resSuccess.Ok);
        Assert.Null(resSuccess.ErrorCode);
        Assert.NotNull(resSuccess.Value);
        Assert.Equal(
            new[] { "demanda:criar", "demanda:listar", "leilao:participar", "pedido:visualizar" },
            resSuccess.Value.Permissions);

        // Caso 2: [TEN-1, TEN-3] -> TEN-3 não tem papéis -> interseção vazia -> ERR-CTX-003
        var resEmpty = await queries.ResolveMacroGroupAsync(userId, groupId, new[] { ten1, ten3 }, OperationalScope.BUYER);
        Assert.False(resEmpty.Ok);
        Assert.Equal("ERR-CTX-003", resEmpty.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_18_3_Borda_ValidacaoBordasListaTenants_MacroGroup()
    {
        var authContext = new AuthContext { BypassTenantFilter = true };
        await using var db = _fixture.CreateDbContext(authContext);
        var queries = CreateQueries(db, authContext);

        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        // 1. Quantidade = 0 elementos -> ERR-CTX-004
        var res0 = await queries.ResolveMacroGroupAsync(userId, groupId, Array.Empty<Guid>(), OperationalScope.BUYER);
        Assert.False(res0.Ok);
        Assert.Equal("ERR-CTX-004", res0.ErrorCode);

        // 2. Quantidade = 51 elementos -> ERR-CTX-004
        var list51 = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        var res51 = await queries.ResolveMacroGroupAsync(userId, groupId, list51, OperationalScope.BUYER);
        Assert.False(res51.Ok);
        Assert.Equal("ERR-CTX-004", res51.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_18_4_Borda_VerificacaoUtilizadorSuspenso_CheckUserAsync()
    {
        await _fixture.CleanDatabaseAsync();

        var userId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var authContext = new AuthContext { BypassTenantFilter = true };
        await using var db = _fixture.CreateDbContext(authContext);

        var user = new UserAccount
        {
            Id = userId,
            Email = "suspended@example.test",
            PasswordHash = ValidPasswordHash,
            Name = "Utilizador Teste 03",
            Status = UserStatus.SUSPENDED,
            PlatformRole = PlatformRole.NONE
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();

        var queries = CreateQueries(db, authContext);
        var result = await queries.CheckUserAsync(userId, tRef: null);

        Assert.False(result.Ok);
        Assert.Equal("ERR-AUTH-004", result.ErrorCode);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_18_5_Borda_ConsultaFiliacaoGrupo_GetGroupMembershipAsync_ComBypass()
    {
        await _fixture.CleanDatabaseAsync();

        var userId = Guid.NewGuid();
        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var tenantId = Guid.NewGuid();

        var authContext = new AuthContext { BypassTenantFilter = true, UserId = userId };
        await using var db = _fixture.CreateDbContext(authContext);

        var group = new EconomicGroup { Id = groupId, Name = "Grupo Membro", Status = EntityStatus.ACTIVE };
        var tenant = new Tenant { Id = tenantId, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant Membro", BranchType = BranchType.HQ, Status = EntityStatus.ACTIVE };
        var user = new UserAccount { Id = userId, Email = "member@example.test", PasswordHash = ValidPasswordHash, Name = "Utilizador Teste 04", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE };
        var member = new TenantMember { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, IsOwner = false, CanViewGroup = true, Status = EntityStatus.ACTIVE };

        db.EconomicGroups.Add(group);
        db.Tenants.Add(tenant);
        db.UserAccounts.Add(user);
        db.TenantMembers.Add(member);
        await db.SaveChangesAsync();

        var queries = CreateQueries(db, authContext);
        var membership = await queries.GetGroupMembershipAsync(userId, groupId);

        Assert.Equal(GroupMembershipKind.MEMBER, membership);
    }
}

using System.Data;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M13Isolamento;
using IdentityService.Modules.M13Isolamento.Interface;
using IdentityService.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace IdentityService.Tests;

[Trait("Fase", "F00")]
[Collection("SharedContainers")]
public class M13IsolamentoTests
{
    private readonly TestcontainersFixture _fixture;

    public M13IsolamentoTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_1_Borda_Camada2_FiltragemLeituraLinqOmiteRegistosForaDeAllowedTenants()
    {
        await _fixture.CleanDatabaseAsync();

        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var ten1Id = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var ten2Id = Guid.Parse("01900000-0000-7000-8000-000000000020");
        var user1Id = Guid.Parse("01900000-0000-7000-8000-000000000101");
        var user2Id = Guid.Parse("01900000-0000-7000-8000-000000000102");
        var now = DateTimeOffset.UtcNow;

        // Inserir com bypass
        var seedContext = new AuthContext { BypassTenantFilter = true };
        await using (var db = _fixture.CreateDbContext(seedContext))
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 16.1", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten1 = new Tenant { Id = ten1Id, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten2 = new Tenant { Id = ten2Id, GroupId = groupId, Cnpj = "99000002000101", LegalName = "Tenant 2", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user1 = new UserAccount { Id = user1Id, Email = "u1.ca161@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "User 1", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var user2 = new UserAccount { Id = user2Id, Email = "u2.ca161@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "User 2", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };

            var member1 = new TenantMember { Id = Guid.NewGuid(), TenantId = ten1Id, UserId = user1Id, IsOwner = true, Status = EntityStatus.ACTIVE, CreatedAt = now };
            var member2 = new TenantMember { Id = Guid.NewGuid(), TenantId = ten2Id, UserId = user2Id, IsOwner = true, Status = EntityStatus.ACTIVE, CreatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.AddRange(ten1, ten2);
            db.UserAccounts.AddRange(user1, user2);
            db.TenantMembers.AddRange(member1, member2);
            await db.SaveChangesAsync();
        }

        // Consulta com AllowedTenants = [TEN1]
        var authContextTen1 = new AuthContext
        {
            ViewMode = ViewMode.SINGLE,
            AllowedTenants = [ten1Id],
            BypassTenantFilter = false
        };

        await using (var dbTen1 = _fixture.CreateDbContext(authContextTen1))
        {
            var members = await dbTen1.TenantMembers.AsNoTracking().ToListAsync();
            Assert.Single(members);
            Assert.Equal(ten1Id, members[0].TenantId);
            Assert.DoesNotContain(members, m => m.TenantId == ten2Id);
        }

        // Consulta com AllowedTenants vazio
        var authContextEmpty = new AuthContext
        {
            ViewMode = ViewMode.NONE,
            AllowedTenants = [],
            BypassTenantFilter = false
        };

        await using (var dbEmpty = _fixture.CreateDbContext(authContextEmpty))
        {
            var members = await dbEmpty.TenantMembers.AsNoTracking().ToListAsync();
            Assert.Empty(members);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_2_Borda_Camada2_LeituraComMultiplosTenantsEmVisaoMacroEModoBypass()
    {
        await _fixture.CleanDatabaseAsync();

        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var now = DateTimeOffset.UtcNow;

        var tenants = new List<Tenant>();
        var members = new List<TenantMember>();
        var userIds = new List<Guid>();

        var seedContext = new AuthContext { BypassTenantFilter = true };
        await using (var db = _fixture.CreateDbContext(seedContext))
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 50 Tenants", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            db.EconomicGroups.Add(group);

            // Criar 51 tenants (50 autorizados + 1 extra)
            for (int i = 1; i <= 51; i++)
            {
                var tenantId = Guid.Parse($"01900000-0000-7000-8000-{i:D12}");
                var userId = Guid.Parse($"01900000-0000-7000-9000-{i:D12}");
                userIds.Add(userId);

                var tenant = new Tenant
                {
                    Id = tenantId,
                    GroupId = groupId,
                    Cnpj = $"99000001{i:D6}",
                    LegalName = $"Tenant {i}",
                    BranchType = BranchType.BRANCH,
                    GeoLat = 0,
                    GeoLng = 0,
                    Status = EntityStatus.ACTIVE,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                tenants.Add(tenant);

                var user = new UserAccount
                {
                    Id = userId,
                    Email = $"user{i}.macro@example.test",
                    PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g",
                    Name = $"User {i}",
                    Status = UserStatus.ACTIVE,
                    PlatformRole = PlatformRole.NONE,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.UserAccounts.Add(user);

                var member = new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    IsOwner = false,
                    CanViewGroup = true,
                    Status = EntityStatus.ACTIVE,
                    CreatedAt = now
                };
                members.Add(member);
            }

            db.Tenants.AddRange(tenants);
            db.TenantMembers.AddRange(members);
            await db.SaveChangesAsync();
        }

        // Contexto com os primeiros 50 tenants autorizados
        var allowed50 = tenants.Take(50).Select(t => t.Id).ToList();
        var macroContext = new AuthContext
        {
            ViewMode = ViewMode.MACRO_GROUP,
            GroupId = groupId,
            AllowedTenants = allowed50,
            BypassTenantFilter = false
        };

        await using (var dbMacro = _fixture.CreateDbContext(macroContext))
        {
            var result = await dbMacro.TenantMembers.AsNoTracking().ToListAsync();
            Assert.Equal(50, result.Count);
            Assert.All(result, m => Assert.Contains(m.TenantId, allowed50));
            Assert.DoesNotContain(result, m => m.TenantId == tenants[50].Id);
        }

        // Consulta com BypassTenantFilter = true retorna todos os 51
        var bypassContext = new AuthContext
        {
            BypassTenantFilter = true
        };

        await using (var dbBypass = _fixture.CreateDbContext(bypassContext))
        {
            var result = await dbBypass.TenantMembers.AsNoTracking().ToListAsync();
            Assert.Equal(51, result.Count);
        }
    }

    [Theory(Timeout = 60000)]
    [InlineData("01900000-0000-7000-8000-000000000002")] // TEN2
    [InlineData("00000000-0000-0000-0000-000000000000")] // Guid.Empty
    public async Task CA_16_3_Borda_Camada3_InterceptorBloqueiaEscritaForaDeWriteTenantsOuGuidVazio(string tenantAlvoStr)
    {
        await _fixture.CleanDatabaseAsync();

        var ten1Id = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var tenantAlvo = Guid.Parse(tenantAlvoStr);
        var now = DateTimeOffset.UtcNow;

        var authContext = new AuthContext
        {
            BypassTenantFilter = false
        };
        authContext.AuthorizeTenantWrite(ten1Id); // Apenas TEN1 autorizado para escrita

        await using var db = _fixture.CreateDbContext(authContext);

        var member = new TenantMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantAlvo,
            UserId = Guid.NewGuid(),
            IsOwner = false,
            Status = EntityStatus.ACTIVE,
            CreatedAt = now
        };

        db.TenantMembers.Add(member);

        var ex = await Assert.ThrowsAsync<ApiErrorException>(() => db.SaveChangesAsync());
        Assert.Equal("ERR-CTX-007", ex.Code);

        // Nenhuma alteração persistida na base
        await using var conn = new NpgsqlConnection(_fixture.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tenant_member WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", member.Id);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_4_Borda_Camada4_PoliticaRlsNoPostgreSqlRejeitaEscritaForaDeWriteTenants()
    {
        await _fixture.CleanDatabaseAsync();

        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var ten1Id = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var ten2Id = Guid.Parse("01900000-0000-7000-8000-000000000020");
        var now = DateTimeOffset.UtcNow;

        // Pré-requisito: criar economic_group e os dois tenants (com bypass)
        var seedContext = new AuthContext { BypassTenantFilter = true };
        await using (var db = _fixture.CreateDbContext(seedContext))
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 16.4", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten1 = new Tenant { Id = ten1Id, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten2 = new Tenant { Id = ten2Id, GroupId = groupId, Cnpj = "99000002000101", LegalName = "Tenant 2", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            db.EconomicGroups.Add(group);
            db.Tenants.AddRange(ten1, ten2);
            await db.SaveChangesAsync();
        }

        // Conexão aberta como identity_app com app.write_tenants = TEN1
        await using var conn = new NpgsqlConnection(_fixture.GetConnectionString());
        await conn.OpenAsync();

        await using (var setupCmd = conn.CreateCommand())
        {
            setupCmd.CommandText = $"SET ROLE identity_app; SELECT set_config('app.allowed_tenants', '{ten1Id}', false), set_config('app.write_tenants', '{ten1Id}', false), set_config('app.auth_flow', 'false', false);";
            await setupCmd.ExecuteNonQueryAsync();
        }

        // 1. Inserção direta para TEN2 deve ser rejeitada com 42501
        await using (var insertTen2Cmd = conn.CreateCommand())
        {
            insertTen2Cmd.CommandText = "INSERT INTO tenant_scope (id, tenant_id, scope, created_at) VALUES (@id, @tenant_id, 'BUYER', now());";
            insertTen2Cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            insertTen2Cmd.Parameters.AddWithValue("tenant_id", ten2Id);

            var ex = await Assert.ThrowsAsync<PostgresException>(() => insertTen2Cmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }

        // 2. Inserção com RETURNING para TEN1 deve ser aceite com sucesso
        var ten1ScopeId = Guid.NewGuid();
        await using (var insertTen1Cmd = conn.CreateCommand())
        {
            insertTen1Cmd.CommandText = "INSERT INTO tenant_scope (id, tenant_id, scope, created_at) VALUES (@id, @tenant_id, 'BUYER', now()) RETURNING id;";
            insertTen1Cmd.Parameters.AddWithValue("id", ten1ScopeId);
            insertTen1Cmd.Parameters.AddWithValue("tenant_id", ten1Id);

            var returnedId = (Guid)(await insertTen1Cmd.ExecuteScalarAsync())!;
            Assert.Equal(ten1ScopeId, returnedId);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_5_Borda_Camada4_AtualizacaoCruzadaAfetaZeroLinhasOuFalhaPorRls()
    {
        await _fixture.CleanDatabaseAsync();

        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var ten1Id = Guid.Parse("01900000-0000-7000-8000-000000000010");
        var ten2Id = Guid.Parse("01900000-0000-7000-8000-000000000020");
        var user1Id = Guid.Parse("01900000-0000-7000-8000-000000000101");
        var user2Id = Guid.Parse("01900000-0000-7000-8000-000000000102");
        var memberTen1Id = Guid.NewGuid();
        var memberTen2Id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var seedContext = new AuthContext { BypassTenantFilter = true };
        await using (var db = _fixture.CreateDbContext(seedContext))
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 16.5", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten1 = new Tenant { Id = ten1Id, GroupId = groupId, Cnpj = "99000001000101", LegalName = "Tenant 1", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten2 = new Tenant { Id = ten2Id, GroupId = groupId, Cnpj = "99000002000101", LegalName = "Tenant 2", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user1 = new UserAccount { Id = user1Id, Email = "u1.ca165@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "User 1", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var user2 = new UserAccount { Id = user2Id, Email = "u2.ca165@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "User 2", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member1 = new TenantMember { Id = memberTen1Id, TenantId = ten1Id, UserId = user1Id, IsOwner = false, Status = EntityStatus.ACTIVE, CreatedAt = now };
            var member2 = new TenantMember { Id = memberTen2Id, TenantId = ten2Id, UserId = user2Id, IsOwner = false, Status = EntityStatus.ACTIVE, CreatedAt = now };

            db.EconomicGroups.Add(group);
            db.Tenants.AddRange(ten1, ten2);
            db.UserAccounts.AddRange(user1, user2);
            db.TenantMembers.AddRange(member1, member2);
            await db.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(_fixture.GetConnectionString());
        await conn.OpenAsync();

        await using (var setupCmd = conn.CreateCommand())
        {
            setupCmd.CommandText = $"SET ROLE identity_app; SELECT set_config('app.allowed_tenants', '{ten1Id}', false), set_config('app.write_tenants', '{ten1Id}', false), set_config('app.auth_flow', 'false', false);";
            await setupCmd.ExecuteNonQueryAsync();
        }

        // 1. UPDATE em membro de TEN2 afeta exatamente zero linhas
        await using (var updateCrossCmd = conn.CreateCommand())
        {
            updateCrossCmd.CommandText = "UPDATE tenant_member SET is_owner = true WHERE id = @id;";
            updateCrossCmd.Parameters.AddWithValue("id", memberTen2Id);
            var rowsAffected = await updateCrossCmd.ExecuteNonQueryAsync();
            Assert.Equal(0, rowsAffected);
        }

        // 2. UPDATE alterando a coluna tenant_id para TEN2 falha com 42501 (WITH CHECK violation)
        await using (var updateColCmd = conn.CreateCommand())
        {
            updateColCmd.CommandText = "UPDATE tenant_member SET tenant_id = @newTenantId WHERE id = @id;";
            updateColCmd.Parameters.AddWithValue("newTenantId", ten2Id);
            updateColCmd.Parameters.AddWithValue("id", memberTen1Id);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => updateColCmd.ExecuteNonQueryAsync());
            Assert.Equal("42501", ex.SqlState);
        }
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_6_Borda_IF11_3_ExecucaoComBypassEReposicaoDeVariaveisNoEncerramento()
    {
        await _fixture.CleanDatabaseAsync();

        var groupId = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var ten2Id = Guid.Parse("01900000-0000-7000-8000-000000000020");
        var user2Id = Guid.Parse("01900000-0000-7000-8000-000000000102");
        var now = DateTimeOffset.UtcNow;

        var seedContext = new AuthContext { BypassTenantFilter = true };
        await using (var dbSeed = _fixture.CreateDbContext(seedContext))
        {
            var group = new EconomicGroup { Id = groupId, Name = "Grupo 16.6", Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var ten2 = new Tenant { Id = ten2Id, GroupId = groupId, Cnpj = "99000002000101", LegalName = "Tenant 2", BranchType = BranchType.HQ, GeoLat = 0, GeoLng = 0, Status = EntityStatus.ACTIVE, CreatedAt = now, UpdatedAt = now };
            var user2 = new UserAccount { Id = user2Id, Email = "u2.ca166@example.test", PasswordHash = "$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHQ$c29tZWhhc2g", Name = "User 2", Status = UserStatus.ACTIVE, PlatformRole = PlatformRole.NONE, CreatedAt = now, UpdatedAt = now };
            var member2 = new TenantMember { Id = Guid.NewGuid(), TenantId = ten2Id, UserId = user2Id, IsOwner = true, Status = EntityStatus.ACTIVE, CreatedAt = now };
            dbSeed.EconomicGroups.Add(group);
            dbSeed.Tenants.Add(ten2);
            dbSeed.UserAccounts.Add(user2);
            dbSeed.TenantMembers.Add(member2);
            await dbSeed.SaveChangesAsync();
        }

        var authContext = new AuthContext
        {
            ViewMode = ViewMode.SINGLE,
            AllowedTenants = [Guid.Parse("01900000-0000-7000-8000-000000000010")],
            BypassTenantFilter = false
        };

        var isolation = new TenantIsolation();
        await using var db = _fixture.CreateDbContext(authContext);

        // Executar com bypass
        var returnedMembers = await isolation.ExecuteWithBypassAsync(db, async ctx =>
        {
            Assert.True(ctx.TenantIsolationBypass);

            // Invocação aninhada deve lançar InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                isolation.ExecuteWithBypassAsync(ctx, inner => Task.FromResult(true)));

            return await ctx.TenantMembers.AsNoTracking().Where(m => m.TenantId == ten2Id).ToListAsync();
        });

        // A leitura cruzada retornou os dados solicitados
        Assert.Single(returnedMembers);

        // Após o término:
        Assert.False(db.TenantIsolationBypass);

        // Variável app.auth_flow tem valor "false" na sessão PostgreSQL
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT current_setting('app.auth_flow', true);";
        var authFlowVal = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal("false", authFlowVal);
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_7_Borda_IF11_3_ReposicaoSobExcecaoEmTransacaoAbortadaSuprime25P02()
    {
        await _fixture.CleanDatabaseAsync();

        var authContext = new AuthContext { BypassTenantFilter = false };
        var isolation = new TenantIsolation();
        await using var db = _fixture.CreateDbContext(authContext);

        await using var transaction = await db.Database.BeginTransactionAsync();

        // Operação que provoca erro SQL em transação abortada
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            isolation.ExecuteWithBypassAsync(db, async ctx =>
            {
                // Erro de divisão por zero que aborta a transação no PostgreSQL
                await ctx.Database.ExecuteSqlRawAsync("SELECT 1/0;");
                return true;
            }));

        // O erro lançado deve ser o original da operação (PostgresException de divisão por zero), e NÃO 25P02
        if (ex is PostgresException pgEx)
        {
            Assert.NotEqual("25P02", pgEx.SqlState);
            Assert.Equal("22012", pgEx.SqlState); // division_by_zero
        }

        // TenantIsolationBypass retorna para valor falso
        Assert.False(db.TenantIsolationBypass);

        await transaction.RollbackAsync();
    }

    [Fact(Timeout = 60000)]
    public async Task CA_16_8_Borda_ReutilizacaoDeConexaoDoPoolOperaComIsolamentoLimpo()
    {
        await _fixture.CleanDatabaseAsync();

        var isolation = new TenantIsolation();

        // 1. Conexão utilizada sob bypass
        var authContextBypass = new AuthContext { BypassTenantFilter = false };
        await using (var dbBypass = _fixture.CreateDbContext(authContextBypass))
        {
            await isolation.ExecuteWithBypassAsync(dbBypass, async ctx =>
            {
                await using var c = ctx.Database.GetDbConnection().CreateCommand();
                c.CommandText = "SELECT 1;";
                await c.ExecuteScalarAsync();
                return true;
            });
        } // Conexão devolvida ao pool

        // 2. Nova requisição sem token abre conexão do pool
        var unauthenticatedContext = new AuthContext
        {
            IsAuthenticated = false,
            BypassTenantFilter = false,
            AllowedTenants = []
        };

        await using (var dbNew = _fixture.CreateDbContext(unauthenticatedContext))
        {
            await dbNew.Database.OpenConnectionAsync();
            await using var cmd = dbNew.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT current_setting('app.auth_flow', true), current_setting('app.allowed_tenants', true), current_setting('app.write_tenants', true);";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            var authFlow = reader.GetString(0);
            var allowedTenants = reader.GetString(1);
            var writeTenants = reader.GetString(2);

            Assert.Equal("false", authFlow);
            Assert.True(string.IsNullOrEmpty(allowedTenants));
            Assert.True(string.IsNullOrEmpty(writeTenants));
        }

        // Comandos SQL contra tabelas ITenantScoped falham com 42501 ou afetam zero linhas
        await using (var dbNew = _fixture.CreateDbContext(unauthenticatedContext))
        {
            var count = await dbNew.TenantMembers.AsNoTracking().CountAsync();
            Assert.Equal(0, count);

            var ex = await Assert.ThrowsAsync<ApiErrorException>(() =>
            {
                dbNew.TenantMembers.Add(new TenantMember
                {
                    Id = Guid.NewGuid(),
                    TenantId = Guid.NewGuid(),
                    UserId = Guid.NewGuid(),
                    Status = EntityStatus.ACTIVE,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                return dbNew.SaveChangesAsync();
            });
            Assert.Equal("ERR-CTX-007", ex.Code);
        }
    }
}

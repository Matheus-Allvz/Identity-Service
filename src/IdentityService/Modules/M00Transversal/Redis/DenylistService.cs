using StackExchange.Redis;

namespace IdentityService.Modules.M00Transversal.Redis;

public class DenylistService : IDenylist
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _timeProvider;

    public DenylistService(IConnectionMultiplexer redis, TimeProvider timeProvider)
    {
        _redis = redis;
        _timeProvider = timeProvider;
    }

    private IDatabase Database => _redis.GetDatabase();

    public async Task RevokeJtiAsync(string jti, long exp, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var ttlSeconds = Math.Max(1, exp - now);
        await Database.StringSetAsync($"denylist:jti:{jti}", "1", TimeSpan.FromSeconds(ttlSeconds));
    }

    public async Task<long> BlockUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        await Database.StringSetAsync($"denylist:user:{userId:D}", now.ToString(), TimeSpan.FromSeconds(900));
        return now;
    }

    public async Task BlockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await Database.StringSetAsync($"denylist:tenant:{tenantId:D}", "1", TimeSpan.FromSeconds(900));
    }

    public async Task UnblockTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await Database.KeyDeleteAsync($"denylist:tenant:{tenantId:D}");
    }

    public async Task<long?> GetUserRevokedAtAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var val = await Database.StringGetAsync($"denylist:user:{userId:D}");
        if (val.HasValue && long.TryParse(val.ToString(), out var unixSeconds))
        {
            return unixSeconds;
        }

        return null;
    }

    public async Task<IReadOnlySet<Guid>> GetBlockedTenantsAsync(IReadOnlyList<Guid> tenantIds, CancellationToken cancellationToken = default)
    {
        if (tenantIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var keys = tenantIds.Select(id => (RedisKey)$"denylist:tenant:{id:D}").ToArray();
        var values = await Database.StringGetAsync(keys);

        var blocked = new HashSet<Guid>();
        for (var i = 0; i < keys.Length; i++)
        {
            if (values[i].HasValue)
            {
                blocked.Add(tenantIds[i]);
            }
        }

        return blocked;
    }
}

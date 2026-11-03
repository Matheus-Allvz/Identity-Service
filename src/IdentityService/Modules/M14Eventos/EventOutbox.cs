using System.Text.Json;
using System.Text.Json.Serialization;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M00Transversal.Errors;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;
using IdentityService.Modules.M14Eventos.Interface;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Modules.M14Eventos;

public class EventOutbox : IEventOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly IdentityDbContext _dbContext;
    private readonly IAuthContext _authContext;
    private readonly ISigningKey _signingKey;
    private readonly TimeProvider _timeProvider;

    public EventOutbox(
        IdentityDbContext dbContext,
        IAuthContext authContext,
        ISigningKey signingKey,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _authContext = authContext;
        _signingKey = signingKey;
        _timeProvider = timeProvider;
    }

    public Guid EnqueueTenantCreated(TenantCreatedPayload payload)
    {
        var now = _timeProvider.GetUtcNow();
        var eventId = Guid.CreateVersion7(now);

        var payloadDict = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D"),
            ["tenant_id"] = payload.TenantId.ToString("D"),
            ["group_id"] = payload.GroupId.ToString("D"),
            ["cnpj"] = payload.Cnpj,
            ["legal_name"] = payload.LegalName,
            ["branch_type"] = payload.BranchType.ToString(),
            ["geo_lat"] = payload.GeoLat,
            ["geo_lng"] = payload.GeoLng,
            ["status"] = "ACTIVE",
            ["scopes"] = payload.Scopes.Select(s => s.ToString()).ToArray(),
            ["created_at"] = payload.CreatedAt.ToString("O")
        };

        var actingScope = _authContext.ActiveScope?.ToString() ?? "NONE";
        var sortedAllowedTenants = _authContext.AllowedTenants.OrderBy(g => g).ToList();
        var initiatorUserId = _authContext.UserId ?? payload.TenantId;

        return CreateAndAddMessage(
            eventId: eventId,
            topic: "empresa_cadastrada",
            partitionKey: payload.TenantId.ToString("D"),
            payloadDict: payloadDict,
            headerTenantId: payload.TenantId,
            headerGroupId: payload.GroupId,
            initiatorUserId: initiatorUserId,
            headerActorUserId: _authContext.ActorUserId,
            actingScope: actingScope,
            jwsTenantId: payload.TenantId,
            jwsGroupId: payload.GroupId,
            viewMode: _authContext.ViewMode?.ToString() ?? "NONE",
            sortedAllowedTenants: sortedAllowedTenants,
            now: now
        );
    }

    public Guid EnqueueTenantBlocked(TenantBlockedPayload payload)
    {
        var now = _timeProvider.GetUtcNow();
        var eventId = Guid.CreateVersion7(now);

        var payloadDict = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D"),
            ["tenant_id"] = payload.TenantId.ToString("D"),
            ["group_id"] = payload.GroupId.ToString("D"),
            ["cnpj"] = payload.Cnpj,
            ["status"] = "SUSPENDED",
            ["reason"] = payload.Reason,
            ["blocked_by_user_id"] = payload.BlockedByUserId.ToString("D"),
            ["blocked_at"] = payload.BlockedAt.ToString("O")
        };

        var actingScope = _authContext.ActiveScope?.ToString() ?? "NONE";
        var sortedAllowedTenants = _authContext.AllowedTenants.OrderBy(g => g).ToList();
        var initiatorUserId = _authContext.UserId ?? payload.BlockedByUserId;

        return CreateAndAddMessage(
            eventId: eventId,
            topic: "tenant_bloqueado",
            partitionKey: payload.TenantId.ToString("D"),
            payloadDict: payloadDict,
            headerTenantId: payload.TenantId,
            headerGroupId: payload.GroupId,
            initiatorUserId: initiatorUserId,
            headerActorUserId: _authContext.ActorUserId,
            actingScope: actingScope,
            jwsTenantId: payload.TenantId,
            jwsGroupId: payload.GroupId,
            viewMode: _authContext.ViewMode?.ToString() ?? "NONE",
            sortedAllowedTenants: sortedAllowedTenants,
            now: now
        );
    }

    public Guid EnqueueUserLocked(Guid userId, DateTimeOffset lockedUntil, DateTimeOffset blockedAt)
    {
        var now = _timeProvider.GetUtcNow();
        var eventId = Guid.CreateVersion7(now);

        var payloadDict = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D"),
            ["user_id"] = userId.ToString("D"),
            ["status"] = "LOCKED",
            ["locked_until"] = lockedUntil.ToString("O"),
            ["reason"] = "Excesso de tentativas consecutivas de login com palavra-passe incorreta",
            ["blocked_at"] = blockedAt.ToString("O")
        };

        return CreateAndAddMessage(
            eventId: eventId,
            topic: "usuario_bloqueado",
            partitionKey: userId.ToString("D"),
            payloadDict: payloadDict,
            headerTenantId: null,
            headerGroupId: null,
            initiatorUserId: userId,
            headerActorUserId: null,
            actingScope: "NONE",
            jwsTenantId: null,
            jwsGroupId: null,
            viewMode: "NONE",
            sortedAllowedTenants: Array.Empty<Guid>(),
            now: now
        );
    }

    public Guid EnqueueUserSuspended(Guid userId, string reason, DateTimeOffset blockedAt)
    {
        var now = _timeProvider.GetUtcNow();
        var eventId = Guid.CreateVersion7(now);

        var payloadDict = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D"),
            ["user_id"] = userId.ToString("D"),
            ["status"] = "SUSPENDED",
            ["locked_until"] = null,
            ["reason"] = reason,
            ["blocked_at"] = blockedAt.ToString("O")
        };

        var actingScope = _authContext.ActiveScope?.ToString() ?? "NONE";
        var sortedAllowedTenants = _authContext.AllowedTenants.OrderBy(g => g).ToList();
        var initiatorUserId = _authContext.UserId ?? userId;
        var actorUserId = _authContext.ActorUserId;

        return CreateAndAddMessage(
            eventId: eventId,
            topic: "usuario_bloqueado",
            partitionKey: userId.ToString("D"),
            payloadDict: payloadDict,
            headerTenantId: _authContext.ActiveTenantId,
            headerGroupId: _authContext.GroupId,
            initiatorUserId: initiatorUserId,
            headerActorUserId: actorUserId,
            actingScope: actingScope,
            jwsTenantId: _authContext.ActiveTenantId,
            jwsGroupId: _authContext.GroupId,
            viewMode: _authContext.ViewMode?.ToString() ?? "NONE",
            sortedAllowedTenants: sortedAllowedTenants,
            now: now
        );
    }

    public Guid EnqueueSessionCompromised(SessionCompromisedPayload payload, FamilyContext family)
    {
        var now = _timeProvider.GetUtcNow();
        var eventId = Guid.CreateVersion7(now);

        var payloadDict = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D"),
            ["user_id"] = payload.UserId.ToString("D"),
            ["family_id"] = payload.FamilyId.ToString("D"),
            ["reused_refresh_token_id"] = payload.ReusedRefreshTokenId.ToString("D"),
            ["ip_address"] = payload.IpAddress,
            ["detected_at"] = payload.DetectedAt.ToString("O")
        };

        var actingScope = family.ActiveScope?.ToString() ?? "NONE";
        var sortedAllowedTenants = family.AllowedTenants.OrderBy(g => g).ToList();

        return CreateAndAddMessage(
            eventId: eventId,
            topic: "sessao_comprometida",
            partitionKey: payload.UserId.ToString("D"),
            payloadDict: payloadDict,
            headerTenantId: family.ActiveTenantId,
            headerGroupId: family.GroupId,
            initiatorUserId: family.UserId,
            headerActorUserId: null,
            actingScope: actingScope,
            jwsTenantId: family.ActiveTenantId,
            jwsGroupId: family.GroupId,
            viewMode: family.ViewMode.ToString(),
            sortedAllowedTenants: sortedAllowedTenants,
            now: now
        );
    }

    private Guid CreateAndAddMessage(
        Guid eventId,
        string topic,
        string partitionKey,
        Dictionary<string, object?> payloadDict,
        Guid? headerTenantId,
        Guid? headerGroupId,
        Guid initiatorUserId,
        Guid? headerActorUserId,
        string actingScope,
        Guid? jwsTenantId,
        Guid? jwsGroupId,
        string viewMode,
        IReadOnlyList<Guid> sortedAllowedTenants,
        DateTimeOffset now)
    {
        var correlationId = (_authContext.CorrelationId != Guid.Empty
            ? _authContext.CorrelationId
            : eventId).ToString("D");

        var iat = now.ToUnixTimeSeconds();
        var exp = iat + 300;

        var jwsClaims = new Dictionary<string, object?>
        {
            ["event_id"] = eventId.ToString("D"),
            ["tenant_id"] = jwsTenantId?.ToString("D"),
            ["group_id"] = jwsGroupId?.ToString("D"),
            ["initiator_user_id"] = initiatorUserId.ToString("D"),
            ["actor_user_id"] = headerActorUserId?.ToString("D"),
            ["acting_scope"] = actingScope,
            ["view_mode"] = viewMode,
            ["allowed_tenants"] = sortedAllowedTenants.Select(t => t.ToString("D")).ToArray(),
            ["correlation_id"] = correlationId,
            ["iat"] = iat,
            ["exp"] = exp
        };

        string contextJws;
        try
        {
            var key = _signingKey.SigningKey;
            var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
            var jwsPayloadJson = JsonSerializer.Serialize(jwsClaims, JsonOptions);
            var handler = new JsonWebTokenHandler();
            contextJws = handler.CreateToken(jwsPayloadJson, credentials);
        }
        catch (Exception ex)
        {
            throw new ApiErrorException("ERR-SYS-002", "Falha ao assinar context_jws", ex);
        }

        var headers = new Dictionary<string, string>
        {
            ["event_id"] = eventId.ToString("D")
        };

        if (headerTenantId.HasValue)
        {
            headers["tenant_id"] = headerTenantId.Value.ToString("D");
        }

        if (headerGroupId.HasValue)
        {
            headers["group_id"] = headerGroupId.Value.ToString("D");
        }

        headers["initiator_user_id"] = initiatorUserId.ToString("D");

        if (headerActorUserId.HasValue)
        {
            headers["actor_user_id"] = headerActorUserId.Value.ToString("D");
        }

        headers["acting_scope"] = actingScope;
        headers["correlation_id"] = correlationId;
        headers["context_jws"] = contextJws;

        var message = new OutboxMessage
        {
            Id = eventId,
            Topic = topic,
            PartitionKey = partitionKey,
            Headers = JsonSerializer.Serialize(headers),
            Payload = JsonSerializer.Serialize(payloadDict, PayloadOptions),
            Status = OutboxStatus.PENDING,
            RetryCount = 0,
            CreatedAt = now,
            ProcessedAt = null
        };

        _dbContext.OutboxMessages.Add(message);
        return eventId;
    }
}

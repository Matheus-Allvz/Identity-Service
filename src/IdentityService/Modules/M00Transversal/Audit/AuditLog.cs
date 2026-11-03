using System.Text.Json;
using System.Text.Json.Serialization;
using IdentityService.Modules.M00Transversal.Auth;
using IdentityService.Modules.M11Esquema.Persistence;
using IdentityService.Modules.M11Esquema.Persistence.Entities;
using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M00Transversal.Audit;

public class AuditLog : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IdentityDbContext _dbContext;
    private readonly IAuthContext _authContext;
    private readonly TimeProvider _timeProvider;

    public AuditLog(
        IdentityDbContext dbContext,
        IAuthContext authContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _authContext = authContext;
        _timeProvider = timeProvider;
    }

    public void Append(
        AuditEventType eventType,
        Guid? userId,
        Guid? actorUserId,
        Guid? tenantId,
        Guid? groupId,
        string? ticketId,
        object? metadata)
    {
        var finalActorUserId = _authContext.ActorUserId ?? actorUserId;
        var finalTicketId = _authContext.ActorUserId != null ? _authContext.TicketId : ticketId;

        var metadataJson = metadata != null
            ? JsonSerializer.Serialize(metadata, JsonOptions)
            : "{}";

        var logEntry = new AuthAuditLog
        {
            Id = Guid.CreateVersion7(_timeProvider.GetUtcNow()),
            EventType = eventType,
            UserId = userId,
            ActorUserId = finalActorUserId,
            TenantId = tenantId,
            GroupId = groupId,
            TicketId = finalTicketId,
            IpAddress = _authContext.IpAddress,
            CorrelationId = _authContext.CorrelationId,
            Metadata = metadataJson,
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _dbContext.AuthAuditLogs.Add(logEntry);
    }
}

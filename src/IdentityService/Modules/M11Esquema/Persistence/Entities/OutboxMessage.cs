using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M11Esquema.Persistence.Entities;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string PartitionKey { get; set; } = string.Empty;
    public string Headers { get; set; } = "{}";
    public string Payload { get; set; } = "{}";
    public OutboxStatus Status { get; set; } = OutboxStatus.PENDING;
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

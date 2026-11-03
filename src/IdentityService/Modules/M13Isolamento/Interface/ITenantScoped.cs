namespace IdentityService.Modules.M13Isolamento.Interface;

public interface ITenantScoped
{
    Guid TenantId { get; }
}

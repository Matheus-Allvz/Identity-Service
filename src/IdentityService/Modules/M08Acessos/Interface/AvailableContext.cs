using IdentityService.Modules.M11Esquema.Persistence.Enums;

namespace IdentityService.Modules.M08Acessos.Interface;

public record AvailableContext(
    Guid GroupId,
    string GroupName,
    Guid TenantId,
    string Cnpj,
    string LegalName,
    BranchType BranchType,
    bool IsOwner,
    bool CanViewGroup,
    IReadOnlyList<OperationalScope> AvailableScopes,
    IReadOnlyList<string> Roles);

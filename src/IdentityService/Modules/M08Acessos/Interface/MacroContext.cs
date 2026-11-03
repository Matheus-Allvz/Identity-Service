namespace IdentityService.Modules.M08Acessos.Interface;

public record MacroContext(
    IReadOnlyList<Guid> AllowedTenants,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

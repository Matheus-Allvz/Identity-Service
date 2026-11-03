namespace IdentityService.Modules.M08Acessos.Interface;

public record RoleSet(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

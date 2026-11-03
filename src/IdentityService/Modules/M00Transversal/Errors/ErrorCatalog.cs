namespace IdentityService.Modules.M00Transversal.Errors;

public static class ErrorCatalog
{
    private static readonly Dictionary<string, (int Status, string Title)> Catalog = new()
    {
        ["ERR-VAL-001"] = (400, "Pedido inválido ou malformado"),
        ["ERR-VAL-002"] = (422, "Dados de CNPJ ou geolocalização inválidos"),
        ["ERR-VAL-003"] = (422, "Formato de e-mail ou política de palavra-passe violada"),
        ["ERR-VAL-004"] = (422, "Código de chamado técnico inválido"),
        ["ERR-REQ-001"] = (404, "Rota inexistente"),
        ["ERR-REQ-002"] = (405, "Método não permitido"),
        ["ERR-REQ-003"] = (413, "Corpo do pedido demasiado grande"),
        ["ERR-REQ-004"] = (415, "Tipo de conteúdo não suportado"),
        ["ERR-AUTH-001"] = (401, "Credenciais inválidas"),
        ["ERR-AUTH-002"] = (401, "Token de acesso ausente, inválido, expirado ou revogado"),
        ["ERR-AUTH-004"] = (403, "Utilizador suspenso ou revogado globalmente"),
        ["ERR-AUTH-005"] = (401, "Refresh token inválido, revogado ou expirado"),
        ["ERR-AUTH-006"] = (401, "Reuso de refresh token detetado e sessão revogada"),
        ["ERR-AUTH-010"] = (403, "Operação de impersonation não autorizada"),
        ["ERR-CTX-001"] = (403, "Utilizador sem vínculo ativo no Tenant solicitado"),
        ["ERR-CTX-002"] = (403, "Tenant ou Grupo Económico suspenso"),
        ["ERR-CTX-003"] = (403, "Escopo operacional inativo ou papel não atribuído"),
        ["ERR-CTX-004"] = (403, "Violação das regras de agregação em visão macro"),
        ["ERR-CTX-005"] = (409, "Conflito de unicidade de registo"),
        ["ERR-CTX-006"] = (422, "Papel incompatível com os escopos ativos do Tenant"),
        ["ERR-CTX-007"] = (403, "Violação de isolamento de contexto ou permissão insuficiente"),
        ["ERR-CTX-008"] = (404, "Recurso de domínio não encontrado"),
        ["ERR-SYS-001"] = (500, "Erro interno do servidor"),
        ["ERR-SYS-002"] = (503, "Dependência indisponível"),
    };

    public static (int Status, string Title) GetInfo(string code)
    {
        if (Catalog.TryGetValue(code, out var info))
        {
            return info;
        }

        return (500, "Erro interno do servidor");
    }
}

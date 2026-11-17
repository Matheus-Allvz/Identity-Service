namespace IdentityService.Modules.M15Docs;

// QW-04: contrato OpenAPI 3.1 escrito à mão (EP-13) e Swagger UI (EP-14).
// O documento é um recurso embebido; o teste de deriva (CA-QW04.2) compara-o com as rotas reais.
public static class ModuleRegistration
{
    public const string OpenApiPath = "/openapi.json";
    public const string SwaggerPath = "/swagger";

    private const string SwaggerUiVersion = "5.17.14";

    private static readonly byte[] Document = LoadDocument();

    private static readonly string SwaggerHtml = $$"""
        <!doctype html>
        <html lang="pt">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Identity-Service B2B API</title>
          <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@{{SwaggerUiVersion}}/swagger-ui.css">
        </head>
        <body>
          <div id="swagger-ui"></div>
          <script src="https://unpkg.com/swagger-ui-dist@{{SwaggerUiVersion}}/swagger-ui-bundle.js" crossorigin></script>
          <script>
            window.ui = SwaggerUIBundle({ url: '{{OpenApiPath}}', dom_id: '#swagger-ui', persistAuthorization: true });
          </script>
        </body>
        </html>
        """;

    public static IEndpointRouteBuilder MapM15Docs(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        if (string.Equals(configuration["OPENAPI_ENABLED"], "false", StringComparison.OrdinalIgnoreCase))
        {
            return app;
        }

        app.MapGet(OpenApiPath, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Bytes(Document, "application/json; charset=utf-8");
        });

        app.MapGet(SwaggerPath, () => Results.Content(SwaggerHtml, "text/html; charset=utf-8"));

        return app;
    }

    private static byte[] LoadDocument()
    {
        using var stream = typeof(ModuleRegistration).Assembly.GetManifestResourceStream("openapi.json")
            ?? throw new InvalidOperationException("Recurso embebido openapi.json não encontrado.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

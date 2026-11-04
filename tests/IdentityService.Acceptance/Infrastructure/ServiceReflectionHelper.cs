using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Acceptance.Infrastructure;

public static class ServiceReflectionHelper
{
    public static object? ObterServicoPorNome(IServiceProvider sp, string nomeInterfaceOuTipo)
    {
        // 1. Tentar por procura em todos os tipos carregados nos assemblies
        var tipo = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == nomeInterfaceOuTipo);

        if (tipo != null)
        {
            var servico = sp.GetService(tipo);
            if (servico != null)
            {
                return servico;
            }
        }

        return null;
    }

    public static T ObterServicoObrigatorio<T>(IServiceProvider sp) where T : class
    {
        var svc = sp.GetService<T>();
        if (svc != null) return svc;

        var porNome = ObterServicoPorNome(sp, typeof(T).Name) as T;
        if (porNome != null) return porNome;

        throw new InvalidOperationException($"Serviço de tipo {typeof(T).FullName} não pôde ser resolvido do DI.");
    }

    public static async Task<object?> InvocarMetodoAsync(object instancia, string nomeMetodo, params object?[] parametros)
    {
        var metodo = instancia.GetType().GetMethod(nomeMetodo, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(instancia.GetType().FullName, nomeMetodo);

        var resultado = metodo.Invoke(instancia, parametros);
        if (resultado is Task task)
        {
            await task;
            var propResult = task.GetType().GetProperty("Result");
            return propResult?.GetValue(task);
        }

        return resultado;
    }
}

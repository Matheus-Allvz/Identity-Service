using System.Diagnostics;

namespace IdentityService.Acceptance.Infrastructure;

public static class WaitHelper
{
    public static async Task EsperarAteAsync(Func<Task<bool>> condicao, TimeSpan teto, TimeSpan? intervalo = null)
    {
        var delay = intervalo ?? TimeSpan.FromMilliseconds(100);
        var sw = Stopwatch.StartNew();

        while (sw.Elapsed < teto)
        {
            try
            {
                if (await condicao())
                {
                    return;
                }
            }
            catch
            {
                // Ignora falhas transitórias durante espera ativa
            }

            await Task.Delay(delay);
        }

        // Última verificação antes de lançar timeout
        if (await condicao())
        {
            return;
        }

        throw new TimeoutException($"Condição não atingida dentro do teto de {teto.TotalMilliseconds} ms.");
    }

    public static async Task EsperarAteAsync(Func<bool> condicao, TimeSpan teto, TimeSpan? intervalo = null)
    {
        await EsperarAteAsync(() => Task.FromResult(condicao()), teto, intervalo);
    }
}

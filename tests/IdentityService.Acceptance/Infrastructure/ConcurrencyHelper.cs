namespace IdentityService.Acceptance.Infrastructure;

public static class ConcurrencyHelper
{
    public static async Task ExecutarConcorrenteAsync(int totalTarefas, Func<int, Task> acao)
    {
        var barrier = new Barrier(totalTarefas);
        var tasks = new Task[totalTarefas];

        for (int i = 0; i < totalTarefas; i++)
        {
            var indice = i;
            tasks[i] = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                await acao(indice);
            });
        }

        await Task.WhenAll(tasks);
    }

    public static async Task ExecutarConcorrenteComRepeticoesAsync(int totalTarefas, int repeticoes, Func<int, int, Task> acao)
    {
        for (int r = 0; r < repeticoes; r++)
        {
            var repAtual = r;
            var barrier = new Barrier(totalTarefas);
            var tasks = new Task[totalTarefas];

            for (int i = 0; i < totalTarefas; i++)
            {
                var indice = i;
                tasks[i] = Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    await acao(repAtual, indice);
                });
            }

            await Task.WhenAll(tasks);
        }
    }
}

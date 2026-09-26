namespace TradutorPdf;

public interface ITradutor : IDisposable
{
    string Nome { get; }

    event Action<string>? Aviso;

    Task<List<string>> TraduzirParagrafosAsync(List<string> paragrafos, CancellationToken ct = default);

    Task VerificarCotaAsync(long caracteres, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class ServicoBloqueadoException(string servico, TimeSpan? tentarDepoisDe = null)
    : Exception($"{servico} bloqueou temporariamente por excesso de pedidos.")
{
    public TimeSpan? TentarDepoisDe { get; } = tentarDepoisDe;
}

public sealed class ServicoIndisponivelException(string mensagem) : Exception(mensagem);

internal static class Espera
{
    public static async Task ComContagemAsync(TimeSpan espera, string motivo, Action<string>? aviso, CancellationToken ct)
    {
        for (var restante = espera; restante > TimeSpan.Zero; restante -= TimeSpan.FromSeconds(1))
        {
            aviso?.Invoke($"{motivo}. Tentando de novo em {restante:m\\:ss}... (o progresso fica salvo)");
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}

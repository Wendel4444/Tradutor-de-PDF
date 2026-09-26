namespace TradutorPdf;

public sealed class TradutorRotativo : ITradutor
{
    private static readonly TimeSpan[] EsperasBloqueio =
        [.. new[] { 1, 2, 5, 10, 15 }.Select(m => TimeSpan.FromMinutes(m))];

    private sealed class Servico(ITradutor tradutor)
    {
        public ITradutor Tradutor { get; } = tradutor;
        public DateTime BloqueadoAte { get; set; } = DateTime.MinValue;
        public int BloqueiosSeguidos { get; set; }
    }

    private readonly List<Servico> _servicos;
    private ITradutor _atual;

    public string Nome => _atual.Nome;
    public event Action<string>? Aviso;

    public TradutorRotativo(IEnumerable<ITradutor> tradutores)
    {
        _servicos = tradutores.Select(t => new Servico(t)).ToList();
        if (_servicos.Count == 0) throw new ArgumentException("Nenhum serviço de tradução.", nameof(tradutores));
        _atual = _servicos[0].Tradutor;
        foreach (var s in _servicos) s.Tradutor.Aviso += msg => Aviso?.Invoke($"{s.Tradutor.Nome}: {msg}");
    }

    public async Task VerificarCotaAsync(long caracteres, CancellationToken ct = default)
    {
        foreach (var s in _servicos.ToList())
        {
            try { await s.Tradutor.VerificarCotaAsync(caracteres, ct); }
            catch (ServicoIndisponivelException ex) when (_servicos.Count > 1) { Remover(s, ex.Message); }
        }
    }

    public async Task<List<string>> TraduzirParagrafosAsync(List<string> paragrafos, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var servico = _servicos.FirstOrDefault(s => s.BloqueadoAte <= DateTime.UtcNow);
            if (servico == null)
            {
                var espera = _servicos.Min(s => s.BloqueadoAte) - DateTime.UtcNow;
                string motivo = _servicos.Count == 1
                    ? $"{_servicos[0].Tradutor.Nome} bloqueou temporariamente"
                    : "Todos os serviços bloquearam temporariamente";
                await Espera.ComContagemAsync(espera, motivo, Aviso, ct);
                continue;
            }

            _atual = servico.Tradutor;
            try
            {
                var resultado = await servico.Tradutor.TraduzirParagrafosAsync(paragrafos, ct);
                servico.BloqueiosSeguidos = 0;
                return resultado;
            }
            catch (ServicoBloqueadoException ex)
            {
                Bloquear(servico, ex.TentarDepoisDe);
            }
            catch (ServicoIndisponivelException ex)
            {
                if (_servicos.Count == 1) throw;
                Remover(servico, ex.Message);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is not OperationCanceledException)
            {
                Bloquear(servico, null);
            }
        }
    }

    private void Bloquear(Servico servico, TimeSpan? tentarDepoisDe)
    {
        var espera = EsperasBloqueio[Math.Min(servico.BloqueiosSeguidos, EsperasBloqueio.Length - 1)];
        if (tentarDepoisDe > espera) espera = tentarDepoisDe.Value;
        servico.BloqueiosSeguidos++;
        servico.BloqueadoAte = DateTime.UtcNow + espera;

        var proximo = _servicos.FirstOrDefault(s => s.BloqueadoAte <= DateTime.UtcNow);
        if (proximo != null)
            Aviso?.Invoke($"{servico.Tradutor.Nome} bloqueou. Trocando para {proximo.Tradutor.Nome}...");
    }

    private void Remover(Servico servico, string motivo)
    {
        _servicos.Remove(servico);
        servico.Tradutor.Dispose();
        _atual = _servicos[0].Tradutor;
        Aviso?.Invoke($"{motivo} Continuando com {string.Join(" e ", _servicos.Select(s => s.Tradutor.Nome))}.");
    }

    public void Dispose()
    {
        foreach (var s in _servicos) s.Tradutor.Dispose();
    }
}

using System.Net;
using System.Text;
using System.Text.Json;

namespace TradutorPdf;

public sealed class TradutorGoogle : ITradutor
{
    private const string Url = "https://translate.googleapis.com/translate_a/single";
    private const int MaxCaracteresPorLote = 4000;
    private const string Separador = "\n\n";

    private static readonly TimeSpan IntervaloMaximo = TimeSpan.FromSeconds(8);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _origem;
    private readonly string _destino;

    private TimeSpan _intervalo = TimeSpan.FromSeconds(1);
    private DateTime _ultimoPedido = DateTime.MinValue;

    public string Nome => "Google";
    public event Action<string>? Aviso;

    public TradutorGoogle(string origem, string destino)
    {
        _origem = origem;
        _destino = destino;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
    }

    public async Task<List<string>> TraduzirParagrafosAsync(List<string> paragrafos, CancellationToken ct = default)
    {
        var resultado = new List<string>();
        foreach (var lote in MontarLotes(paragrafos))
            resultado.AddRange(await TraduzirLoteAsync(lote, ct));
        return resultado;
    }

    private async Task<List<string>> TraduzirLoteAsync(List<string> lote, CancellationToken ct)
    {
        if (lote.Count == 1)
            return [(await TraduzirAsync(lote[0], ct)).Trim()];

        var partes = (await TraduzirAsync(string.Join(Separador, lote), ct))
            .Split(Separador, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == lote.Count)
            return [.. partes];

        int meio = lote.Count / 2;
        var resultado = await TraduzirLoteAsync(lote[..meio], ct);
        resultado.AddRange(await TraduzirLoteAsync(lote[meio..], ct));
        return resultado;
    }

    private static IEnumerable<List<string>> MontarLotes(List<string> paragrafos)
    {
        var lote = new List<string>();
        int tamanho = 0;
        foreach (var p in paragrafos.SelectMany(DividirTextoLongo))
        {
            if (lote.Count > 0 && tamanho + p.Length + Separador.Length > MaxCaracteresPorLote)
            {
                yield return lote;
                lote = new List<string>();
                tamanho = 0;
            }
            lote.Add(p);
            tamanho += p.Length + Separador.Length;
        }
        if (lote.Count > 0) yield return lote;
    }

    private static IEnumerable<string> DividirTextoLongo(string texto)
    {
        while (texto.Length > MaxCaracteresPorLote)
        {
            int corte = texto.LastIndexOfAny(['.', '!', '?', ';'], MaxCaracteresPorLote - 1);
            if (corte < MaxCaracteresPorLote / 2) corte = texto.LastIndexOf(' ', MaxCaracteresPorLote - 1);
            if (corte <= 0) corte = MaxCaracteresPorLote - 1;
            yield return texto[..(corte + 1)].Trim();
            texto = texto[(corte + 1)..].Trim();
        }
        if (texto.Length > 0) yield return texto;
    }

    private async Task<string> TraduzirAsync(string texto, CancellationToken ct)
    {
        var query = $"?client=gtx&sl={_origem}&tl={_destino}&dt=t";
        int falhasRede = 0;
        while (true)
        {
            await RespeitarIntervaloAsync(ct);
            HttpResponseMessage resposta;
            try
            {
                using var conteudo = new FormUrlEncodedContent([new("q", texto)]);
                resposta = await _http.PostAsync(Url + query, conteudo, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested && ++falhasRede < 6)
            {
                await Espera.ComContagemAsync(TimeSpan.FromSeconds(Math.Pow(2, falhasRede)), "Problema de conexão", Aviso, ct);
                continue;
            }

            using (resposta)
            {
                if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _intervalo = TimeSpan.FromTicks(Math.Min(_intervalo.Ticks * 2, IntervaloMaximo.Ticks));
                    throw new ServicoBloqueadoException(Nome, resposta.Headers.RetryAfter?.Delta);
                }
                if ((int)resposta.StatusCode >= 500)
                {
                    if (++falhasRede < 3)
                    {
                        await Espera.ComContagemAsync(TimeSpan.FromSeconds(5 * falhasRede), "Google instável", Aviso, ct);
                        continue;
                    }
                    throw new ServicoBloqueadoException(Nome);
                }
                resposta.EnsureSuccessStatusCode();

                using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
                var sb = new StringBuilder();
                foreach (var segmento in json.RootElement[0].EnumerateArray())
                    if (segmento[0].ValueKind == JsonValueKind.String)
                        sb.Append(segmento[0].GetString());
                return sb.ToString();
            }
        }
    }

    private async Task RespeitarIntervaloAsync(CancellationToken ct)
    {
        var falta = _ultimoPedido + _intervalo - DateTime.UtcNow;
        if (falta > TimeSpan.Zero) await Task.Delay(falta, ct);
        _ultimoPedido = DateTime.UtcNow;
    }

    public void Dispose() => _http.Dispose();
}

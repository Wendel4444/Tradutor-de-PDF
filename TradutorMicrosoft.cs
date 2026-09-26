using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TradutorPdf;

public sealed class TradutorMicrosoft : ITradutor
{
    private const string Url = "https://edge.microsoft.com/translate/translatetext";
    private const string NavegadorEdge =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/140.0.0.0 Safari/537.36 Edg/140.0.0.0";

    private const int MaxTextosPorLote = 25;
    private const int MaxCaracteresPorLote = 10_000;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _query;

    public string Nome => "Microsoft";
    public event Action<string>? Aviso;

    public TradutorMicrosoft(string origem, string destino)
    {
        static string Codigo(string c) => c == "zh-CN" ? "zh-Hans" : c;
        _query = $"?from={(origem == "auto" ? "" : Codigo(origem))}&to={Codigo(destino)}";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(NavegadorEdge);
    }

    public async Task<List<string>> TraduzirParagrafosAsync(List<string> paragrafos, CancellationToken ct = default)
    {
        var resultado = new List<string>();
        foreach (var lote in Lotes.Montar(paragrafos, MaxTextosPorLote, MaxCaracteresPorLote))
            resultado.AddRange(await TraduzirLoteAsync(lote, ct));
        return resultado;
    }

    private async Task<List<string>> TraduzirLoteAsync(List<string> lote, CancellationToken ct)
    {
        for (int tentativa = 1; ; tentativa++)
        {
            HttpResponseMessage resposta;
            try
            {
                resposta = await _http.PostAsJsonAsync(Url + _query, lote, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       && !ct.IsCancellationRequested && tentativa < 4)
            {
                await Espera.ComContagemAsync(TimeSpan.FromSeconds(5 * tentativa), "Problema de conexão", Aviso, ct);
                continue;
            }

            using (resposta)
            {
                if (resposta.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new ServicoBloqueadoException(Nome, resposta.Headers.RetryAfter?.Delta);
                if ((int)resposta.StatusCode >= 500)
                {
                    if (tentativa < 3)
                    {
                        await Espera.ComContagemAsync(TimeSpan.FromSeconds(5 * tentativa), "Microsoft instável", Aviso, ct);
                        continue;
                    }
                    throw new ServicoBloqueadoException(Nome);
                }
                resposta.EnsureSuccessStatusCode();

                var json = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
                var traducoes = json.EnumerateArray()
                    .Select(item => item.GetProperty("translations")[0].GetProperty("text").GetString()!.Trim())
                    .ToList();
                if (traducoes.Count != lote.Count)
                    throw new ServicoBloqueadoException(Nome);
                return traducoes;
            }
        }
    }

    public void Dispose() => _http.Dispose();
}

internal static class Lotes
{
    public static IEnumerable<List<string>> Montar(List<string> paragrafos, int maxTextos, int maxCaracteres)
    {
        var lote = new List<string>();
        int tamanho = 0;
        foreach (var p in paragrafos)
        {
            if (lote.Count > 0 && (lote.Count == maxTextos || tamanho + p.Length > maxCaracteres))
            {
                yield return lote;
                lote = new List<string>();
                tamanho = 0;
            }
            lote.Add(p);
            tamanho += p.Length;
        }
        if (lote.Count > 0) yield return lote;
    }
}

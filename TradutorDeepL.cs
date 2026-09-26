using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TradutorPdf;

public sealed class TradutorDeepL : ITradutor
{
    public const string PaginaCadastro = "https://www.deepl.com/pro-api";

    private const int MaxTextosPorLote = 50;
    private const int MaxCaracteresPorLote = 30_000;

    private readonly HttpClient _http;
    private readonly string? _origem;
    private readonly string _destino;

    public string Nome => "DeepL";
    public event Action<string>? Aviso;

    public TradutorDeepL(string chave, string origem, string destino)
    {
        _http = CriarCliente(chave);
        _origem = CodigoOrigem(origem);
        _destino = CodigoDestino(destino);
    }

    private static HttpClient CriarCliente(string chave)
    {
        chave = chave.Trim();
        var http = new HttpClient
        {
            BaseAddress = new Uri(chave.EndsWith(":fx") ? "https://api-free.deepl.com" : "https://api.deepl.com"),
            Timeout = TimeSpan.FromSeconds(90),
        };
        http.DefaultRequestHeaders.Authorization = new("DeepL-Auth-Key", chave);
        return http;
    }

    private static string CodigoDestino(string codigo) => codigo switch
    {
        "pt" => "PT-BR",
        "en" => "EN-US",
        "zh-CN" => "ZH-HANS",
        _ => codigo.ToUpperInvariant(),
    };

    private static string? CodigoOrigem(string codigo) => codigo switch
    {
        "auto" => null,
        "zh-CN" => "ZH",
        _ => codigo.ToUpperInvariant(),
    };

    public static async Task<(long Usado, long Limite)> ConsultarUsoAsync(string chave, CancellationToken ct = default)
    {
        using var http = CriarCliente(chave);
        using var resposta = await http.GetAsync("/v2/usage", ct);
        await VerificarErroAsync(resposta, ct);
        var uso = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (uso.GetProperty("character_count").GetInt64(), uso.GetProperty("character_limit").GetInt64());
    }

    public async Task VerificarCotaAsync(long caracteres, CancellationToken ct = default)
    {
        using var resposta = await _http.GetAsync("/v2/usage", ct);
        await VerificarErroAsync(resposta, ct);
        var uso = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        long restante = uso.GetProperty("character_limit").GetInt64() - uso.GetProperty("character_count").GetInt64();

        if (restante <= 0)
            throw new ServicoIndisponivelException("A cota grátis do DeepL deste mês já acabou.");
        if (caracteres > restante)
            Aviso?.Invoke($"A cota do DeepL ({restante:N0} caracteres) não dá para o livro inteiro " +
                          $"({caracteres:N0}). Vai traduzir até onde der; o resto fica salvo para depois.");
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
        var corpo = new Dictionary<string, object> { ["text"] = lote, ["target_lang"] = _destino };
        if (_origem != null) corpo["source_lang"] = _origem;

        for (int tentativa = 1; ; tentativa++)
        {
            HttpResponseMessage resposta;
            try
            {
                resposta = await _http.PostAsJsonAsync("/v2/translate", corpo, ct);
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
                        await Espera.ComContagemAsync(TimeSpan.FromSeconds(10 * tentativa), "DeepL instável", Aviso, ct);
                        continue;
                    }
                    throw new ServicoBloqueadoException(Nome);
                }
                await VerificarErroAsync(resposta, ct);

                var json = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
                return json.GetProperty("translations").EnumerateArray()
                    .Select(t => t.GetProperty("text").GetString()!.Trim())
                    .ToList();
            }
        }
    }

    private static async Task VerificarErroAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        if (resposta.IsSuccessStatusCode) return;

        string mensagem = (int)resposta.StatusCode switch
        {
            401 or 403 => "Chave do DeepL inválida. Confira se copiou a chave inteira (a grátis termina em \":fx\").",
            456 => "Acabou a cota grátis do DeepL deste mês (500 mil caracteres).",
            429 => "O DeepL está recebendo pedidos demais. Tente de novo em alguns minutos.",
            _ => $"O DeepL respondeu com erro {(int)resposta.StatusCode}: {await resposta.Content.ReadAsStringAsync(ct)}",
        };
        throw new ServicoIndisponivelException(mensagem);
    }

    public void Dispose() => _http.Dispose();
}

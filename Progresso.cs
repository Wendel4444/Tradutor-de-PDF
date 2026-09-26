using System.Text.Json;

namespace TradutorPdf;

public sealed class Progresso
{
    public Dictionary<int, List<string>> Paginas { get; set; } = new();

    public static Progresso Carregar(string caminho)
    {
        if (!File.Exists(caminho)) return new Progresso();
        try { return JsonSerializer.Deserialize<Progresso>(File.ReadAllText(caminho)) ?? new Progresso(); }
        catch (JsonException) { return new Progresso(); }
    }

    public void Salvar(string caminho) => File.WriteAllText(caminho, JsonSerializer.Serialize(this));
}

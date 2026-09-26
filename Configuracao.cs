using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TradutorPdf;

public sealed class Configuracao
{
    private static readonly string Caminho = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TradutorPdf", "config.json");

    public string Servico { get; set; } = "auto";
    public string Origem { get; set; } = "auto";
    public string Destino { get; set; } = "pt";
    public string? ChaveDeepLProtegida { get; set; }

    public string ChaveDeepL
    {
        get
        {
            if (string.IsNullOrEmpty(ChaveDeepLProtegida)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(ChaveDeepLProtegida), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return "";
            }
        }
        set => ChaveDeepLProtegida = string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public static Configuracao Carregar()
    {
        try
        {
            if (File.Exists(Caminho))
                return JsonSerializer.Deserialize<Configuracao>(File.ReadAllText(Caminho)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return new();
    }

    public void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Caminho)!);
            File.WriteAllText(Caminho, JsonSerializer.Serialize(this));
        }
        catch (IOException) { }
    }
}

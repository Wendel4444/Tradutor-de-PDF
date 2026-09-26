using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace TradutorPdf;

public abstract record Elemento;

public sealed record Texto(string Conteudo) : Elemento;

public sealed record Imagem(byte[] Dados, double LarguraRelativa, double Proporcao) : Elemento;

public static class ExtratorPdf
{
    public static List<List<Elemento>> Extrair(string caminho, bool copiarFiguras, CancellationToken ct = default)
    {
        var resultado = new List<List<Elemento>>();
        using var documento = PdfDocument.Open(caminho);
        byte[]? pdfOriginal = copiarFiguras ? File.ReadAllBytes(caminho) : null;

        foreach (var pagina in documento.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            var elementos = new List<(PdfRectangle Caixa, Elemento Elemento)>();
            try
            {
                var palavras = NearestNeighbourWordExtractor.Instance.GetWords(pagina.Letters);
                var blocos = DocstrumBoundingBoxes.Instance.GetBlocks(palavras);
                var ordenados = UnsupervisedReadingOrderDetector.Instance.Get(blocos);

                foreach (var bloco in ordenados)
                {
                    var linhas = bloco.TextLines.Select(l => l.Text.Trim()).Where(l => l.Length > 0);
                    if (CriarTexto(JuntarLinhas(linhas)) is { } texto)
                        elementos.Add((bloco.BoundingBox, texto));
                }
            }
            catch
            {
                elementos.Clear();
                if (CriarTexto(pagina.Text) is { } texto)
                    elementos.Add((new PdfRectangle(0, pagina.Height, pagina.Width, pagina.Height), texto));
            }

            if (pdfOriginal != null)
            {
                try { CopiarFiguras(pdfOriginal, pagina, elementos); }
                catch { }
            }

            resultado.Add(elementos.Select(e => e.Elemento).ToList());
        }
        return resultado;
    }

    private static void CopiarFiguras(byte[] pdfOriginal, UglyToad.PdfPig.Content.Page pagina,
        List<(PdfRectangle Caixa, Elemento Elemento)> elementos)
    {
        var caixasTexto = elementos.Where(e => e.Elemento is Texto)
            .Select(e => (e.Caixa, ((Texto)e.Elemento).Conteudo)).ToList();

        var figuras = DetectorFiguras.Detectar(pagina, caixasTexto);
        if (figuras.Count == 0) return;

        var imagens = RenderizadorPagina.Recortar(pdfOriginal, pagina, figuras);
        for (int f = 0; f < figuras.Count; f++)
        {
            if (imagens[f] is not { } png) continue;
            var area = figuras[f];

            elementos.RemoveAll(e => e.Elemento is Texto t && DetectorFiguras.EhRotulo(e.Caixa, t.Conteudo, area));

            var imagem = new Imagem(png, Math.Min(1, area.Width / pagina.Width), area.Height / area.Width);

            int indice = elementos.FindLastIndex(e => e.Caixa.Bottom >= area.Top - 1) + 1;
            elementos.Insert(indice, (area, imagem));
        }
    }

    private static Texto? CriarTexto(string texto)
    {
        texto = Regex.Replace(texto, @"\s+", " ").Trim();
        return texto.Length > 0 ? new Texto(texto) : null;
    }

    private static string JuntarLinhas(IEnumerable<string> linhas)
    {
        var sb = new StringBuilder();
        foreach (var linha in linhas)
        {
            if (sb.Length > 0 && sb[^1] == '-' && sb.Length > 1 && char.IsLetter(sb[^2]) && char.IsLower(linha[0]))
                sb.Length--;
            else if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(linha);
        }
        return sb.ToString();
    }
}

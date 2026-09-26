using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace TradutorPdf;

public static class DetectorFiguras
{
    private const double Proximidade = 10;
    private const double LarguraMinima = 60;
    private const double AlturaMinima = 40;
    private const int FormasMinimas = 4;
    private const double AreaMaximaFundo = 0.8;
    private const double MargemRecorte = 4;

    private const int CaracteresTextoCorrido = 400;
    private const int MediaCaracteresTextoCorrido = 80;

    private sealed class Grupo(PdfRectangle caixa, int formas, bool temImagem)
    {
        public PdfRectangle Caixa = caixa;
        public int Formas = formas;
        public bool TemImagem = temImagem;
        public bool TemFormaCheia;
    }

    public static List<PdfRectangle> Detectar(Page pagina, List<(PdfRectangle Caixa, string Conteudo)> textos)
    {
        double areaPagina = pagina.Width * pagina.Height;
        bool FundoDaPagina(PdfRectangle r) => r.Width * r.Height > areaPagina * AreaMaximaFundo;

        var paragrafos = textos.Where(t => t.Conteudo.Length >= MediaCaracteresTextoCorrido).Select(t => t.Caixa).ToList();

        var grupos = new List<Grupo>();
        var jaVistas = new HashSet<(int, int, int, int)>();
        foreach (var caminho in pagina.Paths)
        {
            if (caminho.IsClipping || caminho.GetBoundingRectangle() is not { } r || FundoDaPagina(r)) continue;
            if (r.Width < 1 && r.Height < 1) continue;
            if (!jaVistas.Add(((int)r.Left, (int)r.Bottom, (int)r.Right, (int)r.Top))) continue;
            if (paragrafos.Any(p => EstaDentro(p, Expandir(r, 2)))) continue;

            bool linhaFina = r.Width <= 3 || r.Height <= 3;
            grupos.Add(new Grupo(r, 1, false) { TemFormaCheia = !linhaFina });
        }
        foreach (var img in pagina.GetImages())
        {
            var r = img.BoundingBox;
            if (r.Width < 20 || r.Height < 20 || (FundoDaPagina(r) && textos.Count > 0)) continue;
            grupos.Add(new Grupo(r, 1, true));
        }

        Agrupar(grupos);

        var figuras = new List<PdfRectangle>();
        foreach (var g in grupos)
        {
            if (!g.TemImagem && (g.Formas < FormasMinimas || !g.TemFormaCheia)) continue;

            var caixa = g.Caixa;
            foreach (var t in textos)
                if (t.Conteudo.Length < MediaCaracteresTextoCorrido && EstaDentro(t.Caixa, Expandir(g.Caixa, Proximidade)))
                    caixa = Unir(caixa, t.Caixa);

            if (caixa.Width < LarguraMinima || caixa.Height < AlturaMinima) continue;
            var dentro = textos.Where(t => EstaDentro(t.Caixa, caixa)).Select(t => t.Conteudo).ToList();
            if (!g.TemImagem && EhTextoCorrido(dentro)) continue;

            figuras.Add(Limitar(Expandir(caixa, MargemRecorte), pagina));
        }

        var finais = figuras.Select(f => new Grupo(f, 1, true)).ToList();
        Agrupar(finais, proximidade: 0);
        return finais.Select(g => g.Caixa).ToList();
    }

    public static bool EhRotulo(PdfRectangle texto, string conteudo, PdfRectangle area)
    {
        if (EstaDentro(texto, area)) return true;
        if (conteudo.Length >= MediaCaracteresTextoCorrido) return false;
        var centro = texto.Centroid;
        return centro.X >= area.Left && centro.X <= area.Right && centro.Y >= area.Bottom && centro.Y <= area.Top;
    }

    public static bool EstaDentro(PdfRectangle texto, PdfRectangle area)
    {
        double largura = Math.Min(texto.Right, area.Right) - Math.Max(texto.Left, area.Left);
        double altura = Math.Min(texto.Top, area.Top) - Math.Max(texto.Bottom, area.Bottom);
        if (largura <= 0 || altura <= 0) return false;
        return largura * altura >= 0.6 * texto.Width * texto.Height;
    }

    private static bool EhTextoCorrido(List<string> textos) =>
        textos.Count > 0
        && textos.Sum(t => t.Length) > CaracteresTextoCorrido
        && textos.Average(t => t.Length) > MediaCaracteresTextoCorrido;

    private static void Agrupar(List<Grupo> grupos, double proximidade = Proximidade)
    {
        bool juntou;
        do
        {
            juntou = false;
            for (int i = 0; i < grupos.Count; i++)
            {
                var expandida = Expandir(grupos[i].Caixa, proximidade);
                for (int j = grupos.Count - 1; j > i; j--)
                {
                    if (!SeTocam(expandida, grupos[j].Caixa)) continue;
                    grupos[i].Caixa = Unir(grupos[i].Caixa, grupos[j].Caixa);
                    grupos[i].Formas += grupos[j].Formas;
                    grupos[i].TemImagem |= grupos[j].TemImagem;
                    grupos[i].TemFormaCheia |= grupos[j].TemFormaCheia;
                    grupos.RemoveAt(j);
                    expandida = Expandir(grupos[i].Caixa, proximidade);
                    juntou = true;
                }
            }
        } while (juntou);
    }

    private static bool SeTocam(PdfRectangle a, PdfRectangle b) =>
        a.Left <= b.Right && b.Left <= a.Right && a.Bottom <= b.Top && b.Bottom <= a.Top;

    private static PdfRectangle Unir(PdfRectangle a, PdfRectangle b) => new(
        Math.Min(a.Left, b.Left), Math.Min(a.Bottom, b.Bottom), Math.Max(a.Right, b.Right), Math.Max(a.Top, b.Top));

    private static PdfRectangle Expandir(PdfRectangle r, double m) => new(r.Left - m, r.Bottom - m, r.Right + m, r.Top + m);

    private static PdfRectangle Limitar(PdfRectangle r, Page pagina)
    {
        var limite = pagina.CropBox.Bounds;
        return new(Math.Max(r.Left, limite.Left), Math.Max(r.Bottom, limite.Bottom),
                   Math.Min(r.Right, limite.Right), Math.Min(r.Top, limite.Top));
    }
}

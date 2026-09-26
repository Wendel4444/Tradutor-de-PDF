using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TradutorPdf;

public static class GeradorPdf
{
    private const float Margem = 2.5f * 28.35f;
    private static readonly float LarguraUtil = PageSizes.A4.Width - 2 * Margem;
    private const float AlturaMaximaImagem = 560;

    private static readonly string[] Fontes =
        ["Lato", "Segoe UI", "Segoe UI Symbol", "Segoe UI Emoji", "Microsoft YaHei", "Yu Gothic", "Malgun Gothic"];

    public static void Gerar(string caminhoSaida, string titulo, List<List<Elemento>> paginas,
        IReadOnlySet<int>? naoTraduzidas = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(Margem);
                page.DefaultTextStyle(t => t.FontSize(11).LineHeight(1.4f).FontFamily(Fontes));

                page.Header().PaddingBottom(10).Text(titulo).FontSize(9).FontColor(Colors.Grey.Medium);

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    for (int i = 0; i < paginas.Count; i++)
                    {
                        if (naoTraduzidas?.Contains(i) == true)
                            col.Item().AlignCenter().Text($"— página original {i + 1} (ainda não traduzida) —")
                                .FontSize(8).FontColor(Colors.Orange.Darken2);
                        else
                            col.Item().AlignCenter().Text($"— página original {i + 1} —")
                                .FontSize(8).FontColor(Colors.Grey.Lighten1);

                        foreach (var elemento in paginas[i])
                        {
                            if (elemento is Texto texto)
                                col.Item().Text(texto.Conteudo).Justify();
                            else if (elemento is Imagem imagem)
                                AdicionarImagem(col, imagem);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Medium));
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(caminhoSaida);
    }

    private static void AdicionarImagem(ColumnDescriptor col, Imagem imagem)
    {
        QuestPDF.Infrastructure.Image img;
        try { img = QuestPDF.Infrastructure.Image.FromBinaryData(imagem.Dados); }
        catch { return; }

        float largura = (float)(imagem.LarguraRelativa * LarguraUtil);
        float altura = (float)(largura * imagem.Proporcao);
        if (altura > AlturaMaximaImagem)
        {
            largura *= AlturaMaximaImagem / altura;
            altura = AlturaMaximaImagem;
        }

        col.Item().AlignCenter().Width(largura).Height(altura).Image(img).FitArea();
    }
}

namespace TradutorPdf;

public sealed record Andamento(int Porcentagem, string Mensagem);

public static class TraducaoPdf
{
    public static string CaminhoSaida(string caminho, string destino, bool parcial = false) => Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(caminho))!,
        $"{Path.GetFileNameWithoutExtension(caminho)} ({destino}{(parcial ? ", parcial" : "")}).pdf");

    private static string CaminhoProgresso(string caminho, string destino) => CaminhoSaida(caminho, destino) + ".progresso.json";

    public static int PaginasSalvas(string caminho, string destino) =>
        Progresso.Carregar(CaminhoProgresso(caminho, destino)).Paginas.Count;

    public static async Task<string> TraduzirAsync(
        string caminho, string destino, bool incluirImagens, ITradutor tradutor,
        IProgress<Andamento> andamento, CancellationToken ct)
    {
        string saida = CaminhoSaida(caminho, destino);
        string arquivoProgresso = CaminhoProgresso(caminho, destino);

        var paginas = await LerAsync(caminho, incluirImagens, andamento, ct);
        var progresso = Progresso.Carregar(arquivoProgresso);
        tradutor.Aviso += msg => andamento.Report(new(-1, msg));

        long caracteresFaltando = paginas
            .Where((p, i) => !TraducaoSalva(progresso, paginas, i, out _))
            .Sum(p => p.OfType<Texto>().Sum(t => (long)t.Conteudo.Length));
        andamento.Report(new(0, "Conferindo o serviço de tradução..."));
        await tradutor.VerificarCotaAsync(caracteresFaltando, ct);

        for (int i = 0; i < paginas.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!TraducaoSalva(progresso, paginas, i, out var traducao))
            {
                var textos = Textos(paginas[i]);
                traducao = textos.Count == 0 ? [] : await tradutor.TraduzirParagrafosAsync(textos, ct);
                progresso.Paginas[i] = traducao;
                progresso.Salvar(arquivoProgresso);
            }

            paginas[i] = TrocarTextos(paginas[i], traducao);
            andamento.Report(new((i + 1) * 95 / paginas.Count,
                $"Traduzindo página {i + 1} de {paginas.Count} (via {tradutor.Nome})..."));
        }

        andamento.Report(new(95, "Gerando o PDF..."));
        await Task.Run(() => GeradorPdf.Gerar(saida, Path.GetFileNameWithoutExtension(caminho), paginas), ct);
        File.Delete(arquivoProgresso);

        andamento.Report(new(100, "Pronto!"));
        return saida;
    }

    public static async Task<string> GerarParcialAsync(
        string caminho, string destino, bool incluirImagens, IProgress<Andamento> andamento, CancellationToken ct)
    {
        string saida = CaminhoSaida(caminho, destino, parcial: true);
        var paginas = await LerAsync(caminho, incluirImagens, andamento, ct);
        var progresso = Progresso.Carregar(CaminhoProgresso(caminho, destino));

        int traduzidas = 0;
        var naoTraduzidas = new HashSet<int>();
        for (int i = 0; i < paginas.Count; i++)
        {
            if (TraducaoSalva(progresso, paginas, i, out var traducao))
            {
                paginas[i] = TrocarTextos(paginas[i], traducao);
                traduzidas++;
            }
            else if (Textos(paginas[i]).Count > 0)
            {
                naoTraduzidas.Add(i);
            }
        }

        andamento.Report(new(50, "Gerando o PDF parcial..."));
        await Task.Run(() => GeradorPdf.Gerar(saida, Path.GetFileNameWithoutExtension(caminho), paginas, naoTraduzidas), ct);

        andamento.Report(new(100, $"{traduzidas} de {paginas.Count} páginas traduzidas."));
        return saida;
    }

    private static async Task<List<List<Elemento>>> LerAsync(
        string caminho, bool incluirImagens, IProgress<Andamento> andamento, CancellationToken ct)
    {
        andamento.Report(new(0, "Lendo o PDF..."));
        var paginas = await Task.Run(() => ExtratorPdf.Extrair(caminho, incluirImagens, ct), ct);

        if (!paginas.Any(p => p.OfType<Texto>().Any()))
            throw new InvalidOperationException(
                "Nenhum texto encontrado. O PDF provavelmente é escaneado (imagem) e precisaria de OCR.");
        return paginas;
    }

    private static List<string> Textos(List<Elemento> pagina) =>
        pagina.OfType<Texto>().Select(t => t.Conteudo).ToList();

    private static bool TraducaoSalva(Progresso progresso, List<List<Elemento>> paginas, int i, out List<string> traducao) =>
        progresso.Paginas.TryGetValue(i, out traducao!) && traducao.Count == paginas[i].OfType<Texto>().Count();

    private static List<Elemento> TrocarTextos(List<Elemento> pagina, List<string> traducao)
    {
        int t = 0;
        return pagina.Select(e => e is Texto ? new Texto(traducao[t++]) : e).ToList();
    }
}

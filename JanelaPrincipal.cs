using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace TradutorPdf;

public sealed class JanelaPrincipal : Form
{
    private static readonly (string Codigo, string Nome)[] Idiomas =
    [
        ("pt", "Português"), ("en", "Inglês"), ("es", "Espanhol"), ("fr", "Francês"),
        ("de", "Alemão"), ("it", "Italiano"), ("nl", "Holandês"), ("pl", "Polonês"),
        ("ru", "Russo"), ("tr", "Turco"), ("ar", "Árabe"), ("ja", "Japonês"),
        ("ko", "Coreano"), ("zh-CN", "Chinês (simplificado)"),
    ];

    private static readonly Color Destaque = Color.FromArgb(37, 99, 235);
    private static readonly Color Cinza = Color.FromArgb(100, 116, 139);

    private readonly Panel _areaArquivo = new();
    private readonly Label _textoArquivo = new();
    private readonly ComboBox _origem = new();
    private readonly ComboBox _destino = new();
    private readonly ComboBox _servico = new();
    private readonly TableLayoutPanel _painelDeepL = new();
    private readonly TextBox _chaveDeepL = new();
    private readonly Button _testarChave = new();
    private readonly Label _usoDeepL = new();
    private readonly CheckBox _incluirImagens = new();
    private readonly Button _traduzir = new();
    private readonly Button _cancelar = new();
    private readonly ProgressBar _barra = new();
    private readonly Label _status = new();
    private readonly Button _abrirPdf = new();
    private readonly Button _abrirPasta = new();
    private readonly Button _gerarParcial = new();

    private readonly Configuracao _config = Configuracao.Carregar();
    private string? _arquivo;
    private string? _resultado;
    private CancellationTokenSource? _cancelamento;
    private bool _arrastando;

    public JanelaPrincipal()
    {
        Text = "Tradutor de PDF";
        Icon = new Icon(Recurso("icone.ico"));
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.White;
        ClientSize = new Size(560, 680);
        MinimumSize = new Size(480, 700);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        MontarTela();

        DragEnter += (_, e) => AoArrastar(e);
        DragLeave += (_, _) => DefinirArrastando(false);
        DragDrop += (_, e) => AoSoltar(e);
        FormClosing += (_, _) =>
        {
            _cancelamento?.Cancel();
            SalvarConfiguracao();
        };
    }

    private static Stream Recurso(string nome) =>
        typeof(JanelaPrincipal).Assembly.GetManifestResourceStream(nome)!;

    private void SalvarConfiguracao()
    {
        _config.Servico = (string)_servico.SelectedValue!;
        _config.Origem = (string)_origem.SelectedValue!;
        _config.Destino = (string)_destino.SelectedValue!;
        _config.ChaveDeepL = _chaveDeepL.Text;
        _config.Salvar();
    }

    private void MontarTela()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20),
            ColumnCount = 2,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var cabecalho = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 16),
        };
        var logo = new PictureBox
        {
            Image = Image.FromStream(Recurso("logo.png")),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(60, 60),
            Margin = new Padding(0, 0, 14, 0),
        };
        var titulo = new Label
        {
            Text = "Tradutor de PDF",
            Font = new Font("Segoe UI Semibold", 16f),
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
            Margin = new Padding(0, 4, 0, 0),
        };
        var subtitulo = new Label
        {
            Text = "Livros, artigos, apostilas: qualquer PDF com texto.",
            ForeColor = Cinza,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(2, 0, 0, 0),
        };
        cabecalho.Controls.Add(logo, 0, 0);
        cabecalho.SetRowSpan(logo, 2);
        cabecalho.Controls.Add(titulo, 1, 0);
        cabecalho.Controls.Add(subtitulo, 1, 1);
        cabecalho.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        cabecalho.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(cabecalho, 0, 0);
        layout.SetColumnSpan(cabecalho, 2);

        _areaArquivo.Dock = DockStyle.Fill;
        _areaArquivo.Height = 130;
        _areaArquivo.Cursor = Cursors.Hand;
        _areaArquivo.Margin = new Padding(0, 0, 0, 16);
        _areaArquivo.Paint += DesenharAreaArquivo;
        _areaArquivo.Click += (_, _) => EscolherArquivo();

        _textoArquivo.Dock = DockStyle.Fill;
        _textoArquivo.TextAlign = ContentAlignment.MiddleCenter;
        _textoArquivo.ForeColor = Cinza;
        _textoArquivo.BackColor = Color.Transparent;
        _textoArquivo.Text = "Arraste o PDF aqui\nou clique para escolher";
        _textoArquivo.Click += (_, _) => EscolherArquivo();
        _areaArquivo.Controls.Add(_textoArquivo);

        layout.Controls.Add(_areaArquivo, 0, 2);
        layout.SetColumnSpan(_areaArquivo, 2);

        layout.Controls.Add(Rotulo("Traduzir de"), 0, 3);
        layout.Controls.Add(Rotulo("Para"), 1, 3);

        ConfigurarCombo(_origem, Idiomas.Prepend(("auto", "Detectar automaticamente")), _config.Origem);
        ConfigurarCombo(_destino, Idiomas, _config.Destino);
        _origem.Margin = new Padding(0, 0, 8, 12);
        _destino.Margin = new Padding(8, 0, 0, 12);
        layout.Controls.Add(_origem, 0, 4);
        layout.Controls.Add(_destino, 1, 4);

        var rotuloServico = Rotulo("Serviço de tradução");
        layout.Controls.Add(rotuloServico, 0, 5);
        layout.SetColumnSpan(rotuloServico, 2);

        ConfigurarCombo(_servico,
            [
                ("auto", "Automático: alterna entre os serviços até terminar (recomendado)"),
                ("google", "Só Google Tradutor (grátis, sem cadastro)"),
                ("microsoft", "Só Microsoft Tradutor (grátis, sem cadastro)"),
                ("deepl", "Só DeepL (melhor qualidade, precisa de chave)"),
            ],
            _config.Servico);
        _servico.Margin = new Padding(0, 0, 0, 8);
        _servico.SelectedIndexChanged += (_, _) => AtualizarPainelDeepL();
        layout.Controls.Add(_servico, 0, 6);
        layout.SetColumnSpan(_servico, 2);

        MontarPainelDeepL();
        layout.Controls.Add(_painelDeepL, 0, 7);
        layout.SetColumnSpan(_painelDeepL, 2);

        _incluirImagens.Text = "Copiar imagens e gráficos do original (sem traduzir o que está dentro)";
        _incluirImagens.Checked = true;
        _incluirImagens.AutoSize = true;
        _incluirImagens.Margin = new Padding(0, 4, 0, 16);
        layout.Controls.Add(_incluirImagens, 0, 8);
        layout.SetColumnSpan(_incluirImagens, 2);

        EstilizarBotao(_traduzir, "Traduzir", primario: true);
        _traduzir.Enabled = false;
        _traduzir.Click += async (_, _) => await TraduzirAsync();
        _traduzir.Margin = new Padding(0, 0, 8, 12);

        EstilizarBotao(_cancelar, "Cancelar", primario: false);
        _cancelar.Enabled = false;
        _cancelar.Click += (_, _) => _cancelamento?.Cancel();
        _cancelar.Margin = new Padding(8, 0, 0, 12);

        layout.Controls.Add(_traduzir, 0, 9);
        layout.Controls.Add(_cancelar, 1, 9);

        _barra.Dock = DockStyle.Fill;
        _barra.Height = 8;
        _barra.Margin = new Padding(0, 4, 0, 6);
        layout.Controls.Add(_barra, 0, 10);
        layout.SetColumnSpan(_barra, 2);

        _status.AutoSize = true;
        _status.ForeColor = Cinza;
        _status.MaximumSize = new Size(500, 0);
        _status.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(_status, 0, 11);
        layout.SetColumnSpan(_status, 2);

        EstilizarBotao(_abrirPdf, "Abrir PDF traduzido", primario: true);
        _abrirPdf.Click += (_, _) => Abrir(_resultado!);
        _abrirPdf.Margin = new Padding(0, 0, 8, 0);
        _abrirPdf.Visible = false;

        EstilizarBotao(_abrirPasta, "Mostrar na pasta", primario: false);
        _abrirPasta.Click += (_, _) => Process.Start("explorer.exe", $"/select,\"{_resultado}\"");
        _abrirPasta.Margin = new Padding(8, 0, 0, 0);
        _abrirPasta.Visible = false;

        layout.Controls.Add(_abrirPdf, 0, 12);
        layout.Controls.Add(_abrirPasta, 1, 12);

        EstilizarBotao(_gerarParcial, "", primario: false);
        _gerarParcial.Click += async (_, _) => await GerarParcialAsync();
        _gerarParcial.Margin = new Padding(0, 8, 0, 0);
        _gerarParcial.Visible = false;
        layout.Controls.Add(_gerarParcial, 0, 13);
        layout.SetColumnSpan(_gerarParcial, 2);

        _destino.SelectedIndexChanged += (_, _) => AtualizarProgressoSalvo();

        Controls.Add(layout);
        AtualizarPainelDeepL();
    }

    private int AtualizarProgressoSalvo()
    {
        int salvas = _arquivo == null || _cancelamento != null
            ? 0
            : TraducaoPdf.PaginasSalvas(_arquivo, (string)_destino.SelectedValue!);
        _gerarParcial.Text = $"Gerar PDF com as {salvas} páginas já traduzidas";
        _gerarParcial.Visible = salvas > 0;
        return salvas;
    }

    private async Task GerarParcialAsync()
    {
        if (_arquivo == null) return;

        _cancelamento = new CancellationTokenSource();
        DefinirTraduzindo(true);
        try
        {
            _resultado = await TraducaoPdf.GerarParcialAsync(
                _arquivo, (string)_destino.SelectedValue!, _incluirImagens.Checked, CriarAndamento(), _cancelamento.Token);

            _status.ForeColor = Color.FromArgb(22, 163, 74);
            _status.Text += $" Salvo como \"{Path.GetFileName(_resultado)}\". " +
                            "Clique em Traduzir quando quiser terminar o resto.";
            _abrirPdf.Visible = _abrirPasta.Visible = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            MostrarErro(ex);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _cancelamento.Dispose();
            _cancelamento = null;
            DefinirTraduzindo(false);
        }
    }

    private Progress<Andamento> CriarAndamento() => new(a =>
    {
        if (a.Porcentagem >= 0) _barra.Value = a.Porcentagem;
        _status.ForeColor = Cinza;
        _status.Text = a.Mensagem;
    });

    private void MostrarErro(Exception ex)
    {
        _status.ForeColor = Color.FromArgb(220, 38, 38);
        _status.Text = "Erro: " + (ex is HttpRequestException ? "sem conexão com a internet." : ex.Message);
    }

    private void MontarPainelDeepL()
    {
        _painelDeepL.Dock = DockStyle.Fill;
        _painelDeepL.AutoSize = true;
        _painelDeepL.ColumnCount = 2;
        _painelDeepL.Margin = new Padding(0, 0, 0, 4);
        _painelDeepL.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _painelDeepL.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _chaveDeepL.Dock = DockStyle.Fill;
        _chaveDeepL.UseSystemPasswordChar = true;
        _chaveDeepL.PlaceholderText = "Cole aqui sua chave do DeepL";
        _chaveDeepL.Text = _config.ChaveDeepL;
        _chaveDeepL.Margin = new Padding(0, 1, 8, 4);
        _chaveDeepL.TextChanged += (_, _) => _usoDeepL.Text = "";

        EstilizarBotao(_testarChave, "Testar chave", primario: false);
        _testarChave.Dock = DockStyle.None;
        _testarChave.Size = new Size(120, _chaveDeepL.PreferredHeight + 2);
        _testarChave.Margin = new Padding(0, 0, 0, 4);
        _testarChave.Click += async (_, _) => await TestarChaveAsync();

        var link = new LinkLabel
        {
            Text = "Não tem chave? Crie uma grátis (500 mil caracteres por mês)",
            AutoSize = true,
            LinkColor = Destaque,
            Margin = new Padding(0, 0, 0, 2),
        };
        link.LinkClicked += (_, _) => Abrir(TradutorDeepL.PaginaCadastro);

        _usoDeepL.AutoSize = true;
        _usoDeepL.MaximumSize = new Size(500, 0);
        _usoDeepL.Margin = new Padding(0, 0, 0, 0);

        _painelDeepL.Controls.Add(_chaveDeepL, 0, 0);
        _painelDeepL.Controls.Add(_testarChave, 1, 0);
        _painelDeepL.Controls.Add(link, 0, 1);
        _painelDeepL.SetColumnSpan(link, 2);
        _painelDeepL.Controls.Add(_usoDeepL, 0, 2);
        _painelDeepL.SetColumnSpan(_usoDeepL, 2);
    }

    private string ServicoEscolhido => (string?)_servico.SelectedValue ?? "auto";

    private void AtualizarPainelDeepL()
    {
        _painelDeepL.Visible = ServicoEscolhido is "auto" or "deepl";
        _chaveDeepL.PlaceholderText = ServicoEscolhido == "auto"
            ? "Opcional: chave do DeepL (entra no rodízio com a melhor qualidade)"
            : "Cole aqui sua chave do DeepL";
    }

    private TradutorRotativo CriarTradutor(string origem, string destino)
    {
        var servicos = new List<ITradutor>();
        string chave = _chaveDeepL.Text.Trim();
        if (ServicoEscolhido == "deepl" || (ServicoEscolhido == "auto" && chave.Length > 0))
            servicos.Add(new TradutorDeepL(chave, origem, destino));
        if (ServicoEscolhido is "auto" or "google")
            servicos.Add(new TradutorGoogle(origem, destino));
        if (ServicoEscolhido is "auto" or "microsoft")
            servicos.Add(new TradutorMicrosoft(origem, destino));
        return new TradutorRotativo(servicos);
    }

    private async Task TestarChaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_chaveDeepL.Text))
        {
            MostrarUso("Cole a chave primeiro.", erro: true);
            return;
        }

        _testarChave.Enabled = false;
        MostrarUso("Testando...", erro: false);
        try
        {
            var (usado, limite) = await TradutorDeepL.ConsultarUsoAsync(_chaveDeepL.Text);
            MostrarUso($"✓ Chave funcionando. Usado este mês: {usado:N0} de {limite:N0} caracteres.", erro: false);
            _usoDeepL.ForeColor = Color.FromArgb(22, 163, 74);
            SalvarConfiguracao();
        }
        catch (Exception ex)
        {
            MostrarUso(ex is HttpRequestException ? "Sem conexão com o DeepL. Confira sua internet." : ex.Message, erro: true);
        }
        finally
        {
            _testarChave.Enabled = true;
        }
    }

    private void MostrarUso(string texto, bool erro)
    {
        _usoDeepL.Text = texto;
        _usoDeepL.ForeColor = erro ? Color.FromArgb(220, 38, 38) : Cinza;
    }

    private static Label Rotulo(string texto) => new()
    {
        Text = texto,
        AutoSize = true,
        ForeColor = Cinza,
        Margin = new Padding(0, 0, 0, 4),
    };

    private static void ConfigurarCombo(ComboBox combo, IEnumerable<(string Codigo, string Nome)> itens, string padrao)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Dock = DockStyle.Fill;
        combo.DisplayMember = "Nome";
        combo.ValueMember = "Codigo";
        combo.DataSource = itens.Select(i => new { i.Codigo, i.Nome }).ToList();
        combo.BindingContextChanged += (_, _) => combo.SelectedValue = padrao;
    }

    private static void EstilizarBotao(Button botao, string texto, bool primario)
    {
        botao.Text = texto;
        botao.Dock = DockStyle.Fill;
        botao.Height = 40;
        botao.FlatStyle = FlatStyle.Flat;
        botao.Cursor = Cursors.Hand;
        botao.FlatAppearance.BorderColor = primario ? Destaque : Color.FromArgb(203, 213, 225);
        botao.BackColor = primario ? Destaque : Color.White;
        botao.ForeColor = primario ? Color.White : Color.FromArgb(30, 41, 59);
        if (!primario) return;

        botao.Font = new Font("Segoe UI Semibold", 10f);
        botao.EnabledChanged += (_, _) =>
        {
            var cor = botao.Enabled ? Destaque : Color.FromArgb(148, 163, 184);
            botao.BackColor = cor;
            botao.FlatAppearance.BorderColor = cor;
        };
    }

    private void DesenharAreaArquivo(object? sender, PaintEventArgs e)
    {
        var r = _areaArquivo.ClientRectangle;
        r.Inflate(-1, -1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var fundo = new SolidBrush(_arrastando ? Color.FromArgb(239, 246, 255) : Color.FromArgb(248, 250, 252));
        e.Graphics.FillRectangle(fundo, r);

        using var borda = new Pen(_arrastando || _arquivo != null ? Destaque : Color.FromArgb(203, 213, 225), 2)
        {
            DashStyle = _arquivo != null ? DashStyle.Solid : DashStyle.Dash,
        };
        e.Graphics.DrawRectangle(borda, r);
    }

    private void DefinirArrastando(bool valor)
    {
        _arrastando = valor;
        _areaArquivo.Invalidate();
    }

    private static string? PdfArrastado(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } arquivos
        && arquivos[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? arquivos[0]
            : null;

    private void AoArrastar(DragEventArgs e)
    {
        bool valido = _cancelamento == null && PdfArrastado(e) != null;
        e.Effect = valido ? DragDropEffects.Copy : DragDropEffects.None;
        DefinirArrastando(valido);
    }

    private void AoSoltar(DragEventArgs e)
    {
        DefinirArrastando(false);
        if (PdfArrastado(e) is { } arquivo) SelecionarArquivo(arquivo);
    }

    private void EscolherArquivo()
    {
        if (_cancelamento != null) return;
        using var dialogo = new OpenFileDialog { Filter = "Arquivos PDF (*.pdf)|*.pdf", Title = "Escolha o PDF" };
        if (dialogo.ShowDialog(this) == DialogResult.OK) SelecionarArquivo(dialogo.FileName);
    }

    private void SelecionarArquivo(string caminho)
    {
        _arquivo = caminho;
        var tamanho = new FileInfo(caminho).Length / 1024.0 / 1024.0;
        _textoArquivo.Text = $"📄 {Path.GetFileName(caminho)}\n{tamanho:0.0} MB · clique para trocar";
        _textoArquivo.ForeColor = Color.FromArgb(30, 41, 59);
        _traduzir.Enabled = true;
        _abrirPdf.Visible = _abrirPasta.Visible = false;
        _barra.Value = 0;
        _status.ForeColor = Cinza;
        _status.Text = AtualizarProgressoSalvo() is > 0 and var salvas
            ? $"Tradução anterior encontrada ({salvas} páginas prontas). Clique em Traduzir para continuar de onde parou."
            : "";
        _areaArquivo.Invalidate();
    }

    private async Task TraduzirAsync()
    {
        if (_arquivo == null) return;

        if (ServicoEscolhido == "deepl" && string.IsNullOrWhiteSpace(_chaveDeepL.Text))
        {
            _status.ForeColor = Color.FromArgb(220, 38, 38);
            _status.Text = "Cole sua chave do DeepL (ou escolha o Automático, que não precisa de chave).";
            _chaveDeepL.Focus();
            return;
        }

        SalvarConfiguracao();
        string origem = (string)_origem.SelectedValue!;
        string destino = (string)_destino.SelectedValue!;
        using var tradutor = CriarTradutor(origem, destino);

        _cancelamento = new CancellationTokenSource();
        DefinirTraduzindo(true);

        try
        {
            _resultado = await TraducaoPdf.TraduzirAsync(
                _arquivo, destino, _incluirImagens.Checked, tradutor, CriarAndamento(), _cancelamento.Token);

            _status.ForeColor = Color.FromArgb(22, 163, 74);
            _status.Text = $"Pronto! Salvo como \"{Path.GetFileName(_resultado)}\", na mesma pasta do original.";
            _abrirPdf.Visible = _abrirPasta.Visible = true;
        }
        catch (OperationCanceledException)
        {
            _status.ForeColor = Cinza;
            _status.Text = "Cancelado. O que já foi traduzido ficou salvo: clique em Traduzir para continuar de onde parou.";
        }
        catch (Exception ex)
        {
            MostrarErro(ex);
        }
        finally
        {
            _cancelamento.Dispose();
            _cancelamento = null;
            DefinirTraduzindo(false);
            AtualizarProgressoSalvo();
        }
    }

    private void DefinirTraduzindo(bool traduzindo)
    {
        _traduzir.Enabled = !traduzindo;
        _cancelar.Enabled = traduzindo;
        _origem.Enabled = _destino.Enabled = _incluirImagens.Enabled = !traduzindo;
        _servico.Enabled = _chaveDeepL.Enabled = _testarChave.Enabled = !traduzindo;
        _areaArquivo.Cursor = _textoArquivo.Cursor = traduzindo ? Cursors.Default : Cursors.Hand;
        if (traduzindo) _abrirPdf.Visible = _abrirPasta.Visible = _gerarParcial.Visible = false;
    }

    private static void Abrir(string caminho) =>
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
}

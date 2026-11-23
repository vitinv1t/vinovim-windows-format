using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using VinovimFormat.Models;
using VinovimFormat.Services;
using IOPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace VinovimFormat;

public partial class MainWindow : Window
{
    private readonly Logger _log = new();
    private readonly string[] _nomesPassos = { "Diagnóstico", "Modo", "Imagem ISO", "Confirmação" };
    private readonly string[] _iconesPassos = { "IcScan", "IcLayers", "IcDisc", "IcShield" };

    private SystemInfo? _sistema;
    private IsoInfo? _iso;
    private string? _caminhoIso;
    private string? _raizIso;
    private int _passo;
    private bool _diagnosticoOk;
    private bool _isoOk;
    private bool _hashDivergente;
    private bool _ocupado;
    private bool _execucaoIniciada;
    private bool _setupIniciado;
    private bool _fechar;
    private CancellationTokenSource? _ctsHash;
    private CoreWebView2DownloadOperation? _download;
    private DownloadIsoWindow? _janelaDownload;
    private DateTime _ultimaAmostra;
    private long _bytesUltimaAmostra;
    private double _velocidade;
    private Atualizacao? _atualizacao;
    private List<FaseExecucao> _fases = new();

    public MainWindow()
    {
        InitializeComponent();
        var versao = UpdateService.VersaoAtual;
        TxtVersao.Text = $"Versão {versao.Major}.{versao.Minor}.{Math.Max(0, versao.Build)}\nRegisto: {IOPath.GetFileName(_log.Arquivo)}";
        _log.NovaLinha += linha => Dispatcher.InvokeAsync(() =>
        {
            TxtLog.AppendText(linha + Environment.NewLine);
            TxtLog.ScrollToEnd();
        });
    }

    private Panel[] Paineis => new Panel[] { PainelDiagnostico, PainelModo, PainelIso, PainelConfirmacao };

    private ModoFormatacao Modo =>
        RbModo1.IsChecked == true ? ModoFormatacao.ParticaoTemporaria :
        RbModo2.IsChecked == true ? ModoFormatacao.ReinstalacaoDireta : ModoFormatacao.Reparar;

    private TipoReinstalacao Tipo => RbManter.IsChecked == true ? TipoReinstalacao.ManterFicheiros : TipoReinstalacao.Limpa;

    private bool ApagaTudo => Modo == ModoFormatacao.ParticaoTemporaria ||
                              (Modo == ModoFormatacao.ReinstalacaoDireta && Tipo == TipoReinstalacao.Limpa);

    // ───────────────────────── Janela ─────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var escuro = 1;
            DwmSetWindowAttribute(hwnd, 20, ref escuro, sizeof(int));      // DWMWA_USE_IMMERSIVE_DARK_MODE
            var cor = 0x000B0909;                                          // #09090B em COLORREF (BGR)
            DwmSetWindowAttribute(hwnd, 35, ref cor, sizeof(int));         // DWMWA_CAPTION_COLOR (Windows 11)
        }
        catch { /* Windows 10 antigo: mantém a barra padrão */ }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _log.Info($"Vinovim Windows Format {UpdateService.VersaoAtual} iniciado.");
        IrPara(0);
        _ = VerificarAtualizacaoAsync();
        await DiagnosticarAsync();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_fechar) return;
        if (_download is not null)
        {
            var d = MessageBox.Show("A ISO ainda está a ser descarregada. Fechar agora cancela o download. Fechar mesmo assim?",
                "Vinovim Windows Format", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (d != MessageBoxResult.Yes) { e.Cancel = true; return; }
            try { _download.Cancel(); } catch { }
        }
        _janelaDownload?.Close();
        if (_ocupado && _execucaoIniciada)
        {
            var r = MessageBox.Show("Há uma operação em curso. Fechar agora pode deixar o processo a meio. Fechar mesmo assim?",
                "Vinovim Windows Format", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        if (_caminhoIso is not null && !_setupIniciado)
        {
            e.Cancel = true;
            _fechar = true;
            try { await IsoService.DesmontarAsync(_caminhoIso); } catch { }
            Close();
        }
    }

    // ───────────────────────── Navegação ─────────────────────────

    private void IrPara(int passo)
    {
        _passo = passo;
        for (var i = 0; i < Paineis.Length; i++)
            Paineis[i].Visibility = i == passo ? Visibility.Visible : Visibility.Collapsed;

        if (passo == 2) PrepararPassoIso();
        if (passo == 3) PrepararConfirmacao();

        Animar(Paineis[passo]);
        Rolagem.ScrollToTop();
        DesenharPassos();
        AtualizarBotoes();
    }

    /// <summary>Transição suave: o painel entra com fade e um pequeno deslize.</summary>
    private static void Animar(UIElement elemento)
    {
        var duracao = TimeSpan.FromMilliseconds(280);
        var curva = new CubicEase { EasingMode = EasingMode.EaseOut };
        var deslize = new TranslateTransform(16, 0);
        elemento.RenderTransform = deslize;
        elemento.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracao) { EasingFunction = curva });
        deslize.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(16, 0, duracao) { EasingFunction = curva });
    }

    // ───────────────────────── Atualizações ─────────────────────────

    private async Task VerificarAtualizacaoAsync()
    {
        _atualizacao = await UpdateService.VerificarAsync();
        if (_atualizacao is null) return;
        TxtAtualizacao.Text = $"Versão {_atualizacao.Etiqueta} disponível";
        PainelAtualizacao.Visibility = Visibility.Visible;
        Animar(PainelAtualizacao);
        _log.Info($"Há uma versão nova: {_atualizacao.Etiqueta}.");
    }

    private void BtnAtualizar_Click(object sender, RoutedEventArgs e) => Abrir(_atualizacao?.Pagina ?? UpdateService.Site);

    private void DesenharPassos()
    {
        PainelPassos.Children.Clear();
        for (var i = 0; i < _nomesPassos.Length; i++)
        {
            var atual = i == _passo;
            var feito = i < _passo || (_execucaoIniciada && i == _passo && !_ocupado);
            var linha = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var fundo = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 9, 10, 9),
                Background = atual ? (Brush)FindResource("Superficie2") : Brushes.Transparent,
            };
            var conteudo = new StackPanel { Orientation = Orientation.Horizontal };
            var icone = new ShapePath
            {
                Style = (Style)FindResource("Icone"),
                Data = (Geometry)FindResource(feito && !atual ? "IcCheck" : _iconesPassos[i]),
                Width = 16, Height = 16,
                Margin = new Thickness(0, 0, 12, 0),
                Stroke = (Brush)FindResource(atual ? "Destaque" : feito ? "Ok" : "TextoMudo"),
            };
            conteudo.Children.Add(icone);
            conteudo.Children.Add(new TextBlock
            {
                Text = _nomesPassos[i],
                FontSize = 13.5,
                FontWeight = atual ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource(atual ? "Texto" : feito ? "TextoFraco" : "TextoMudo"),
            });
            fundo.Child = conteudo;
            linha.Children.Add(fundo);
            PainelPassos.Children.Add(linha);
        }
    }

    private bool PodeAvancar() => _passo switch
    {
        0 => _diagnosticoOk,
        1 => true,
        2 => Modo == ModoFormatacao.Reparar ? _iso is null || _isoOk : _isoOk && !_hashDivergente,
        3 => ConfirmacaoValida(),
        _ => false,
    };

    private bool ConfirmacaoValida()
    {
        if (_execucaoIniciada) return false;
        if (Modo == ModoFormatacao.Reparar) return true;
        var bitlockerOk = _sistema?.BitLocker != BitLockerEstado.Ligado || ChkBitLocker.IsChecked == true;
        return ChkBackup.IsChecked == true && bitlockerOk && TxtConfirmacao.Text.Trim() == "FORMATAR";
    }

    private void AtualizarBotoes()
    {
        BtnVoltar.IsEnabled = _passo > 0 && !_ocupado && !_execucaoIniciada;
        BtnAvancar.IsEnabled = !_ocupado && PodeAvancar();
        BtnAvancar.Visibility = _execucaoIniciada ? Visibility.Collapsed : Visibility.Visible;
        BtnAvancar.Content = _passo switch
        {
            2 when Modo == ModoFormatacao.Reparar && _iso is null => "Continuar sem ISO",
            3 => Modo switch
            {
                ModoFormatacao.ParticaoTemporaria => "Preparar partição e arranque",
                ModoFormatacao.ReinstalacaoDireta => "Iniciar reinstalação",
                _ => "Iniciar reparação",
            },
            _ => "Continuar",
        };
        BtnDiagnostico.IsEnabled = !_ocupado;
        BtnEscolherIso.IsEnabled = !_ocupado;
        Cursor = _ocupado ? System.Windows.Input.Cursors.AppStarting : null;
    }

    private void Ocupado(bool valor)
    {
        _ocupado = valor;
        AtualizarBotoes();
    }

    private async void BtnAvancar_Click(object sender, RoutedEventArgs e)
    {
        if (!PodeAvancar()) return;
        if (_passo < 3) { IrPara(_passo + 1); return; }
        await ExecutarAsync();
    }

    private void BtnVoltar_Click(object sender, RoutedEventArgs e) { if (_passo > 0) IrPara(_passo - 1); }

    private void BtnSair_Click(object sender, RoutedEventArgs e) => Close();

    private void Opcao_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) AtualizarBotoes(); }

    private void TxtConfirmacao_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) AtualizarBotoes(); }

    // ───────────────────────── 1. Diagnóstico ─────────────────────────

    private async Task DiagnosticarAsync()
    {
        Ocupado(true);
        _diagnosticoOk = false;
        ListaDiagnostico.ItemsSource = null;
        TxtResumo.Text = "A analisar o computador…";
        try
        {
            var (info, verificacoes) = await SystemChecks.ExecutarAsync();
            _sistema = info;
            ListaDiagnostico.ItemsSource = verificacoes;
            TxtResumo.Text = info.Resumo;
            _diagnosticoOk = verificacoes.All(v => v.Status != CheckStatus.Erro);
            foreach (var v in verificacoes) _log.Info($"[{v.Status}] {v.Titulo}: {v.Detalhe}");

            var chave = info.ChaveFirmware ?? (info.ChaveParcial is null ? null : $"XXXXX-XXXXX-XXXXX-XXXXX-{info.ChaveParcial}");
            PainelChave.Visibility = chave is null ? Visibility.Collapsed : Visibility.Visible;
            TxtChave.Text = chave ?? "";

            var bitlocker = info.BitLocker == BitLockerEstado.Ligado;
            BtnBitLocker.Visibility = bitlocker ? Visibility.Visible : Visibility.Collapsed;
            ChkBitLocker.Visibility = bitlocker ? Visibility.Visible : Visibility.Collapsed;
            PainelParticaoExistente.Visibility = info.ParticaoTemporariaExiste ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            TxtResumo.Text = "Não foi possível concluir o diagnóstico: " + ex.Message;
            _log.Info("Erro no diagnóstico: " + ex);
        }
        finally { Ocupado(false); }
    }

    private async void BtnDiagnostico_Click(object sender, RoutedEventArgs e) => await DiagnosticarAsync();

    private void BtnCopiarChave_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(TxtChave.Text); } catch { }
    }

    private void BtnGuardarChave_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = "Chave-Windows.txt", Filter = "Texto (*.txt)|*.txt" };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName,
            $"Vinovim Windows Format · {DateTime.Now:g}\r\n{_sistema?.NomeProduto} {_sistema?.EdicaoAmigavel}\r\nChave: {TxtChave.Text}\r\n" +
            "Se a chave aparece com X, é uma licença digital: volta a ativar sozinha com a mesma conta/hardware.\r\n");
        if (BackupService.EstaNoDiscoDoSistema(dlg.FileName))
            MessageBox.Show("Guardou no disco do Windows, que vai ser apagado. Copie o ficheiro para o telemóvel, a nuvem ou outro disco.",
                "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────────────────────── 2. Modo ─────────────────────────

    private void Modo_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // eventos disparados durante InitializeComponent
        PainelTipoModo2.Visibility = RbModo2.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AtualizarBotoes();
    }

    private async void BtnLimparParticao_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show("Remover a partição VINOVIM_BOOT e devolver o espaço ao disco do Windows?",
            "Vinovim Windows Format", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        Ocupado(true);
        var ok = await PartitionBootService.LimparAsync(_log.Info);
        Ocupado(false);
        MessageBox.Show(ok ? "Partição temporária removida." : "Não foi possível remover tudo. Veja o ficheiro de registo.",
            "Vinovim Windows Format", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await DiagnosticarAsync();
    }

    private async void BtnRearmar_Click(object sender, RoutedEventArgs e)
    {
        Ocupado(true);
        var ok = await PartitionBootService.RearmarArranqueAsync(_log.Info);
        Ocupado(false);
        if (!ok)
        {
            MessageBox.Show("Não encontrei a entrada de arranque. Remova a partição e prepare o Modo 1 de novo.",
                "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        PerguntarReiniciar();
    }

    // ───────────────────────── 3. ISO ─────────────────────────

    private void PrepararPassoIso()
    {
        TxtIntroIso.Text = Modo == ModoFormatacao.Reparar
            ? "Opcional no Modo 3: com uma ISO, o DISM consegue reparar o sistema mesmo sem ligação ao Windows Update. Pode continuar sem ISO."
            : "A ISO faz o papel do pen-drive. A app descarrega-a diretamente da Microsoft, ou pode escolher uma que já tenha.";

        if (CmbVersao.ItemsSource is null)
        {
            CmbVersao.ItemsSource = new[] { "Windows 11", "Windows 10" };
            CmbVersao.SelectedIndex = _sistema is { IsWindows11: false } && !ArmPc ? 1 : 0;
        }
        if (_iso is not null && _sistema is not null) MostrarCompatibilidade();
    }

    private static void Abrir(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private bool ArmPc => _sistema?.Arquitetura == "arm64";

    private ProdutoWindows ProdutoEscolhido => CmbVersao.SelectedIndex == 1 ? ProdutoWindows.Windows10 : ProdutoWindows.Windows11;

    private void LnkPaginaOficial_Click(object sender, RoutedEventArgs e) =>
        Abrir(IsoDownloadService.PaginaOficial(ProdutoEscolhido, ArmPc));

    private void CmbVersao_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbVersao.SelectedIndex < 0) return;
        if (ProdutoEscolhido == ProdutoWindows.Windows10 && ArmPc)
        {
            TxtDownloadInfo.Text = "A Microsoft não disponibiliza a ISO do Windows 10 para processadores ARM64. Escolha o Windows 11.";
            BtnDescarregarIso.IsEnabled = false;
            return;
        }
        BtnDescarregarIso.IsEnabled = _download is null;
        TxtDownloadInfo.Text = ProdutoEscolhido == ProdutoWindows.Windows10
            ? "Abre a página oficial do Windows 10 dentro da app. Nota: o Windows 10 já não recebe atualizações de segurança gratuitas."
            : $"Abre a página oficial do Windows 11{(ArmPc ? " para ARM64" : "")} dentro da app. Escolha a edição e o idioma, carregue em Download " +
              "e a app guarda a ISO, mostra o progresso e carrega-a sozinha no fim.";
    }

    private void BtnDescarregarIso_Click(object sender, RoutedEventArgs e)
    {
        if (_download is not null) return;

        var transferencias = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dlg = new OpenFolderDialog { Title = "Onde guardar a ISO do Windows" };
        if (Directory.Exists(transferencias)) dlg.InitialDirectory = transferencias;
        if (dlg.ShowDialog(this) != true) return;
        var pasta = dlg.FolderName;

        var livre = new DriveInfo(IOPath.GetPathRoot(pasta)!).AvailableFreeSpace;
        if (livre < 8L * 1024 * 1024 * 1024)
        {
            MessageBox.Show($"Só há {livre / 1024d / 1024 / 1024:0.0} GB livres nesse disco. A ISO precisa de cerca de 8 GB.",
                "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _janelaDownload?.Close();
        _janelaDownload = new DownloadIsoWindow(ProdutoEscolhido, ArmPc, pasta) { Owner = this };
        _janelaDownload.DownloadIniciado += AoIniciarDownload;
        _janelaDownload.Closed += (_, _) => { if (_download is null) _janelaDownload = null; };
        _log.Info($"Página oficial aberta: {IsoDownloadService.PaginaOficial(ProdutoEscolhido, ArmPc)} (destino: {pasta})");
        _janelaDownload.Show();
    }

    private void AoIniciarDownload(CoreWebView2DownloadOperation operacao, string destino)
    {
        _download = operacao;
        _ultimaAmostra = DateTime.UtcNow;
        _bytesUltimaAmostra = 0;
        _velocidade = 0;
        _log.Info($"A descarregar {IOPath.GetFileName(destino)} para {IOPath.GetDirectoryName(destino)}");

        Ocupado(true);
        BtnDescarregarIso.IsEnabled = false;
        CmbVersao.IsEnabled = false;
        PainelProgressoDownload.Visibility = Visibility.Visible;
        Animar(PainelProgressoDownload);
        BarraDownload.Value = 0;
        TxtProgressoDownload.Text = $"A descarregar {IOPath.GetFileName(destino)}…";
        Activate();

        operacao.BytesReceivedChanged += (_, _) => AtualizarProgressoDownload(operacao);
        operacao.StateChanged += async (_, _) => await AoMudarEstadoDownload(operacao, destino);
    }

    private void AtualizarProgressoDownload(CoreWebView2DownloadOperation op)
    {
        var agora = DateTime.UtcNow;
        var dt = (agora - _ultimaAmostra).TotalSeconds;
        if (dt < 0.5) return;
        var instantanea = (op.BytesReceived - _bytesUltimaAmostra) / dt;
        _velocidade = _velocidade == 0 ? instantanea : _velocidade * 0.7 + instantanea * 0.3;
        _ultimaAmostra = agora;
        _bytesUltimaAmostra = op.BytesReceived;

        var total = (long)(op.TotalBytesToReceive ?? 0);
        BarraDownload.Value = total > 0 ? op.BytesReceived * 100d / total : 0;
        TxtProgressoDownload.Text = FormatarProgresso(new ProgressoDownload(op.BytesReceived, total, _velocidade));
    }

    private async Task AoMudarEstadoDownload(CoreWebView2DownloadOperation op, string destino)
    {
        if (op.State == CoreWebView2DownloadState.InProgress) return;

        _download = null;
        Ocupado(false);
        BtnDescarregarIso.IsEnabled = true;
        CmbVersao.IsEnabled = true;
        _janelaDownload?.Close();
        _janelaDownload = null;

        if (op.State == CoreWebView2DownloadState.Completed)
        {
            BarraDownload.Value = 100;
            TxtProgressoDownload.Text = "Download concluído. A verificar a ISO…";
            _log.Info("ISO descarregada: " + destino);
            if (_sistema is not null)
                _sistema.EspacoLivre = new DriveInfo(IOPath.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace;
            await CarregarIsoAsync(destino);
        }
        else
        {
            var motivo = op.InterruptReason == CoreWebView2DownloadInterruptReason.UserCanceled
                ? "Download cancelado."
                : $"O download foi interrompido ({op.InterruptReason}). Carregue em \"Descarregar a ISO oficial\" para tentar de novo.";
            TxtProgressoDownload.Text = motivo;
            _log.Info(motivo);
        }
    }

    private void BtnCancelarDownload_Click(object sender, RoutedEventArgs e)
    {
        try { _download?.Cancel(); } catch { /* já terminou */ }
    }

    private static string FormatarProgresso(ProgressoDownload p)
    {
        static string Gb(long b) => $"{b / 1024d / 1024 / 1024:0.00} GB";
        var texto = p.Total > 0 ? $"{Gb(p.Recebidos)} de {Gb(p.Total)}" : Gb(p.Recebidos);
        if (p.BytesPorSegundo > 0)
        {
            texto += $"  ·  {p.BytesPorSegundo / 1024 / 1024:0.0} MB/s";
            if (p.Total > p.Recebidos)
            {
                var falta = TimeSpan.FromSeconds((p.Total - p.Recebidos) / p.BytesPorSegundo);
                texto += falta.TotalMinutes >= 1 ? $"  ·  faltam {Math.Ceiling(falta.TotalMinutes):0} min" : "  ·  falta menos de 1 min";
            }
        }
        return texto;
    }

    private async void BtnEscolherIso_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Imagem ISO do Windows (*.iso)|*.iso", Title = "Escolher a ISO do Windows" };
        if (dlg.ShowDialog(this) != true) return;
        await CarregarIsoAsync(dlg.FileName);
    }

    private async Task CarregarIsoAsync(string caminho)
    {
        Ocupado(true);
        _isoOk = false;
        _iso = null;
        _hashDivergente = false;
        PainelCompat.Visibility = Visibility.Collapsed;
        ListaIso.ItemsSource = null;
        TxtCaminhoIso.Text = caminho;
        try
        {
            if (_caminhoIso is not null && !string.Equals(_caminhoIso, caminho, StringComparison.OrdinalIgnoreCase))
                await IsoService.DesmontarAsync(_caminhoIso);

            _caminhoIso = caminho;
            _log.Info($"ISO escolhida: {caminho} ({new FileInfo(caminho).Length / 1024d / 1024 / 1024:0.00} GB)");
            _ = CalcularHashAsync(caminho);

            TxtStatusIso.Text = "A montar a ISO…";
            _raizIso = await IsoService.MontarAsync(caminho);
            _log.Info($"ISO montada em {_raizIso}");

            TxtStatusIso.Text = "A ler as edições do Windows na ISO…";
            var imagem = IsoService.ValidarArquivos(_raizIso);
            _iso = await IsoService.LerInfoAsync(imagem);
            TxtStatusIso.Text = $"{_iso.NomeWindows} · versão {_iso.Versao} · {_iso.ArquiteturaNome} · {string.Join(", ", _iso.Idiomas)} · montada em {_raizIso}";
            MostrarCompatibilidade();
        }
        catch (Exception ex)
        {
            TxtStatusIso.Text = ex.Message;
            _log.Info("Erro na ISO: " + ex.Message);
        }
        finally { Ocupado(false); }
    }

    private void MostrarCompatibilidade()
    {
        if (_iso is null || _sistema is null) return;
        var lista = IsoService.Compatibilidade(_sistema, _iso);

        if (Modo == ModoFormatacao.ParticaoTemporaria && _caminhoIso is not null)
        {
            var tamanho = PartitionBootService.CalcularTamanho(new FileInfo(_caminhoIso).Length);
            var necessario = tamanho + 2L * 1024 * 1024 * 1024;
            lista.Add(_sistema.EspacoLivre >= necessario
                ? new("Partição temporária", CheckStatus.Ok, $"Vai ocupar {tamanho / 1024d / 1024 / 1024:0.0} GB do {_sistema.UnidadeSistema} até ao fim da instalação.")
                : new("Partição temporária", CheckStatus.Erro, $"São precisos {necessario / 1024d / 1024 / 1024:0.0} GB livres em {_sistema.UnidadeSistema} para a partição temporária."));
        }

        ListaIso.ItemsSource = lista;
        PainelCompat.Visibility = Visibility.Visible;
        _isoOk = lista.All(v => v.Status != CheckStatus.Erro);
        foreach (var v in lista) _log.Info($"[ISO {v.Status}] {v.Titulo}: {v.Detalhe}");
        AtualizarBotoes();
    }

    private async Task CalcularHashAsync(string caminho)
    {
        _ctsHash?.Cancel();
        var cts = _ctsHash = new CancellationTokenSource();
        PainelHash.Visibility = Visibility.Visible;
        ProgressoHash.Visibility = Visibility.Visible;
        ProgressoHash.Value = 0;
        TxtHash.Text = "A calcular SHA-256…";
        TxtHashResultado.Text = "";
        try
        {
            var progresso = new Progress<double>(p => ProgressoHash.Value = p);
            var hash = await Task.Run(() => SafetyService.Sha256Async(caminho, progresso, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            TxtHash.Text = hash;
            _log.Info($"SHA-256 da ISO: {hash}");
            CompararHash();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TxtHash.Text = "Não foi possível calcular: " + ex.Message; }
        finally { if (!cts.IsCancellationRequested) ProgressoHash.Visibility = Visibility.Collapsed; }
    }

    private void TxtHashEsperado_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) CompararHash(); }

    private void CompararHash()
    {
        var esperado = new string(TxtHashEsperado.Text.Where(Uri.IsHexDigit).ToArray());
        var calculado = TxtHash.Text;
        _hashDivergente = false;
        if (esperado.Length == 0 || calculado.Length != 64)
            TxtHashResultado.Text = "";
        else if (string.Equals(esperado, calculado, StringComparison.OrdinalIgnoreCase))
        {
            TxtHashResultado.Text = "O hash coincide: a ISO é original e não está corrompida.";
            TxtHashResultado.Foreground = (Brush)FindResource("Ok");
        }
        else
        {
            _hashDivergente = true;
            TxtHashResultado.Text = "O hash NÃO coincide. Descarregue a ISO outra vez do site da Microsoft.";
            TxtHashResultado.Foreground = (Brush)FindResource("Erro");
        }
        AtualizarBotoes();
    }

    // ───────────────────────── 4. Confirmação ─────────────────────────

    private void PrepararConfirmacao()
    {
        var s = _sistema!;
        var unidade = s.UnidadeSistema;
        TxtAviso.Text = Modo switch
        {
            ModoFormatacao.ParticaoTemporaria =>
                $"O {unidade} vai ser reduzido para criar a partição VINOVIM_BOOT e o próximo arranque vai abrir o instalador do Windows. " +
                $"Lá, quando apagar ou formatar a partição do Windows, TUDO o que está em {unidade} desaparece. Não apague a partição VINOVIM_BOOT durante a instalação.",
            ModoFormatacao.ReinstalacaoDireta when Tipo == TipoReinstalacao.Limpa =>
                $"O Windows vai ser reinstalado do zero. Programas, definições e ficheiros em {unidade} vão ser apagados. O PC reinicia várias vezes: não o desligue.",
            ModoFormatacao.ReinstalacaoDireta =>
                "O Windows vai ser reinstalado mantendo os ficheiros pessoais. Programas e definições vão ser removidos. O PC reinicia várias vezes: não o desligue.",
            _ => "A reparação não apaga ficheiros nem programas. Pode demorar 20 a 60 minutos; não desligue o PC até terminar.",
        };
        AvisoApagar.Background = (Brush)new BrushConverter().ConvertFrom(Modo == ModoFormatacao.Reparar ? "#140078D4" : "#14F87171")!;
        AvisoApagar.BorderBrush = (Brush)new BrushConverter().ConvertFrom(Modo == ModoFormatacao.Reparar ? "#400078D4" : "#40F87171")!;

        var iso = _iso is null ? "sem ISO" : $"{_iso.NomeWindows} {_iso.Versao} ({_iso.ArquiteturaNome})";
        var modo = Modo switch
        {
            ModoFormatacao.ParticaoTemporaria => "Modo 1 · partição temporária + arranque no instalador",
            ModoFormatacao.ReinstalacaoDireta => $"Modo 2 · setup.exe ({(Tipo == TipoReinstalacao.Limpa ? "limpa" : "manter ficheiros")})",
            _ => "Modo 3 · reparar o sistema",
        };
        PreencherResumo(new (string, string)[]
        {
            ("Modo", modo),
            ("Este PC", $"{s.NomeProduto} {s.EdicaoAmigavel} {s.VersaoExibicao} · {s.Arquitetura}"),
            ("Imagem ISO", iso),
            ("Disco do Windows", $"{s.UnidadeSistema} · {s.EspacoLivreGb:0.0} GB livres"),
            ("Antes de começar", "Ponto de restauro do sistema" + (s.BitLocker == BitLockerEstado.Ligado && Modo != ModoFormatacao.Reparar ? " e BitLocker suspenso" : "")),
        });

        var formatar = Modo != ModoFormatacao.Reparar;
        PainelPalavra.Visibility = formatar ? Visibility.Visible : Visibility.Collapsed;
        ChkBackup.Visibility = formatar ? Visibility.Visible : Visibility.Collapsed;
        ChkBitLocker.Visibility = formatar && s.BitLocker == BitLockerEstado.Ligado ? Visibility.Visible : Visibility.Collapsed;
        TxtConfirmacao.Text = "";
    }

    private void PreencherResumo((string Rotulo, string Valor)[] linhas)
    {
        GradeResumo.Children.Clear();
        GradeResumo.RowDefinitions.Clear();
        GradeResumo.ColumnDefinitions.Clear();
        GradeResumo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        GradeResumo.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < linhas.Length; i++)
        {
            GradeResumo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var borda = new Thickness(0, 0, 0, i < linhas.Length - 1 ? 1 : 0);
            var rotulo = new Border
            {
                BorderBrush = (Brush)FindResource("Linha"), BorderThickness = borda, Padding = new Thickness(0, 11, 12, 11),
                Child = new TextBlock { Text = linhas[i].Rotulo, Foreground = (Brush)FindResource("TextoMudo"), FontSize = 13 },
            };
            var valor = new Border
            {
                BorderBrush = (Brush)FindResource("Linha"), BorderThickness = borda, Padding = new Thickness(0, 11, 0, 11),
                Child = new TextBlock { Text = linhas[i].Valor, TextWrapping = TextWrapping.Wrap, FontSize = 13.5 },
            };
            Grid.SetRow(rotulo, i);
            Grid.SetRow(valor, i);
            Grid.SetColumn(valor, 1);
            GradeResumo.Children.Add(rotulo);
            GradeResumo.Children.Add(valor);
        }
    }

    private void BtnEscolherPasta_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Pasta para a cópia de segurança (de preferência fora do disco do Windows)" };
        if (dlg.ShowDialog(this) != true) return;
        var pasta = IOPath.Combine(dlg.FolderName, $"Vinovim-Backup-{DateTime.Now:yyyyMMdd-HHmm}");
        Directory.CreateDirectory(pasta);
        TxtPastaBackup.Text = pasta;
        var noSistema = BackupService.EstaNoDiscoDoSistema(pasta);
        TxtAvisoPasta.Visibility = noSistema ? Visibility.Visible : Visibility.Collapsed;
        TxtAvisoPasta.Text = "Esta pasta está no disco do Windows e vai ser apagada. Depois de guardar, copie-a para o telemóvel, a nuvem ou outro disco.";
    }

    private string? PastaBackup()
    {
        if (!string.IsNullOrEmpty(TxtPastaBackup.Text)) return TxtPastaBackup.Text;
        MessageBox.Show("Escolha primeiro a pasta da cópia de segurança.", "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private async void BtnDrivers_Click(object sender, RoutedEventArgs e)
    {
        if (PastaBackup() is not { } pasta) return;
        Ocupado(true);
        _log.Info("A exportar drivers…");
        var r = await BackupService.ExportarDriversAsync(pasta, _log.Info);
        _log.Info(r.Sucesso ? $"Drivers guardados em {IOPath.Combine(pasta, "Drivers")}" : $"Erro ao exportar drivers ({r.CodigoSaida}).");
        Ocupado(false);
        MessageBox.Show(r.Sucesso ? "Drivers guardados. Depois de formatar, pode instalá-los no Gestor de Dispositivos (Atualizar controlador → Procurar no computador)." : "Não foi possível exportar os drivers. Veja o registo.",
            "Vinovim Windows Format", MessageBoxButton.OK, r.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void BtnWifi_Click(object sender, RoutedEventArgs e)
    {
        if (PastaBackup() is not { } pasta) return;
        Ocupado(true);
        var r = await BackupService.ExportarWifiAsync(pasta, _log.Info);
        Ocupado(false);
        MessageBox.Show(r.Sucesso
                ? "Redes Wi-Fi guardadas (com as palavras-passe em texto). Para restaurar: netsh wlan add profile filename=\"ficheiro.xml\""
                : "Não foi possível exportar as redes Wi-Fi (o PC tem Wi-Fi?).",
            "Vinovim Windows Format", MessageBoxButton.OK, r.Sucesso ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void BtnBitLocker_Click(object sender, RoutedEventArgs e)
    {
        if (PastaBackup() is not { } pasta) return;
        try
        {
            var arquivo = await BackupService.SalvarChaveBitLockerAsync(pasta, _sistema!.UnidadeSistema);
            _log.Info($"Chave do BitLocker guardada em {arquivo}");
            Process.Start(new ProcessStartInfo(arquivo) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnAbrirPasta_Click(object sender, RoutedEventArgs e)
    {
        if (PastaBackup() is { } pasta) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{pasta}\""));
    }

    // ───────────────────────── Execução ─────────────────────────

    private List<FaseExecucao> CriarFases()
    {
        var bitlocker = Modo != ModoFormatacao.Reparar && _sistema?.BitLocker == BitLockerEstado.Ligado;
        var nomes = new List<string> { "Criar ponto de restauro" };
        if (bitlocker) nomes.Add("Suspender o BitLocker");
        nomes.AddRange(Modo switch
        {
            ModoFormatacao.ParticaoTemporaria => new[]
            {
                "Criar a partição VINOVIM_BOOT", "Copiar o instalador do Windows", "Preparar a limpeza automática",
                "Configurar o arranque", "Ocultar a partição",
            },
            ModoFormatacao.ReinstalacaoDireta => new[] { "Iniciar o instalador oficial" },
            _ => new[] { "DISM: reparar a imagem do Windows", "SFC: verificar ficheiros do sistema", "Ativar o Ambiente de Recuperação" },
        });
        return nomes.Select(n => new FaseExecucao(n)).ToList();
    }

    private FaseExecucao FaseQueComeca(string inicio) => _fases.First(f => f.Nome.StartsWith(inicio, StringComparison.Ordinal));

    /// <summary>Executa uma fase: marca-a em curso, depois feita (ou com erro, relançando a exceção).</summary>
    private async Task<T> Fase<T>(string inicio, Func<Task<T>> acao, Func<T, string>? detalhe = null)
    {
        var fase = FaseQueComeca(inicio);
        fase.Estado = EstadoFase.EmCurso;
        TxtTituloExec.Text = fase.Nome + "…";
        try
        {
            var r = await acao();
            fase.Estado = EstadoFase.Feito;
            if (detalhe is not null) fase.Detalhe = detalhe(r);
            return r;
        }
        catch
        {
            fase.Estado = EstadoFase.Erro;
            throw;
        }
    }

    private Task Fase(string inicio, Func<Task> acao) => Fase(inicio, async () => { await acao(); return true; });

    private void MostrarConclusao(bool sucesso, string titulo, string texto)
    {
        TxtRotuloExec.Text = sucesso ? "CONCLUÍDO" : "INTERROMPIDO";
        TxtConcluidoTitulo.Text = titulo;
        TxtConcluidoTexto.Text = texto;
        var conv = new BrushConverter();
        PainelConcluido.Background = (Brush)conv.ConvertFrom(sucesso ? "#144ADE80" : "#14F87171")!;
        PainelConcluido.BorderBrush = (Brush)conv.ConvertFrom(sucesso ? "#404ADE80" : "#40F87171")!;
        IcConcluido.Data = (Geometry)FindResource(sucesso ? "IcCheck" : "IcX");
        IcConcluido.Stroke = (Brush)FindResource(sucesso ? "Ok" : "Erro");
        ((Border)IcConcluido.Parent).Background = (Brush)conv.ConvertFrom(sucesso ? "#264ADE80" : "#26F87171")!;
        PainelConcluido.Visibility = Visibility.Visible;
        Animar(PainelConcluido);
    }

    private async Task ExecutarAsync()
    {
        var simular = ChkSimular.IsChecked == true;
        if (!simular && ApagaTudo)
        {
            var r = MessageBox.Show($"Última confirmação: todos os dados do disco {_sistema!.UnidadeSistema} vão ser apagados. Continuar?",
                "Vinovim Windows Format", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r != MessageBoxResult.Yes) return;
        }

        _execucaoIniciada = true;
        _fases = CriarFases();
        ListaFases.ItemsSource = _fases;
        VistaConfirmar.Visibility = Visibility.Collapsed;
        VistaExecutar.Visibility = Visibility.Visible;
        Animar(VistaExecutar);
        Rolagem.ScrollToTop();
        Ocupado(true);
        DesenharPassos();

        var progresso = new Progress<double>(p => ProgressoExec.Value = p);
        IProgress<double> relatar = progresso;
        var particaoCriada = false;
        try
        {
            _log.Info($"=== Início: {Modo}{(Modo == ModoFormatacao.ReinstalacaoDireta ? $" ({Tipo})" : "")}{(simular ? " · MODO DE TESTE" : "")} ===");
            if (simular) { Simular(); return; }

            TxtStatusExec.Text = "Não desligue o computador até terminar.";
            await Fase("Criar ponto de restauro", () => SafetyService.CriarPontoRestauroAsync(_log.Info), ok => ok ? "" : "indisponível");
            relatar.Report(3);

            if (_fases.Any(f => f.Nome.StartsWith("Suspender", StringComparison.Ordinal)))
            {
                await Fase("Suspender", async () =>
                {
                    var b = await BackupService.SuspenderBitLockerAsync(_sistema!.UnidadeSistema);
                    _log.Info(b.Sucesso ? "BitLocker suspenso durante os próximos reinícios." : "Aviso: não foi possível suspender o BitLocker.");
                    return b.Sucesso;
                }, ok => ok ? "" : "não suspenso");
            }

            switch (Modo)
            {
                case ModoFormatacao.ParticaoTemporaria:
                {
                    var s = _sistema!;
                    var tamanho = PartitionBootService.CalcularTamanho(new FileInfo(_caminhoIso!).Length);
                    var letra = await Fase("Criar a partição", () => PartitionBootService.CriarParticaoAsync(tamanho, s.UnidadeSistema, _log.Info),
                        l => $"{tamanho / 1024d / 1024 / 1024:0.0} GB em {l}");
                    particaoCriada = true;
                    relatar.Report(8);

                    var copia = new Progress<double>(p =>
                    {
                        ProgressoExec.Value = 8 + p * 0.8;
                        FaseQueComeca("Copiar").Detalhe = $"{p:0}%";
                    });
                    await Fase("Copiar", () => PartitionBootService.CopiarIsoAsync(_raizIso!, letra, _log.Info, copia));
                    FaseQueComeca("Copiar").Detalhe = "";
                    await Fase("Preparar a limpeza", () => PartitionBootService.EscreverLimpezaAutomaticaAsync(letra, _log.Info));
                    await Fase("Configurar o arranque", () => PartitionBootService.ConfigurarArranqueAsync(letra, s.Uefi, _log.Info));
                    await Fase("Ocultar", async () =>
                    {
                        await PartitionBootService.OcultarAsync(letra, _log.Info);
                        await IsoService.DesmontarAsync(_caminhoIso!);
                    });
                    _caminhoIso = null;
                    relatar.Report(100);

                    TxtTituloExec.Text = "Pronto para reiniciar";
                    TxtStatusExec.Text = "";
                    MostrarConclusao(true, "Tudo preparado. Ao reiniciar, abre o instalador oficial do Windows.",
                        "1. Escolha o idioma e carregue em Seguinte e Instalar.\n" +
                        "2. Escolha a instalação personalizada para ver a lista de discos.\n" +
                        $"3. Apague a partição do Windows antigo ({s.UnidadeSistema}) e instale no espaço não alocado.\n" +
                        "4. Não apague a partição VINOVIM_BOOT: é de lá que vêm os ficheiros. É removida sozinha no fim.");
                    BtnReiniciar.Visibility = Visibility.Visible;
                    break;
                }

                case ModoFormatacao.ReinstalacaoDireta:
                    await Fase("Iniciar o instalador", () =>
                    {
                        _log.Info($"\"{SetupService.CaminhoSetup(_raizIso!)}\" {SetupService.MontarArgumentos(Tipo)}");
                        SetupService.Iniciar(_raizIso!, Tipo);
                        return Task.CompletedTask;
                    });
                    _setupIniciado = true;
                    relatar.Report(100);
                    TxtTituloExec.Text = "Instalador iniciado";
                    TxtStatusExec.Text = "";
                    MostrarConclusao(true, "O instalador oficial do Windows está a trabalhar.",
                        "Primeiro prepara os ficheiros, depois reinicia o PC várias vezes. Pode demorar de 30 a 90 minutos.\n" +
                        "Não desligue o computador e não ejete a ISO. Pode fechar esta janela.");
                    break;

                case ModoFormatacao.Reparar:
                {
                    var dism = FaseQueComeca("DISM");
                    var sfc = FaseQueComeca("SFC");
                    var winre = FaseQueComeca("Ativar");
                    dism.Estado = EstadoFase.EmCurso;
                    var reparar = new Progress<double>(p =>
                    {
                        ProgressoExec.Value = p;
                        var atual = p < 60 ? dism : p < 95 ? sfc : winre;
                        foreach (var f in new[] { dism, sfc, winre })
                        {
                            if (f == atual) { f.Estado = EstadoFase.EmCurso; TxtTituloExec.Text = f.Nome + "…"; }
                            else if (Array.IndexOf(new[] { dism, sfc, winre }, f) < Array.IndexOf(new[] { dism, sfc, winre }, atual)) f.Estado = EstadoFase.Feito;
                        }
                        if (atual == dism) dism.Detalhe = $"{p / 60 * 100:0}%";
                        else if (atual == sfc) { dism.Detalhe = ""; sfc.Detalhe = $"{(p - 60) / 35 * 100:0}%"; }
                        else sfc.Detalhe = "";
                    });
                    var ok = await RepairService.RepararAsync(_raizIso, _log.Info, reparar);
                    foreach (var f in new[] { dism, sfc, winre }) { f.Estado = EstadoFase.Feito; f.Detalhe = ""; }
                    if (!ok) winre.Detalhe = "com avisos";
                    TxtTituloExec.Text = ok ? "Reparação concluída" : "Reparação concluída com avisos";
                    TxtStatusExec.Text = "";
                    MostrarConclusao(ok, ok ? "O sistema foi reparado." : "Alguns passos não correram bem.",
                        ok ? "Reinicie o PC. Depois, Definições, Sistema, Recuperação, Repor este PC deve voltar a funcionar."
                           : "Veja o registo detalhado. Se o Repor este PC continuar avariado, use o Modo 1 ou o Modo 2.");
                    BtnReiniciar.Visibility = Visibility.Visible;
                    break;
                }
            }
            _log.Info("=== Concluído ===");
        }
        catch (Exception ex)
        {
            TxtTituloExec.Text = "Ocorreu um erro";
            TxtStatusExec.Text = "";
            _log.Info("ERRO: " + ex);
            foreach (var f in _fases.Where(f => f.Estado == EstadoFase.EmCurso)) f.Estado = EstadoFase.Erro;
            var desfeito = "";
            if (particaoCriada)
            {
                _log.Info("A desfazer: remover a partição temporária…");
                desfeito = await PartitionBootService.LimparAsync(_log.Info)
                    ? "\nA partição temporária foi removida e o disco ficou como estava."
                    : "\nNão foi possível remover a partição temporária: abra a app de novo e use \"Remover partição\" no passo 2.";
            }
            MostrarConclusao(false, "A operação parou antes de alterar o arranque.", ex.Message + desfeito);
            TxtLog.Visibility = Visibility.Visible;
            BtnMostrarLog.Content = "Ocultar registo detalhado";
        }
        finally
        {
            Ocupado(false);
            DesenharPassos();
        }
    }

    private void Simular()
    {
        var s = _sistema!;
        _log.Info("Ponto de restauro: Checkpoint-Computer -Description 'Vinovim Windows Format'");
        switch (Modo)
        {
            case ModoFormatacao.ParticaoTemporaria:
                var tamanho = PartitionBootService.CalcularTamanho(new FileInfo(_caminhoIso!).Length);
                _log.Info($"Resize-Partition {s.UnidadeSistema} (−{tamanho / 1024d / 1024 / 1024:0.0} GB) + New-Partition NTFS '{PartitionBootService.Rotulo}'");
                _log.Info($"robocopy {_raizIso} V:\\ /E");
                _log.Info("sources\\$OEM$\\$$\\Setup\\Scripts\\SetupComplete.cmd → limpeza automática");
                _log.Info("bcdedit {ramdiskoptions} + osloader ramdisk=[V:]\\sources\\boot.wim (winpe) + /bootsequence");
                break;
            case ModoFormatacao.ReinstalacaoDireta:
                _log.Info($"\"{SetupService.CaminhoSetup(_raizIso!)}\" {SetupService.MontarArgumentos(Tipo)}");
                break;
            default:
                _log.Info("dism /Online /Cleanup-Image /RestoreHealth" + (_raizIso is null ? "" : $"  (fonte alternativa: {_raizIso}sources)"));
                _log.Info("sfc /scannow");
                _log.Info("reagentc /enable");
                break;
        }
        foreach (var f in _fases) { f.Estado = EstadoFase.Saltada; f.Detalhe = "teste"; }
        TxtTituloExec.Text = "Teste concluído";
        TxtStatusExec.Text = "";
        ProgressoExec.Value = 100;
        MostrarConclusao(true, "Modo de teste: nada foi alterado.",
            "O registo detalhado mostra cada comando que seria executado. Para executar a sério, feche a app, abra-a de novo e desligue o modo de teste.");
        TxtLog.Visibility = Visibility.Visible;
        BtnMostrarLog.Content = "Ocultar registo detalhado";
        _log.Info("=== Modo de teste: nada foi alterado ===");
    }

    private void BtnMostrarLog_Click(object sender, RoutedEventArgs e)
    {
        var mostrar = TxtLog.Visibility != Visibility.Visible;
        TxtLog.Visibility = mostrar ? Visibility.Visible : Visibility.Collapsed;
        BtnMostrarLog.Content = mostrar ? "Ocultar registo detalhado" : "Mostrar registo detalhado";
        if (mostrar) { Animar(TxtLog); TxtLog.ScrollToEnd(); }
    }

    private void BtnReiniciar_Click(object sender, RoutedEventArgs e) => PerguntarReiniciar();

    private void PerguntarReiniciar()
    {
        var r = MessageBox.Show("Guardar o trabalho aberto e reiniciar o PC agora?", "Vinovim Windows Format",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(IOPath.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /t 5") { CreateNoWindow = true, UseShellExecute = false });
    }

    private void BtnAbrirLog_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_log.Arquivo) { UseShellExecute = true });
}

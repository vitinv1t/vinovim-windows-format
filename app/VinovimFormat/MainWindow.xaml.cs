using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using VinovimFormat.Models;
using VinovimFormat.Services;
using IOPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace VinovimFormat;

public partial class MainWindow : Window
{
    private const string UrlWin11 = "https://www.microsoft.com/software-download/windows11";
    private const string UrlWin11Arm = "https://www.microsoft.com/software-download/windows11arm64";
    private const string UrlWin10 = "https://www.microsoft.com/software-download/windows10";

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

    public MainWindow()
    {
        InitializeComponent();
        var versao = Assembly.GetExecutingAssembly().GetName().Version;
        TxtVersao.Text = $"Versão {versao?.Major}.{versao?.Minor}.{versao?.Build}\nRegisto: {IOPath.GetFileName(_log.Arquivo)}";
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
        _log.Info("Vinovim Windows Format iniciado.");
        IrPara(0);
        await DiagnosticarAsync();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_fechar) return;
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

        Rolagem.ScrollToTop();
        DesenharPassos();
        AtualizarBotoes();
    }

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
            : "A ISO faz o papel do pen-drive. Descarregue-a dos servidores da Microsoft (opção \"Transferir imagem de disco (ISO)\"), no mesmo idioma e arquitetura do seu Windows.";
        if (_iso is not null && _sistema is not null) MostrarCompatibilidade();
    }

    private static void Abrir(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void BtnBaixar11_Click(object sender, RoutedEventArgs e) => Abrir(UrlWin11);
    private void BtnBaixar11Arm_Click(object sender, RoutedEventArgs e) => Abrir(UrlWin11Arm);
    private void BtnBaixar10_Click(object sender, RoutedEventArgs e) => Abrir(UrlWin10);

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
        TxtResumoFinal.Text = $"{modo}\nPC: {s.NomeProduto} {s.EdicaoAmigavel} {s.VersaoExibicao} · ISO: {iso}\nAntes de começar é criado um ponto de restauro do sistema.";

        var formatar = Modo != ModoFormatacao.Reparar;
        PainelPalavra.Visibility = formatar ? Visibility.Visible : Visibility.Collapsed;
        ChkBackup.Visibility = formatar ? Visibility.Visible : Visibility.Collapsed;
        ChkBitLocker.Visibility = formatar && s.BitLocker == BitLockerEstado.Ligado ? Visibility.Visible : Visibility.Collapsed;
        TxtConfirmacao.Text = "";
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
        VistaConfirmar.Visibility = Visibility.Collapsed;
        VistaExecutar.Visibility = Visibility.Visible;
        Ocupado(true);
        DesenharPassos();

        var progresso = new Progress<double>(p => ProgressoExec.Value = p);
        var particaoCriada = false;
        try
        {
            _log.Info($"=== Início: {Modo}{(Modo == ModoFormatacao.ReinstalacaoDireta ? $" ({Tipo})" : "")}{(simular ? " · MODO DE TESTE" : "")} ===");
            if (simular) { Simular(); return; }

            TxtTituloExec.Text = "A criar ponto de restauro…";
            await SafetyService.CriarPontoRestauroAsync(_log.Info);
            ((IProgress<double>)progresso).Report(3);

            if (Modo != ModoFormatacao.Reparar && _sistema!.BitLocker == BitLockerEstado.Ligado)
            {
                var b = await BackupService.SuspenderBitLockerAsync(_sistema.UnidadeSistema);
                _log.Info(b.Sucesso ? "BitLocker suspenso durante os próximos reinícios." : "Aviso: não foi possível suspender o BitLocker.");
            }

            switch (Modo)
            {
                case ModoFormatacao.ParticaoTemporaria:
                {
                    var s = _sistema!;
                    TxtTituloExec.Text = "A criar a partição temporária…";
                    TxtStatusExec.Text = "Isto pode demorar alguns minutos.";
                    var tamanho = PartitionBootService.CalcularTamanho(new FileInfo(_caminhoIso!).Length);
                    var letra = await PartitionBootService.CriarParticaoAsync(tamanho, s.UnidadeSistema, _log.Info);
                    particaoCriada = true;
                    ((IProgress<double>)progresso).Report(8);

                    TxtTituloExec.Text = "A copiar o instalador do Windows…";
                    var copia = new Progress<double>(p => ProgressoExec.Value = 8 + p * 0.8);
                    await PartitionBootService.CopiarIsoAsync(_raizIso!, letra, _log.Info, copia);
                    await PartitionBootService.EscreverLimpezaAutomaticaAsync(letra, _log.Info);

                    TxtTituloExec.Text = "A configurar o arranque…";
                    await PartitionBootService.ConfigurarArranqueAsync(letra, s.Uefi, _log.Info);
                    await PartitionBootService.OcultarAsync(letra, _log.Info);
                    await IsoService.DesmontarAsync(_caminhoIso!);
                    _caminhoIso = null;
                    ((IProgress<double>)progresso).Report(100);

                    TxtTituloExec.Text = "Pronto para reiniciar";
                    TxtStatusExec.Text =
                        "Ao reiniciar, o PC abre o instalador oficial do Windows.\n" +
                        "1. Escolha o idioma e clique em Instalar.\n" +
                        "2. Escolha a instalação Personalizada (ou \"Instalar o Windows\" e depois a lista de discos).\n" +
                        $"3. Apague a partição do Windows antigo ({s.UnidadeSistema}) e instale no espaço não alocado.\n" +
                        "4. Não apague a partição VINOVIM_BOOT: é de lá que vêm os ficheiros. Ela é removida sozinha no fim.";
                    BtnReiniciar.Visibility = Visibility.Visible;
                    break;
                }

                case ModoFormatacao.ReinstalacaoDireta:
                    TxtTituloExec.Text = "A iniciar o instalador do Windows…";
                    _log.Info($"\"{SetupService.CaminhoSetup(_raizIso!)}\" {SetupService.MontarArgumentos(Tipo)}");
                    SetupService.Iniciar(_raizIso!, Tipo);
                    _setupIniciado = true;
                    ((IProgress<double>)progresso).Report(100);
                    TxtTituloExec.Text = "Instalador iniciado";
                    TxtStatusExec.Text = "O instalador oficial está a preparar os ficheiros e depois reinicia o PC várias vezes. Pode demorar de 30 a 90 minutos. Não desligue o computador e não ejete a ISO.";
                    break;

                case ModoFormatacao.Reparar:
                    TxtTituloExec.Text = "A reparar o Windows…";
                    TxtStatusExec.Text = "DISM, SFC e Ambiente de Recuperação. Não desligue o PC.";
                    var ok = await RepairService.RepararAsync(_raizIso, _log.Info, progresso);
                    TxtTituloExec.Text = ok ? "Reparação concluída" : "Reparação concluída com avisos";
                    TxtStatusExec.Text = ok
                        ? "Reinicie o PC. Depois, \"Definições → Sistema → Recuperação → Repor este PC\" deve voltar a funcionar."
                        : "Alguns passos falharam (veja o registo). Se o \"Repor este PC\" continuar avariado, use o Modo 1 ou o Modo 2.";
                    BtnReiniciar.Visibility = Visibility.Visible;
                    break;
            }
            _log.Info("=== Concluído ===");
        }
        catch (Exception ex)
        {
            TxtTituloExec.Text = "Ocorreu um erro";
            TxtStatusExec.Text = ex.Message;
            _log.Info("ERRO: " + ex);
            if (particaoCriada)
            {
                _log.Info("A desfazer: remover a partição temporária…");
                await PartitionBootService.LimparAsync(_log.Info);
            }
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
        TxtTituloExec.Text = "Teste concluído";
        TxtStatusExec.Text = "Nada foi alterado. Feche a aplicação e abra-a de novo para executar a sério.";
        ProgressoExec.Value = 100;
        _log.Info("=== Modo de teste: nada foi alterado ===");
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

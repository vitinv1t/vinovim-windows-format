using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using VinovimFormat.Services;

namespace VinovimFormat;

/// <summary>
/// Janela com a página oficial de download da Microsoft (WebView2).
/// Quando a pessoa carrega em descarregar uma ISO, o ficheiro vai para a pasta escolhida
/// e a janela esconde-se; o progresso passa a aparecer na janela principal.
/// </summary>
public partial class DownloadIsoWindow : Window
{
    private readonly ProdutoWindows _produto;
    private readonly bool _arm;
    private readonly string _pasta;

    /// <summary>Disparado quando começa o download de uma ISO (já com o caminho final).</summary>
    public event Action<CoreWebView2DownloadOperation, string>? DownloadIniciado;

    public DownloadIsoWindow(ProdutoWindows produto, bool arm, string pasta)
    {
        _produto = produto;
        _arm = arm;
        _pasta = pasta;
        InitializeComponent();
        TxtTitulo.Text = produto == ProdutoWindows.Windows10
            ? "Windows 10 · página oficial da Microsoft"
            : arm ? "Windows 11 para ARM64 · página oficial da Microsoft" : "Windows 11 · página oficial da Microsoft";
        TxtInstrucoes.Text = arm
            ? "Desça até \"Transferir imagem de disco (ISO)\", escolha o idioma e carregue em Download. A app guarda a ISO e trata do resto."
            : "Desça até \"Transferir imagem de disco (ISO)\", escolha a edição e o idioma e carregue em \"64-bit Download\". A app guarda a ISO e trata do resto.";
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamanho);

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var escuro = 1;
            DwmSetWindowAttribute(hwnd, 20, ref escuro, sizeof(int));
            var cor = 0x00151212; // #121215
            DwmSetWindowAttribute(hwnd, 35, ref cor, sizeof(int));
        }
        catch { /* barra padrão */ }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var dados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vinovim", "WebView2");
            var ambiente = await CoreWebView2Environment.CreateAsync(null, dados);
            await Navegador.EnsureCoreWebView2Async(ambiente);

            var core = Navegador.CoreWebView2;
            if (IsoDownloadService.PrecisaAgenteSemWindows(_produto))
                core.Settings.UserAgent = IsoDownloadService.AgenteSemWindows;
            core.Settings.AreDevToolsEnabled = false;
            core.DownloadStarting += Core_DownloadStarting;
            core.NewWindowRequested += (_, a) => { a.Handled = true; core.Navigate(a.Uri); };
            core.NavigationCompleted += (_, _) => TxtCarregar.Visibility = Visibility.Collapsed;
            core.Navigate(IsoDownloadService.PaginaOficial(_produto, _arm));
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MostrarErro("O componente WebView2 do Microsoft Edge não está instalado. Use a página oficial no seu browser.");
        }
        catch (Exception ex)
        {
            MostrarErro("Não foi possível abrir a página da Microsoft: " + ex.Message);
        }
    }

    private void MostrarErro(string texto)
    {
        TxtCarregar.Text = texto;
        TxtCarregar.Visibility = Visibility.Visible;
    }

    private void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var nome = Path.GetFileName(e.ResultFilePath);
        if (!nome.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            MessageBox.Show(this, "Esse ficheiro não é uma imagem ISO do Windows. Escolha a opção \"Transferir imagem de disco (ISO)\".",
                "Vinovim Windows Format", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destino = Path.Combine(_pasta, nome);
        e.ResultFilePath = destino;
        e.Handled = true; // sem a barra de downloads do browser: o progresso aparece na app
        DownloadIniciado?.Invoke(e.DownloadOperation, destino);
        Hide(); // continua viva em segundo plano até o download acabar
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => Close();
}

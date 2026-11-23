namespace VinovimFormat.Services;

public enum ProdutoWindows { Windows11, Windows10 }

public sealed record ProgressoDownload(long Recebidos, long Total, double BytesPorSegundo);

/// <summary>
/// Páginas oficiais de download da Microsoft. A app abre-as numa janela própria (WebView2):
/// a pessoa escolhe a edição e o idioma na página verdadeira e a app trata do ficheiro.
/// </summary>
public static class IsoDownloadService
{
    /// <summary>
    /// Num browser em Windows, a página do Windows 10 mostra a Ferramenta de Criação de Multimédia em vez da ISO.
    /// Para essa página usa-se um identificador de browser de outro sistema, que é o que faz a Microsoft mostrar a ISO.
    /// </summary>
    public const string AgenteSemWindows =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";

    public static string PaginaOficial(ProdutoWindows produto, bool arm) => produto switch
    {
        ProdutoWindows.Windows11 when arm => "https://www.microsoft.com/software-download/windows11arm64",
        ProdutoWindows.Windows11 => "https://www.microsoft.com/software-download/windows11",
        _ => "https://www.microsoft.com/software-download/windows10ISO",
    };

    public static bool PrecisaAgenteSemWindows(ProdutoWindows produto) => produto == ProdutoWindows.Windows10;
}

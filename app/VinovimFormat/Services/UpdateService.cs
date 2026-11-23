using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace VinovimFormat.Services;

public sealed record Atualizacao(Version Versao, string Etiqueta, string Pagina);

/// <summary>Ao abrir, pergunta ao GitHub se existe uma versão mais recente da app.</summary>
public static class UpdateService
{
    public const string Repositorio = "vitinv1t/vinovim-windows-format";
    public const string Site = "https://vinovimwindows.vercel.app/#download";

    public static Version VersaoAtual =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Devolve a versão nova, ou null se esta já é a mais recente (ou sem internet).</summary>
    public static async Task<Atualizacao?> VerificarAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"VinovimWindowsFormat/{VersaoAtual}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repositorio}/releases/latest").ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var etiqueta = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(etiqueta.TrimStart('v', 'V'), out var remota)) return null;

            var atual = VersaoAtual;
            var atual3 = new Version(atual.Major, atual.Minor, Math.Max(0, atual.Build));
            var remota3 = new Version(remota.Major, remota.Minor, Math.Max(0, remota.Build));
            return remota3 > atual3 ? new Atualizacao(remota3, etiqueta, Site) : null;
        }
        catch
        {
            return null;
        }
    }
}

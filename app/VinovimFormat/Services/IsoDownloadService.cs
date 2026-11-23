using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VinovimFormat.Services;

public enum ProdutoWindows { Windows11, Windows10 }

public sealed record IdiomaIso(string SkuId, string Nome, string NomeLocalizado)
{
    public override string ToString() => NomeLocalizado;
}

public sealed record ProgressoDownload(long Recebidos, long Total, double BytesPorSegundo);

/// <summary>
/// Descarrega a ISO oficial diretamente dos servidores da Microsoft, pelo mesmo serviço
/// usado pelas páginas "Transferir o Windows" (software-download-connector).
/// Se a Microsoft recusar o pedido, a app volta a oferecer a página oficial.
/// </summary>
public static partial class IsoDownloadService
{
    private const string Perfil = "606624d44113";
    private const string Api = "https://www.microsoft.com/software-download-connector/api";
    private const string AgenteNavegador =
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = CriarCliente();

    [GeneratedRegex(@"<option[^>]*value=""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RegexEdicao();

    private static HttpClient CriarCliente()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(AgenteNavegador);
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return c;
    }

    public sealed class Sessao
    {
        internal Sessao(ProdutoWindows produto, string pagina, string edicao)
        {
            Produto = produto;
            Pagina = pagina;
            Edicao = edicao;
        }

        public ProdutoWindows Produto { get; }
        public string Pagina { get; }
        public string Edicao { get; }
        public string Id { get; } = Guid.NewGuid().ToString();
    }

    public static string PaginaOficial(ProdutoWindows produto, bool arm) => produto switch
    {
        ProdutoWindows.Windows11 when arm => "https://www.microsoft.com/software-download/windows11arm64",
        ProdutoWindows.Windows11 => "https://www.microsoft.com/software-download/windows11",
        _ => "https://www.microsoft.com/software-download/windows10ISO",
    };

    /// <summary>Abre a sessão e lista os idiomas disponíveis para a ISO escolhida.</summary>
    public static async Task<(Sessao Sessao, List<IdiomaIso> Idiomas)> ObterIdiomasAsync(ProdutoWindows produto, bool arm, CancellationToken ct)
    {
        var pagina = PaginaOficial(produto, arm);
        var html = await TextoAsync(pagina, null, ct).ConfigureAwait(false);
        var edicao = RegexEdicao().Matches(html).Select(m => m.Groups[1].Value).FirstOrDefault()
                     ?? throw new InvalidOperationException("A página da Microsoft não indicou nenhuma edição para descarregar.");

        var sessao = new Sessao(produto, pagina, edicao);

        // Regista a sessão no serviço da Microsoft (o mesmo que a página faz no browser).
        try { await TextoAsync($"https://vlscppe.microsoft.com/tags?org_id=y6jn8c31&session_id={sessao.Id}", null, ct).ConfigureAwait(false); }
        catch { /* não é obrigatório */ }

        var json = await TextoAsync(
            $"{Api}/getskuinformationbyproductedition?profile={Perfil}&ProductEditionId={edicao}&SKU=undefined&friendlyFileName=undefined&Locale=en-US&sessionID={sessao.Id}",
            pagina, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        LancarSeErro(doc.RootElement);
        var idiomas = new List<IdiomaIso>();
        if (doc.RootElement.TryGetProperty("Skus", out var skus))
        {
            foreach (var s in skus.EnumerateArray())
            {
                var id = s.GetProperty("Id").GetString();
                var nome = s.GetProperty("Language").GetString();
                var local = s.TryGetProperty("LocalizedLanguage", out var l) ? l.GetString() : nome;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(nome))
                    idiomas.Add(new IdiomaIso(id, nome, local ?? nome));
            }
        }
        if (idiomas.Count == 0) throw new InvalidOperationException("A Microsoft não devolveu nenhum idioma.");
        return (sessao, idiomas.OrderBy(i => i.NomeLocalizado, StringComparer.CurrentCulture).ToList());
    }

    /// <summary>Escolhe o idioma da ISO que corresponde ao idioma do Windows instalado.</summary>
    public static IdiomaIso? IdiomaRecomendado(IEnumerable<IdiomaIso> idiomas, string cultura)
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pt-PT"] = "Portuguese", ["pt-BR"] = "Brazilian Portuguese",
            ["en-US"] = "English", ["en-GB"] = "English International",
            ["es-ES"] = "Spanish", ["es-MX"] = "Spanish (Mexico)",
            ["fr-FR"] = "French", ["fr-CA"] = "French Canadian",
            ["de-DE"] = "German", ["it-IT"] = "Italian", ["nl-NL"] = "Dutch",
            ["pl-PL"] = "Polish", ["ru-RU"] = "Russian", ["tr-TR"] = "Turkish",
            ["uk-UA"] = "Ukrainian", ["ro-RO"] = "Romanian", ["cs-CZ"] = "Czech",
            ["ja-JP"] = "Japanese", ["ko-KR"] = "Korean",
            ["zh-CN"] = "Chinese Simplified", ["zh-TW"] = "Chinese Traditional",
        };
        var lista = idiomas.ToList();
        if (mapa.TryGetValue(cultura, out var nome))
        {
            var exato = lista.FirstOrDefault(i => string.Equals(i.Nome, nome, StringComparison.OrdinalIgnoreCase));
            if (exato is not null) return exato;
        }
        string? ingles = null;
        try { ingles = new CultureInfo(cultura).Parent.EnglishName; } catch { /* cultura desconhecida */ }
        return lista.FirstOrDefault(i => string.Equals(i.Nome, ingles, StringComparison.OrdinalIgnoreCase))
               ?? lista.FirstOrDefault(i => string.Equals(i.Nome, "English International", StringComparison.OrdinalIgnoreCase))
               ?? lista.FirstOrDefault();
    }

    /// <summary>Pede o link de download (válido por 24 h) para o idioma e a arquitetura.</summary>
    public static async Task<Uri> ObterLinkAsync(Sessao sessao, IdiomaIso idioma, string arquitetura, CancellationToken ct)
    {
        var json = await TextoAsync(
            $"{Api}/GetProductDownloadLinksBySku?profile={Perfil}&ProductEditionId=undefined&SKU={idioma.SkuId}&friendlyFileName=undefined&Locale=en-US&sessionID={sessao.Id}",
            sessao.Pagina, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        LancarSeErro(doc.RootElement);

        var links = new List<string>();
        if (doc.RootElement.TryGetProperty("ProductDownloadOptions", out var opcoes))
            foreach (var o in opcoes.EnumerateArray())
                if (o.TryGetProperty("Uri", out var u) && u.GetString() is { Length: > 0 } s) links.Add(s);

        if (links.Count == 0) throw new InvalidOperationException("A Microsoft não devolveu nenhum link de download.");

        // Windows 10 devolve 32 e 64 bits; Windows 11 devolve só a arquitetura da página.
        var marca = arquitetura switch { "arm64" => "arm64", "x86" => "x32", _ => "x64" };
        var link = links.FirstOrDefault(l => NomeFicheiro(new Uri(l)).Contains(marca, StringComparison.OrdinalIgnoreCase)) ?? links[0];
        return new Uri(link);
    }

    public static string NomeFicheiro(Uri link)
    {
        var nome = Path.GetFileName(Uri.UnescapeDataString(link.AbsolutePath));
        return string.IsNullOrWhiteSpace(nome) || !nome.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) ? "Windows.iso" : nome;
    }

    /// <summary>Descarrega para "destino" (via ficheiro .part, retomável) e reporta o progresso.</summary>
    public static async Task DescarregarAsync(Uri link, string destino, IProgress<ProgressoDownload> progresso, CancellationToken ct)
    {
        var parcial = destino + ".part";
        long inicio = File.Exists(parcial) ? new FileInfo(parcial).Length : 0;

        using var pedido = new HttpRequestMessage(HttpMethod.Get, link);
        if (inicio > 0) pedido.Headers.Range = new RangeHeaderValue(inicio, null);

        using var resposta = await Http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (inicio > 0 && resposta.StatusCode != HttpStatusCode.PartialContent)
            inicio = 0; // o servidor não aceitou retomar: começa do zero
        resposta.EnsureSuccessStatusCode();

        var total = (resposta.Content.Headers.ContentLength ?? 0) + inicio;
        await using var origem = await resposta.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (var ficheiro = new FileStream(parcial, inicio > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
        {
            var buffer = new byte[1 << 20];
            long recebidos = inicio, ultimoRelatorio = 0, bytesJanela = 0;
            var relogio = Stopwatch.StartNew();
            var janela = Stopwatch.StartNew();
            double velocidade = 0;
            int n;
            while ((n = await origem.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await ficheiro.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                recebidos += n;
                bytesJanela += n;
                if (janela.ElapsedMilliseconds >= 1000)
                {
                    var atual = bytesJanela / janela.Elapsed.TotalSeconds;
                    velocidade = velocidade == 0 ? atual : velocidade * 0.7 + atual * 0.3;
                    bytesJanela = 0;
                    janela.Restart();
                }
                if (relogio.ElapsedMilliseconds - ultimoRelatorio >= 250)
                {
                    ultimoRelatorio = relogio.ElapsedMilliseconds;
                    progresso.Report(new ProgressoDownload(recebidos, total, velocidade));
                }
            }
            progresso.Report(new ProgressoDownload(recebidos, total, velocidade));
        }

        if (File.Exists(destino)) File.Delete(destino);
        File.Move(parcial, destino);
    }

    private static async Task<string> TextoAsync(string url, string? referer, CancellationToken ct)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer is not null) pedido.Headers.Referrer = new Uri(referer);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        using var r = await Http.SendAsync(pedido, cts.Token).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
    }

    private static void LancarSeErro(JsonElement raiz)
    {
        if (!raiz.TryGetProperty("Errors", out var erros) || erros.ValueKind != JsonValueKind.Array || erros.GetArrayLength() == 0)
            return;
        var texto = string.Join(" ", erros.EnumerateArray()
            .Select(e => e.TryGetProperty("Value", out var v) ? v.GetString() : null)
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        if (texto.Contains("715-123130"))
            throw new InvalidOperationException("A Microsoft bloqueou temporariamente o download direto a partir desta rede (erro 715-123130). Desligue a VPN, se usar, ou use a página oficial.");
        throw new InvalidOperationException("A Microsoft recusou o pedido: " + (texto.Length > 0 ? texto : "erro desconhecido."));
    }
}

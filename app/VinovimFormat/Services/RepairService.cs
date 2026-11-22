using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static VinovimFormat.Services.ProcessRunner;

namespace VinovimFormat.Services;

/// <summary>
/// Modo 3: repara a imagem do Windows e o "Restaurar o PC" sem apagar nada.
/// DISM /RestoreHealth → sfc /scannow → reagentc /enable.
/// </summary>
public static partial class RepairService
{
    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d)?)\s?%")]
    private static partial Regex RegexPercentagem();

    /// <param name="progresso">Progresso total de 0 a 100 (DISM 0–60, SFC 60–95, reagentc 95–100).</param>
    public static async Task<bool> RepararAsync(string? raizIso, Action<string> log, IProgress<double> progresso)
    {
        var sistema = Environment.SystemDirectory;
        var ok = true;

        // 1. DISM
        log("1/3  DISM: a reparar a imagem do Windows (pode demorar 10–30 minutos)...");
        var dism = await ExecutarAsync(Path.Combine(sistema, "dism.exe"),
            "/Online /Cleanup-Image /RestoreHealth",
            l => Relatar(l, 0, 60, log, progresso)).ConfigureAwait(false);

        if (!dism.Sucesso && raizIso is not null)
        {
            var wim = new[] { "install.wim", "install.esd" }
                .Select(n => Path.Combine(raizIso, "sources", n)).FirstOrDefault(File.Exists);
            if (wim is not null)
            {
                var tipo = wim.EndsWith(".esd", StringComparison.OrdinalIgnoreCase) ? "esd" : "wim";
                log($"DISM online falhou (código {dism.CodigoSaida}). A tentar de novo usando a ISO como fonte...");
                dism = await ExecutarAsync(Path.Combine(sistema, "dism.exe"),
                    $"/Online /Cleanup-Image /RestoreHealth /Source:{tipo}:{wim}:1 /LimitAccess",
                    l => Relatar(l, 0, 60, log, progresso)).ConfigureAwait(false);
            }
        }
        if (dism.Sucesso) log("DISM concluído sem erros.");
        else { ok = false; log($"DISM terminou com o código {dism.CodigoSaida}. Detalhes em C:\\Windows\\Logs\\DISM\\dism.log."); }
        progresso.Report(60);

        // 2. SFC (a saída do sfc é UTF-16)
        log("2/3  SFC: a verificar ficheiros do sistema...");
        var sfc = await ExecutarAsync(Path.Combine(sistema, "sfc.exe"), "/scannow",
            l => Relatar(l, 60, 95, log, progresso), Encoding.Unicode).ConfigureAwait(false);
        log(sfc.Sucesso ? "SFC concluído." : $"SFC terminou com o código {sfc.CodigoSaida}. Detalhes em C:\\Windows\\Logs\\CBS\\CBS.log.");
        progresso.Report(95);

        // 3. Ambiente de recuperação
        log("3/3  A ativar o Ambiente de Recuperação do Windows (reagentc)...");
        var re = await ExecutarAsync(Path.Combine(sistema, "reagentc.exe"), "/enable", log).ConfigureAwait(false);
        if (!re.Sucesso)
        {
            ok = false;
            log("Não foi possível ativar o WinRE. Normalmente falta o ficheiro Winre.wim; o Modo 1 ou 2 reinstala-o.");
        }
        await ExecutarAsync(Path.Combine(sistema, "reagentc.exe"), "/info", log).ConfigureAwait(false);
        progresso.Report(100);
        return ok;
    }

    private static void Relatar(string linha, double inicio, double fim, Action<string> log, IProgress<double> progresso)
    {
        var m = RegexPercentagem().Match(linha);
        if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
        {
            progresso.Report(inicio + (fim - inicio) * Math.Clamp(p, 0, 100) / 100);
            return; // não encher o log com linhas de percentagem
        }
        log(linha.Trim());
    }
}

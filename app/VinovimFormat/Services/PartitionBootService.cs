using System.Management;
using System.Text.RegularExpressions;
using static VinovimFormat.Services.ProcessRunner;

namespace VinovimFormat.Services;

/// <summary>
/// Modo 1: cria a partição temporária VINOVIM_BOOT no próprio disco, copia a ISO para lá
/// e configura o BCD para arrancar uma única vez no instalador oficial do Windows (boot.wim em ramdisk).
/// Funciona como um "pen-drive" dentro do próprio disco.
/// </summary>
public static partial class PartitionBootService
{
    public const string Rotulo = "VINOVIM_BOOT";
    public const string DescricaoBcd = "Vinovim Windows Format";
    private const long Mb = 1024L * 1024;
    private const long TamanhoMinimo = 8L * 1024 * Mb;

    [GeneratedRegex(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}")]
    private static partial Regex RegexGuid();

    /// <summary>Tamanho da partição: o maior entre 8 GB e o tamanho da ISO + 1 GB, arredondado a 1 MB.</summary>
    public static long CalcularTamanho(long tamanhoIso)
    {
        var bytes = Math.Max(TamanhoMinimo, tamanhoIso + 1024 * Mb);
        return (bytes + Mb - 1) / Mb * Mb;
    }

    public static bool ExisteParticaoSemLetra()
    {
        try
        {
            using var b = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                $"SELECT FileSystemLabel FROM MSFT_Volume WHERE FileSystemLabel='{Rotulo}'");
            return b.Get().Count > 0;
        }
        catch { return false; }
    }

    /// <summary>Reduz o C:, cria e formata a partição VINOVIM_BOOT. Devolve a letra atribuída (ex.: "V:").</summary>
    public static async Task<string> CriarParticaoAsync(long bytes, string unidadeSistema, Action<string> log)
    {
        var letraSistema = unidadeSistema.TrimEnd(':', '\\');
        log($"A reduzir {unidadeSistema} em {bytes / 1024d / 1024 / 1024:0.0} GB e a criar a partição {Rotulo}...");

        var script = $@"$ErrorActionPreference='Stop'
$bytes = [int64]{bytes}
$c = Get-Partition -DriveLetter {letraSistema}
$sup = Get-PartitionSupportedSize -DriveLetter {letraSistema}
$novo = [int64]([math]::Floor(($c.Size - $bytes) / 1MB) * 1MB)
if ($novo -lt $sup.SizeMin) {{
  throw (""Não é possível reduzir {letraSistema}: o suficiente. O Windows só permite reduzir até "" + [math]::Round($sup.SizeMin/1GB,1) + "" GB. Liberte espaço, desative a hibernação (powercfg /h off) e tente outra vez."")
}}
Resize-Partition -DriveLetter {letraSistema} -Size $novo
$c = Get-Partition -DriveLetter {letraSistema}
$offset = $c.Offset + $c.Size
$p = New-Partition -DiskNumber $c.DiskNumber -Offset $offset -Size $bytes -AssignDriveLetter
Format-Volume -Partition $p -FileSystem NTFS -NewFileSystemLabel '{Rotulo}' -Confirm:$false | Out-Null
$p = Get-Partition -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber
Write-Output $p.DriveLetter";

        var r = await PowerShellAsync(script).ConfigureAwait(false);
        var letra = r.Saida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (!r.Sucesso || string.IsNullOrEmpty(letra) || !char.IsLetter(letra[0]))
            throw new InvalidOperationException("Falha ao criar a partição temporária. " + LimparErroPs(r.Erro));

        log($"Partição {Rotulo} criada em {letra[0]}:.");
        return $"{letra[0]}:";
    }

    /// <summary>Copia todo o conteúdo da ISO montada para a partição, reportando a percentagem.</summary>
    public static async Task CopiarIsoAsync(string raizIso, string destino, Action<string> log, IProgress<double> progresso)
    {
        var opcoes = new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        var total = Directory.EnumerateFiles(raizIso, "*", opcoes).Sum(f => new FileInfo(f).Length);
        log($"A copiar os ficheiros de instalação ({total / 1024d / 1024 / 1024:0.0} GB)...");

        using var cts = new CancellationTokenSource();
        var monitor = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var copiado = Directory.EnumerateFiles(destino + "\\", "*", opcoes).Sum(f => new FileInfo(f).Length);
                    progresso.Report(Math.Min(100, copiado * 100d / Math.Max(1, total)));
                }
                catch { /* ficheiros a serem escritos */ }
                await Task.Delay(1500).ConfigureAwait(false);
            }
        });

        var r = await ExecutarAsync(
            Path.Combine(Environment.SystemDirectory, "robocopy.exe"),
            $"{raizIso} {destino}\\ /E /R:2 /W:2 /NP /NFL /NDL /NJH",
            null).ConfigureAwait(false);
        cts.Cancel();
        await monitor.ConfigureAwait(false);

        // robocopy: códigos de 0 a 7 = sucesso
        if (r.CodigoSaida >= 8)
            throw new InvalidOperationException($"A cópia falhou (robocopy {r.CodigoSaida}). {r.Saida.Trim()}");

        if (!File.Exists(Path.Combine(destino + "\\", "sources", "boot.wim")) || !File.Exists(Path.Combine(destino + "\\", "boot", "boot.sdi")))
            throw new InvalidOperationException("A cópia terminou, mas faltam sources\\boot.wim ou boot\\boot.sdi na partição.");

        progresso.Report(100);
        log("Cópia concluída.");
    }

    /// <summary>
    /// Coloca um SetupComplete.cmd em sources\$OEM$ para que o Windows novo, no fim da instalação,
    /// apague a partição temporária, devolva o espaço ao C: e remova a entrada de arranque.
    /// </summary>
    public static async Task EscreverLimpezaAutomaticaAsync(string destino, Action<string> log)
    {
        var pasta = Path.Combine(destino + "\\", "sources", "$OEM$", "$$", "Setup", "Scripts");
        Directory.CreateDirectory(pasta);
        await File.WriteAllTextAsync(Path.Combine(pasta, "VinovimLimpeza.ps1"), ScriptLimpeza).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(pasta, "SetupComplete.cmd"),
            "@echo off\r\n" +
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"%WINDIR%\\Setup\\Scripts\\VinovimLimpeza.ps1\" > \"%WINDIR%\\Setup\\Scripts\\VinovimLimpeza.log\" 2>&1\r\n" +
            "exit /b 0\r\n").ConfigureAwait(false);
        log("Limpeza automática pós-instalação preparada.");
    }

    /// <summary>Cria a entrada de arranque em ramdisk e marca-a para o próximo reinício (uma única vez).</summary>
    public static async Task<string> ConfigurarArranqueAsync(string letraParticao, bool uefi, Action<string> log)
    {
        var bcd = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
        async Task<ResultadoProcesso> Bcd(string args, bool obrigatorio = true)
        {
            var r = await ExecutarAsync(bcd, args).ConfigureAwait(false);
            log($"bcdedit {args}");
            if (obrigatorio && !r.Sucesso)
                throw new InvalidOperationException($"bcdedit falhou: {(r.Saida + r.Erro).Trim()}");
            return r;
        }

        log("A configurar o arranque (BCD)...");
        await Bcd($"/create {{ramdiskoptions}} /d \"{DescricaoBcd} Ramdisk\"", obrigatorio: false);
        await Bcd($"/set {{ramdiskoptions}} ramdisksdidevice partition={letraParticao}");
        await Bcd("/set {ramdiskoptions} ramdisksdipath \\boot\\boot.sdi");

        var criar = await Bcd($"/create /d \"{DescricaoBcd}\" /application osloader");
        var guid = RegexGuid().Match(criar.Saida).Value;
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException("Não foi possível ler o identificador da nova entrada de arranque.");

        var ramdisk = $"ramdisk=[{letraParticao}]\\sources\\boot.wim,{{ramdiskoptions}}";
        await Bcd($"/set {guid} device {ramdisk}");
        await Bcd($"/set {guid} osdevice {ramdisk}");
        await Bcd($"/set {guid} path \\windows\\system32\\boot\\{(uefi ? "winload.efi" : "winload.exe")}");
        await Bcd($"/set {guid} systemroot \\windows");
        await Bcd($"/set {guid} winpe yes");
        await Bcd($"/set {guid} detecthal yes");
        await Bcd($"/bootsequence {guid}");

        await File.WriteAllTextAsync(Path.Combine(letraParticao + "\\", "vinovim-boot.txt"), guid).ConfigureAwait(false);
        log($"Entrada {guid} criada: o próximo arranque abre o instalador do Windows.");
        return guid;
    }

    /// <summary>Volta a marcar a entrada existente para o próximo arranque.</summary>
    public static async Task<bool> RearmarArranqueAsync(Action<string> log)
    {
        var bcd = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
        var lista = await ExecutarAsync(bcd, "/enum all /v").ConfigureAwait(false);
        var bloco = BlocosBcd(lista.Saida).FirstOrDefault(b => b.Contains(DescricaoBcd) && !b.Contains(DescricaoBcd + " Ramdisk"));
        var guid = bloco is null ? "" : RegexGuid().Match(bloco).Value;
        if (string.IsNullOrEmpty(guid)) return false;
        var r = await ExecutarAsync(bcd, $"/bootsequence {guid}").ConfigureAwait(false);
        log($"bcdedit /bootsequence {guid}");
        return r.Sucesso;
    }

    /// <summary>Tira a letra de unidade à partição para ficar oculta no Explorador.</summary>
    public static async Task OcultarAsync(string letraParticao, Action<string> log)
    {
        var l = letraParticao.TrimEnd(':');
        var r = await PowerShellAsync($"$p = Get-Partition -DriveLetter {l}; Remove-PartitionAccessPath -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber -AccessPath '{l}:\\'").ConfigureAwait(false);
        log(r.Sucesso ? $"Partição {Rotulo} oculta (sem letra de unidade)." : "Aviso: não foi possível ocultar a partição; não afeta a instalação.");
    }

    /// <summary>Remove a partição VINOVIM_BOOT, devolve o espaço ao C: e apaga a entrada de arranque.</summary>
    public static async Task<bool> LimparAsync(Action<string> log)
    {
        log("A remover a partição temporária e a entrada de arranque...");
        var r = await PowerShellAsync(ScriptLimpeza).ConfigureAwait(false);
        foreach (var linha in r.Saida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            log(linha);
        if (!r.Sucesso) log("Erro: " + LimparErroPs(r.Erro));
        return r.Sucesso;
    }

    private static IEnumerable<string> BlocosBcd(string saida) =>
        Regex.Split(saida.Replace("\r\n", "\n"), @"\n\s*\n").Where(b => b.Contains('{'));

    /// <summary>Script de limpeza: usado pela app e copiado para o Windows novo (SetupComplete).</summary>
    public const string ScriptLimpeza = """
        $ErrorActionPreference = 'Continue'
        $rotulo = 'VINOVIM_BOOT'
        $vol = Get-Volume -FileSystemLabel $rotulo -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($vol) {
          $p = $vol | Get-Partition
          $disco = $p.DiskNumber
          Remove-Partition -DiskNumber $disco -PartitionNumber $p.PartitionNumber -Confirm:$false
          Write-Output "Partição $rotulo removida."
          $sist = $env:SystemDrive.TrimEnd(':')
          $max = (Get-PartitionSupportedSize -DriveLetter $sist).SizeMax
          $atual = (Get-Partition -DriveLetter $sist).Size
          if ($max -gt $atual) { Resize-Partition -DriveLetter $sist -Size $max; Write-Output "Espaço devolvido a $($env:SystemDrive)." }
        } else { Write-Output "Partição $rotulo não encontrada." }

        $saida = (& bcdedit.exe /enum all /v) -join "`n"
        $blocos = $saida -split "`n\s*`n"
        foreach ($b in $blocos) {
          if ($b -match 'Vinovim Windows Format' -and $b -match '(\{[0-9a-fA-F-]{36}\})') {
            & bcdedit.exe /delete $Matches[1] /f | Out-Null
            Write-Output "Entrada de arranque $($Matches[1]) removida."
          }
        }
        exit 0
        """;

    private static string LimparErroPs(string erro) =>
        string.Join(" ", erro.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("#< CLIXML") && !l.StartsWith("<Objs"))
            .Select(l => Regex.Replace(l, "<[^>]+>", " ").Replace("_x000D__x000A_", " "))).Trim();
}


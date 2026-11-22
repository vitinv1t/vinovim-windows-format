using System.Security.Cryptography;
using static VinovimFormat.Services.ProcessRunner;

namespace VinovimFormat.Services;

/// <summary>Ponto de restauro e verificação SHA-256 da ISO.</summary>
public static class SafetyService
{
    public static async Task<bool> CriarPontoRestauroAsync(Action<string> log)
    {
        log("A criar ponto de restauro do sistema...");
        var r = await PowerShellAsync("""
            $ErrorActionPreference = 'Stop'
            Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
            New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -PropertyType DWord -Force | Out-Null
            Checkpoint-Computer -Description 'Vinovim Windows Format' -RestorePointType MODIFY_SETTINGS
            """).ConfigureAwait(false);
        log(r.Sucesso ? "Ponto de restauro criado." : "Aviso: não foi possível criar o ponto de restauro (o Restauro do Sistema pode estar desativado). A continuar.");
        return r.Sucesso;
    }

    public static async Task<string> Sha256Async(string arquivo, IProgress<double> progresso, CancellationToken ct = default)
    {
        const int buffer = 4 * 1024 * 1024;
        await using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read, buffer, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var dados = new byte[buffer];
        long lido = 0;
        int n;
        while ((n = await fs.ReadAsync(dados, ct).ConfigureAwait(false)) > 0)
        {
            sha.AppendData(dados, 0, n);
            lido += n;
            progresso.Report(lido * 100d / Math.Max(1, fs.Length));
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }
}

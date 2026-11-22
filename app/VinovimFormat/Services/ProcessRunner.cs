using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VinovimFormat.Services;

public sealed record ResultadoProcesso(int CodigoSaida, string Saida, string Erro)
{
    public bool Sucesso => CodigoSaida == 0;
}

/// <summary>Executa programas do Windows (dism, netsh, manage-bde, PowerShell) e captura a saída.</summary>
public static class ProcessRunner
{
    public static string PowerShellExe =>
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    /// <summary>Codificação usada pelos programas de console (ex.: 850 em português).</summary>
    public static Encoding CodificacaoOem
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }
    }

    public static async Task<ResultadoProcesso> ExecutarAsync(
        string arquivo,
        string argumentos,
        Action<string>? aoReceberLinha = null,
        Encoding? codificacao = null,
        CancellationToken ct = default)
    {
        codificacao ??= CodificacaoOem;

        var psi = new ProcessStartInfo(arquivo, argumentos)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = codificacao,
            StandardErrorEncoding = codificacao,
        };

        var saida = new StringBuilder();
        var erro = new StringBuilder();

        using var processo = new Process { StartInfo = psi, EnableRaisingEvents = true };
        processo.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (saida) saida.AppendLine(e.Data);
            if (!string.IsNullOrWhiteSpace(e.Data)) aoReceberLinha?.Invoke(e.Data);
        };
        processo.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (erro) erro.AppendLine(e.Data);
            if (!string.IsNullOrWhiteSpace(e.Data)) aoReceberLinha?.Invoke(e.Data);
        };

        processo.Start();
        processo.BeginOutputReadLine();
        processo.BeginErrorReadLine();
        await processo.WaitForExitAsync(ct).ConfigureAwait(false);

        return new ResultadoProcesso(processo.ExitCode, saida.ToString(), erro.ToString());
    }

    /// <summary>Executa um script PowerShell (enviado codificado, sem problemas de aspas).</summary>
    public static Task<ResultadoProcesso> PowerShellAsync(string script, CancellationToken ct = default)
    {
        var completo =
            "$ProgressPreference='SilentlyContinue';" +
            "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;" +
            script;
        var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(completo));
        return ExecutarAsync(
            PowerShellExe,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {b64}",
            null, Encoding.UTF8, ct);
    }

    /// <summary>Converte um texto num literal PowerShell entre aspas simples.</summary>
    public static string LiteralPs(string valor) => "'" + valor.Replace("'", "''") + "'";
}

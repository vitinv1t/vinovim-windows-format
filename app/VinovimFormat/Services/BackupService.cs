using static VinovimFormat.Services.ProcessRunner;

namespace VinovimFormat.Services;

/// <summary>Salva o que é necessário para o PC funcionar bem depois de formatar.</summary>
public static class BackupService
{
    /// <summary>Retorna true se a pasta está no disco do Windows (que será apagado).</summary>
    public static bool EstaNoDiscoDoSistema(string pasta)
    {
        var raizSistema = Path.GetPathRoot(Environment.SystemDirectory);
        var raizPasta = Path.GetPathRoot(Path.GetFullPath(pasta));
        return string.Equals(raizSistema, raizPasta, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Exporta todos os drivers instalados (Wi-Fi, vídeo, áudio...) com o DISM.</summary>
    public static Task<ResultadoProcesso> ExportarDriversAsync(string pastaBackup, Action<string> linha)
    {
        var destino = Path.Combine(pastaBackup, "Drivers");
        Directory.CreateDirectory(destino);
        return ExecutarAsync(
            Path.Combine(Environment.SystemDirectory, "dism.exe"),
            $"/online /export-driver /destination:\"{destino}\"",
            linha);
    }

    /// <summary>Exporta as redes Wi-Fi salvas (com as senhas em texto, para reconectar depois).</summary>
    public static Task<ResultadoProcesso> ExportarWifiAsync(string pastaBackup, Action<string> linha)
    {
        var destino = Path.Combine(pastaBackup, "WiFi");
        Directory.CreateDirectory(destino);
        return ExecutarAsync(
            Path.Combine(Environment.SystemDirectory, "netsh.exe"),
            $"wlan export profile key=clear folder=\"{destino}\"",
            linha);
    }

    /// <summary>Salva a chave de recuperação do BitLocker num arquivo .txt.</summary>
    public static async Task<string> SalvarChaveBitLockerAsync(string pastaBackup, string unidade)
    {
        var r = await ExecutarAsync(
            Path.Combine(Environment.SystemDirectory, "manage-bde.exe"),
            $"-protectors -get {unidade}").ConfigureAwait(false);
        if (!r.Sucesso)
            throw new InvalidOperationException("Não foi possível ler a chave do BitLocker: " + (r.Erro + r.Saida).Trim());

        var arquivo = Path.Combine(pastaBackup, "ChaveRecuperacao-BitLocker.txt");
        await File.WriteAllTextAsync(arquivo, r.Saida).ConfigureAwait(false);
        return arquivo;
    }

    /// <summary>Suspende o BitLocker durante as próximas reinicializações da instalação.</summary>
    public static Task<ResultadoProcesso> SuspenderBitLockerAsync(string unidade) =>
        ExecutarAsync(
            Path.Combine(Environment.SystemDirectory, "manage-bde.exe"),
            $"-protectors -disable {unidade} -RebootCount 3");
}

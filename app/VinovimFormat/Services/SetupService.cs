using System.Diagnostics;
using VinovimFormat.Models;

namespace VinovimFormat.Services;

/// <summary>
/// Modo 2: inicia o instalador oficial (setup.exe) da ISO montada, a partir do próprio Windows.
/// Parâmetros documentados em "Windows Setup Command-Line Options" (Microsoft Learn).
/// </summary>
public static class SetupService
{
    public static string MontarArgumentos(TipoReinstalacao tipo) =>
        (tipo == TipoReinstalacao.Limpa ? "/auto clean " : "/auto dataonly ") +
        "/eula accept /showoobe full /compat ignorewarning /telemetry disable /dynamicupdate enable";

    public static string CaminhoSetup(string raizIso) => Path.Combine(raizIso, "setup.exe");

    /// <summary>Não espera terminar: o setup copia-se para o disco e reinicia o PC sozinho.</summary>
    public static void Iniciar(string raizIso, TipoReinstalacao tipo)
    {
        Process.Start(new ProcessStartInfo(CaminhoSetup(raizIso), MontarArgumentos(tipo))
        {
            UseShellExecute = true,
            WorkingDirectory = raizIso,
        });
    }
}

namespace VinovimFormat.Services;

/// <summary>Registro simples em arquivo + evento para mostrar na tela.</summary>
public sealed class Logger
{
    private readonly object _trava = new();

    public string Arquivo { get; }

    public event Action<string>? NovaLinha;

    public Logger()
    {
        string pasta;
        try
        {
            pasta = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(pasta);
        }
        catch
        {
            pasta = Path.Combine(Path.GetTempPath(), "VinovimFormat");
            Directory.CreateDirectory(pasta);
        }
        Arquivo = Path.Combine(pasta, $"vinovim-format-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public void Info(string mensagem)
    {
        var linha = $"[{DateTime.Now:HH:mm:ss}] {mensagem}";
        lock (_trava)
        {
            try { File.AppendAllText(Arquivo, linha + Environment.NewLine); }
            catch { /* log nunca deve derrubar a aplicação */ }
        }
        NovaLinha?.Invoke(linha);
    }
}

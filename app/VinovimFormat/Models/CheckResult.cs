namespace VinovimFormat.Models;

public enum CheckStatus { Ok, Aviso, Erro, Info }

/// <summary>Uma linha da lista de verificações exibida ao usuário.</summary>
public sealed record CheckResult(string Titulo, CheckStatus Status, string Detalhe)
{
    public string Icone => Status switch
    {
        CheckStatus.Ok => "✔",
        CheckStatus.Aviso => "⚠",
        CheckStatus.Erro => "✖",
        _ => "ℹ",
    };

    public string Cor => Status switch
    {
        CheckStatus.Ok => "#1E8E3E",
        CheckStatus.Aviso => "#B06000",
        CheckStatus.Erro => "#C5221F",
        _ => "#1A73E8",
    };
}

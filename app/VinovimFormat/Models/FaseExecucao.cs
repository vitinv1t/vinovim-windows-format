using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VinovimFormat.Models;

public enum EstadoFase { Pendente, EmCurso, Feito, Erro, Saltada }

/// <summary>Uma linha da lista de fases mostrada durante a execução (atualiza-se ao vivo).</summary>
public sealed class FaseExecucao : INotifyPropertyChanged
{
    private EstadoFase _estado;
    private string _detalhe = "";

    public FaseExecucao(string nome) => Nome = nome;

    public string Nome { get; }

    public EstadoFase Estado
    {
        get => _estado;
        set { if (_estado != value) { _estado = value; Avisar(); } }
    }

    public string Detalhe
    {
        get => _detalhe;
        set { if (_detalhe != value) { _detalhe = value; Avisar(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Avisar([CallerMemberName] string? nome = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}

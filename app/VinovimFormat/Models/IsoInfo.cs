namespace VinovimFormat.Models;

public sealed class EdicaoIso
{
    public int Index { get; set; }
    public string Nome { get; set; } = "";
}

/// <summary>Informações lidas do install.wim/esd da ISO.</summary>
public sealed class IsoInfo
{
    /// <summary>Código DISM: 0 = x86, 5 = arm, 9 = x64, 12 = arm64.</summary>
    public int Arquitetura { get; set; }
    public List<string> Idiomas { get; set; } = new();
    public string Versao { get; set; } = "";
    public List<EdicaoIso> Edicoes { get; set; } = new();

    public string ArquivoImagem { get; set; } = "";

    public string ArquiteturaNome => Arquitetura switch
    {
        0 => "x86",
        5 => "arm",
        9 => "x64",
        12 => "arm64",
        _ => "desconhecida",
    };

    public int Build
    {
        get
        {
            var partes = Versao.Split('.');
            return partes.Length >= 3 && int.TryParse(partes[2], out var b) ? b : 0;
        }
    }

    public bool IsWindows11 => Build >= 22000;

    public string NomeWindows => IsWindows11 ? "Windows 11" : "Windows 10";
}

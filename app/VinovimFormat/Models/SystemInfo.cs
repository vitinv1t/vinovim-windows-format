namespace VinovimFormat.Models;

public enum BitLockerEstado { Desligado, Ligado, Desconhecido }

/// <summary>Informações coletadas do PC no diagnóstico.</summary>
public sealed class SystemInfo
{
    public string NomeProduto { get; set; } = "Windows";
    public string Edicao { get; set; } = "";          // EditionID: Core, Professional, ...
    public string VersaoExibicao { get; set; } = "";  // 23H2, 24H2, 25H2 ...
    public int Build { get; set; }
    public string Arquitetura { get; set; } = "";     // x64, x86, arm64
    public string Idioma { get; set; } = "";          // pt-BR, pt-PT, ...
    public bool Admin { get; set; }
    public bool Uefi { get; set; }
    public bool? SecureBoot { get; set; }
    public string? Tpm { get; set; }                  // "2.0", "1.2", "nenhum" ou null
    public string UnidadeSistema { get; set; } = "C:";
    public long EspacoLivre { get; set; }
    public bool TemBateria { get; set; }
    public bool NaTomada { get; set; }
    public int? BateriaPercent { get; set; }
    public BitLockerEstado BitLocker { get; set; } = BitLockerEstado.Desconhecido;
    public bool? Ativado { get; set; }
    public string? ChaveParcial { get; set; }         // últimos 5 caracteres da chave instalada
    public string? ChaveFirmware { get; set; }        // chave OEM gravada na BIOS/UEFI (OA3)
    public string? SaudeDisco { get; set; }           // Saudável / Aviso / Com falhas
    public string? ModeloDisco { get; set; }
    public bool ParticaoTemporariaExiste { get; set; }

    public bool IsWindows11 => Build >= 22000;

    public string EdicaoAmigavel => Edicao switch
    {
        "Core" => "Home",
        "CoreSingleLanguage" => "Home Single Language",
        "CoreN" => "Home N",
        "Professional" => "Pro",
        "ProfessionalN" => "Pro N",
        "ProfessionalWorkstation" => "Pro for Workstations",
        "ProfessionalEducation" => "Pro Education",
        "Education" => "Education",
        "Enterprise" => "Enterprise",
        "EnterpriseS" => "Enterprise LTSC",
        _ => Edicao,
    };

    public double EspacoLivreGb => EspacoLivre / 1024d / 1024 / 1024;

    public string Resumo =>
        $"{NomeProduto} {EdicaoAmigavel} {VersaoExibicao}  ·  build {Build}  ·  {Arquitetura}  ·  {Idioma}  ·  " +
        $"{(Uefi ? "UEFI" : "BIOS Legacy")}  ·  {EspacoLivreGb:0.0} GB livres em {UnidadeSistema}";
}

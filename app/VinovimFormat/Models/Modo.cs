namespace VinovimFormat.Models;

public enum ModoFormatacao
{
    /// <summary>Partição temporária VINOVIM_BOOT + arranque direto no instalador oficial.</summary>
    ParticaoTemporaria,

    /// <summary>setup.exe da ISO montada, a partir do próprio Windows.</summary>
    ReinstalacaoDireta,

    /// <summary>DISM + SFC + reagentc, sem apagar nada.</summary>
    Reparar,
}

public enum TipoReinstalacao
{
    /// <summary>/auto clean: apaga tudo.</summary>
    Limpa,

    /// <summary>/auto dataonly: mantém só os ficheiros pessoais.</summary>
    ManterFicheiros,
}

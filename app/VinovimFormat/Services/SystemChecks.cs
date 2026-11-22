using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using VinovimFormat.Models;
using Microsoft.Win32;

namespace VinovimFormat.Services;

/// <summary>Coleta informações do PC e decide se é seguro reinstalar.</summary>
public static class SystemChecks
{
    public const long EspacoMinimoBytes = 15L * 1024 * 1024 * 1024; // 15 GB

    private const string IdWindowsLicenca = "55c92734-d682-4d71-983e-d6ec3f16059f";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out uint tipo); // 1 = BIOS, 2 = UEFI

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public static Task<(SystemInfo Info, List<CheckResult> Verificacoes)> ExecutarAsync() =>
        Task.Run(() =>
        {
            var info = Coletar();
            return (info, Avaliar(info));
        });

    public static SystemInfo Coletar()
    {
        var info = new SystemInfo();

        using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var cv = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            info.NomeProduto = cv?.GetValue("ProductName") as string ?? "Windows";
            info.Edicao = cv?.GetValue("EditionID") as string ?? "";
            info.VersaoExibicao = cv?.GetValue("DisplayVersion") as string
                                  ?? cv?.GetValue("ReleaseId") as string ?? "";
            int.TryParse(cv?.GetValue("CurrentBuild") as string, out var build);
            info.Build = build;
        }
        // No Windows 11 o registro ainda diz "Windows 10" em ProductName.
        if (info.IsWindows11)
            info.NomeProduto = info.NomeProduto.Replace("Windows 10", "Windows 11");

        info.Arquitetura = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            var a => a.ToString().ToLowerInvariant(),
        };
        info.Idioma = CultureInfo.InstalledUICulture.Name;
        info.Admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        info.Uefi = GetFirmwareType(out var firmware) && firmware == 2;

        using (var sb = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
        {
            var v = sb?.GetValue("UEFISecureBootEnabled");
            info.SecureBoot = v is int i ? i == 1 : null;
        }

        info.Tpm = LerTpm();

        var raiz = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        info.UnidadeSistema = raiz.TrimEnd('\\');
        info.EspacoLivre = new DriveInfo(raiz).AvailableFreeSpace;

        if (GetSystemPowerStatus(out var energia))
        {
            info.TemBateria = energia.BatteryFlag != 255 && (energia.BatteryFlag & 128) == 0;
            info.NaTomada = energia.ACLineStatus == 1;
            info.BateriaPercent = energia.BatteryLifePercent == 255 ? null : energia.BatteryLifePercent;
        }

        info.BitLocker = LerBitLocker(info.UnidadeSistema);
        LerLicenca(info);
        LerSaudeDisco(info);
        info.ParticaoTemporariaExiste = DriveInfo.GetDrives().Any(d =>
        {
            try { return d.IsReady && d.VolumeLabel == PartitionBootService.Rotulo; } catch { return false; }
        }) || PartitionBootService.ExisteParticaoSemLetra();
        return info;
    }

    public static List<CheckResult> Avaliar(SystemInfo s)
    {
        var r = new List<CheckResult>();

        r.Add(s.Admin
            ? new("Permissão de administrador", CheckStatus.Ok, "A aplicação está rodando como administrador.")
            : new("Permissão de administrador", CheckStatus.Erro, "Feche e abra a aplicação com \"Executar como administrador\"."));

        if (s.Build < 10240)
            r.Add(new("Versão do Windows", CheckStatus.Erro, $"{s.NomeProduto} não é suportado. A aplicação funciona com Windows 10 e 11."));
        else
            r.Add(new("Versão do Windows", CheckStatus.Ok, $"{s.NomeProduto} ({s.EdicaoAmigavel}) {s.VersaoExibicao}, build {s.Build}, {s.Arquitetura}, idioma {s.Idioma}."));

        var gb = s.EspacoLivre / 1024d / 1024 / 1024;
        r.Add(s.EspacoLivre >= EspacoMinimoBytes
            ? new("Espaço em disco", CheckStatus.Ok, $"{gb:0.0} GB livres em {s.UnidadeSistema}.")
            : new("Espaço em disco", CheckStatus.Erro, $"Só {gb:0.0} GB livres em {s.UnidadeSistema}. São necessários pelo menos 15 GB (partição temporária + instalação). Apague ficheiros grandes ou esvazie a Reciclagem e clique em \"Repetir diagnóstico\"."));

        if (!s.TemBateria)
            r.Add(new("Energia", CheckStatus.Ok, "Computador de secretária. Se houver falhas de luz frequentes, use uma UPS."));
        else if (s.NaTomada)
            r.Add(new("Energia", CheckStatus.Ok, $"Portátil ligado ao carregador{(s.BateriaPercent is int p ? $" (bateria {p}%)" : "")}."));
        else
            r.Add(new("Energia", CheckStatus.Erro, "Ligue o carregador. Se a bateria acabar no meio da instalação, o Windows pode ficar inutilizável."));

        var chave = s.ChaveFirmware is not null
            ? $" Chave OEM gravada na placa-mãe: {s.ChaveFirmware}."
            : s.ChaveParcial is not null ? $" Chave instalada termina em {s.ChaveParcial}." : "";
        r.Add(s.Ativado switch
        {
            true => new("Ativação do Windows", CheckStatus.Ok, "Windows ativado. A licença digital fica associada a este PC e volta sozinha depois de formatar (basta ligar à internet)." + chave),
            false => new("Ativação do Windows", CheckStatus.Aviso, "O Windows não está ativado. Depois de formatar pode ser preciso introduzir uma chave de produto." + chave),
            null => new("Ativação do Windows", CheckStatus.Aviso, "Não foi possível verificar a ativação." + chave),
        });

        r.Add(s.SaudeDisco switch
        {
            "Saudável" => new("Saúde do disco", CheckStatus.Ok, $"{s.ModeloDisco ?? "Disco do sistema"}: sem falhas reportadas."),
            "Aviso" => new("Saúde do disco", CheckStatus.Aviso, $"{s.ModeloDisco ?? "Disco do sistema"} reporta avisos. Faça cópia de segurança antes de continuar; o disco pode estar a falhar."),
            "Com falhas" => new("Saúde do disco", CheckStatus.Erro, $"{s.ModeloDisco ?? "Disco do sistema"} reporta falhas. Formatar não resolve um disco avariado: substitua-o primeiro."),
            _ => new("Saúde do disco", CheckStatus.Info, "Não foi possível ler o estado do disco."),
        });

        if (s.ParticaoTemporariaExiste)
            r.Add(new("Partição temporária", CheckStatus.Aviso, $"Já existe uma partição {PartitionBootService.Rotulo} de uma utilização anterior. Pode removê-la no passo 2."));

        r.Add(s.BitLocker switch
        {
            BitLockerEstado.Ligado => new("BitLocker / criptografia", CheckStatus.Aviso, $"O disco {s.UnidadeSistema} está criptografado. No passo 4, guarde a chave de recuperação FORA deste PC antes de continuar."),
            BitLockerEstado.Desligado => new("BitLocker / criptografia", CheckStatus.Ok, "Disco sem criptografia ativa."),
            _ => new("BitLocker / criptografia", CheckStatus.Info, "Não foi possível verificar o BitLocker. Se usa criptografia, guarde a chave de recuperação antes de continuar."),
        });

        if (s.IsWindows11 && s.Tpm is not null && !s.Tpm.StartsWith("2"))
            r.Add(new("Requisitos do Windows 11", CheckStatus.Aviso, $"TPM encontrado: {s.Tpm}. O Windows 11 exige TPM 2.0; o instalador pode bloquear a reinstalação neste PC."));
        else if (s.IsWindows11)
            r.Add(new("Requisitos do Windows 11", CheckStatus.Ok, $"TPM {s.Tpm ?? "?"}, Secure Boot {(s.SecureBoot == true ? "ativado" : s.SecureBoot == false ? "desativado" : "desconhecido")}."));

        r.Add(new("Firmware", CheckStatus.Info, s.Uefi ? "Modo UEFI." : "Modo BIOS Legacy."));

        if (s.Arquitetura == "arm64")
            r.Add(new("Processador ARM", CheckStatus.Info, "Este PC é ARM64: use uma ISO do Windows para ARM64."));

        return r;
    }

    private static ManagementObject? PrimeiroWmi(string escopo, string consulta)
    {
        using var buscador = new ManagementObjectSearcher(escopo, consulta);
        return buscador.Get().Cast<ManagementObject>().FirstOrDefault();
    }

    private static string? LerTpm()
    {
        try
        {
            var o = PrimeiroWmi(@"root\CIMV2\Security\MicrosoftTpm", "SELECT SpecVersion FROM Win32_Tpm");
            if (o is null) return "nenhum";
            return (o["SpecVersion"] as string)?.Split(',')[0].Trim();
        }
        catch { return null; }
    }

    private static BitLockerEstado LerBitLocker(string unidade)
    {
        try
        {
            var o = PrimeiroWmi(@"root\CIMV2\Security\MicrosoftVolumeEncryption",
                $"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='{unidade}'");
            if (o is null) return BitLockerEstado.Desligado;
            return Convert.ToUInt32(o["ProtectionStatus"]) switch
            {
                0 => BitLockerEstado.Desligado,
                1 => BitLockerEstado.Ligado,
                _ => BitLockerEstado.Desconhecido,
            };
        }
        catch { return BitLockerEstado.Desconhecido; }
    }

    private static void LerLicenca(SystemInfo info)
    {
        try
        {
            using var buscador = new ManagementObjectSearcher(
                $"SELECT LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE ApplicationID='{IdWindowsLicenca}' AND PartialProductKey IS NOT NULL");
            var produtos = buscador.Get().Cast<ManagementObject>().ToList();
            info.Ativado = produtos.Count > 0 && produtos.Any(p => Convert.ToUInt32(p["LicenseStatus"]) == 1);
            info.ChaveParcial = produtos.Select(p => p["PartialProductKey"] as string).FirstOrDefault(k => !string.IsNullOrEmpty(k));
        }
        catch { info.Ativado = null; }

        try
        {
            var servico = PrimeiroWmi(@"root\cimv2", "SELECT OA3xOriginalProductKey FROM SoftwareLicensingService");
            var chave = servico?["OA3xOriginalProductKey"] as string;
            info.ChaveFirmware = string.IsNullOrWhiteSpace(chave) ? null : chave.Trim();
        }
        catch { /* sem chave OEM */ }
    }

    private static void LerSaudeDisco(SystemInfo info)
    {
        try
        {
            var letra = info.UnidadeSistema.TrimEnd(':');
            // Partição do sistema -> disco físico
            ManagementObject? particao;
            using (var buscador = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
            {
                particao = buscador.Get().Cast<ManagementObject>().FirstOrDefault(p =>
                    p["DriveLetter"] is char c ? char.ToUpperInvariant(c).ToString() == letra.ToUpperInvariant()
                    : p["DriveLetter"] is ushort u && ((char)u).ToString().ToUpperInvariant() == letra.ToUpperInvariant());
            }
            if (particao is null) return;
            var numero = Convert.ToUInt32(particao["DiskNumber"]);
            var disco = PrimeiroWmi(@"root\Microsoft\Windows\Storage",
                $"SELECT FriendlyName, HealthStatus FROM MSFT_PhysicalDisk WHERE DeviceId='{numero}'");
            if (disco is null) return;
            info.ModeloDisco = disco["FriendlyName"] as string;
            info.SaudeDisco = Convert.ToUInt16(disco["HealthStatus"]) switch
            {
                0 => "Saudável",
                1 => "Aviso",
                2 => "Com falhas",
                _ => null,
            };
        }
        catch { info.SaudeDisco = null; }
    }
}

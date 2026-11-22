using System.Text.Json;
using VinovimFormat.Models;
using static VinovimFormat.Services.ProcessRunner;

namespace VinovimFormat.Services;

/// <summary>Monta, valida e lê a ISO do Windows usando os comandos nativos do sistema.</summary>
public static class IsoService
{
    /// <summary>Monta a ISO (ou reaproveita se já estiver montada) e devolve a raiz, ex.: "E:\".</summary>
    public static async Task<string> MontarAsync(string caminhoIso)
    {
        var script = $@"$ErrorActionPreference='Stop'
$p = {LiteralPs(caminhoIso)}
$img = Get-DiskImage -ImagePath $p
if (-not $img.Attached) {{ $img = Mount-DiskImage -ImagePath $p -PassThru }}
for ($i = 0; $i -lt 20; $i++) {{
  $l = ($img | Get-Volume -ErrorAction SilentlyContinue).DriveLetter
  if ($l) {{ Write-Output $l; exit 0 }}
  Start-Sleep -Milliseconds 500
  $img = Get-DiskImage -ImagePath $p
}}
throw 'A ISO foi montada, mas o Windows não atribuiu uma letra de unidade.'";

        var r = await PowerShellAsync(script).ConfigureAwait(false);
        var letra = r.Saida.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        if (!r.Sucesso || string.IsNullOrEmpty(letra))
            throw new InvalidOperationException("Não foi possível montar a ISO. " + LimparErro(r.Erro));

        return $"{letra[0]}:\\";
    }

    public static async Task DesmontarAsync(string caminhoIso)
    {
        await PowerShellAsync($"Dismount-DiskImage -ImagePath {LiteralPs(caminhoIso)} | Out-Null").ConfigureAwait(false);
    }

    /// <summary>Confere se a ISO é de instalação do Windows e devolve o caminho do install.wim/esd/swm.</summary>
    public static string ValidarArquivos(string raiz)
    {
        if (!File.Exists(Path.Combine(raiz, "setup.exe")))
            throw new InvalidOperationException("Essa ISO não tem setup.exe. Use uma ISO oficial de instalação do Windows 10 ou 11.");

        foreach (var nome in new[] { "install.wim", "install.esd", "install.swm" })
        {
            var caminho = Path.Combine(raiz, "sources", nome);
            if (File.Exists(caminho)) return caminho;
        }
        throw new InvalidOperationException("Essa ISO não tem sources\\install.wim nem install.esd. Use uma ISO oficial de instalação do Windows.");
    }

    public static async Task<IsoInfo> LerInfoAsync(string arquivoImagem)
    {
        var script = $@"$ErrorActionPreference='Stop'
$f = {LiteralPs(arquivoImagem)}
$imgs = @(Get-WindowsImage -ImagePath $f)
$d = Get-WindowsImage -ImagePath $f -Index $imgs[0].ImageIndex
$r = [pscustomobject]@{{
  Arquitetura = [int]$d.Architecture
  Idiomas     = @($d.Languages)
  Versao      = ""$($d.Version)""
  Edicoes     = @($imgs | ForEach-Object {{ [pscustomobject]@{{ Index = [int]$_.ImageIndex; Nome = ""$($_.ImageName)"" }} }})
}}
ConvertTo-Json -InputObject $r -Depth 4 -Compress";

        var r = await PowerShellAsync(script).ConfigureAwait(false);
        if (!r.Sucesso)
            throw new InvalidOperationException("Não foi possível ler as edições da ISO. " + LimparErro(r.Erro));

        var json = r.Saida.Trim();
        var info = JsonSerializer.Deserialize<IsoInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Resposta inesperada ao ler a ISO.");
        info.ArquivoImagem = arquivoImagem;
        return info;
    }

    /// <summary>Compara a ISO com o sistema atual.</summary>
    public static List<CheckResult> Compatibilidade(SystemInfo s, IsoInfo i)
    {
        var r = new List<CheckResult>();

        r.Add(i.ArquiteturaNome == s.Arquitetura
            ? new("Arquitetura", CheckStatus.Ok, $"ISO {i.ArquiteturaNome}, igual ao PC.")
            : new("Arquitetura", CheckStatus.Erro, $"A ISO é {i.ArquiteturaNome}, mas o PC é {s.Arquitetura}. Descarregue a ISO {s.Arquitetura}."));

        r.Add(new("Versão da ISO", CheckStatus.Ok, $"{i.NomeWindows}, versão {i.Versao}."));

        if (s.IsWindows11 && !i.IsWindows11)
            r.Add(new("Voltar para o Windows 10", CheckStatus.Aviso, "Está no Windows 11 e escolheu uma ISO do Windows 10, que não recebe mais atualizações de segurança gratuitas desde outubro de 2025."));
        else if (!s.IsWindows11 && i.IsWindows11)
            r.Add(new("Mudar para o Windows 11", CheckStatus.Info, "Vai instalar o Windows 11. O instalador confere se o PC atende aos requisitos (TPM 2.0, processador compatível)."));
        else if (i.Build > 0 && i.Build < s.Build)
            r.Add(new("ISO mais antiga", CheckStatus.Aviso, $"A ISO (build {i.Build}) é mais antiga que o sistema atual (build {s.Build}). O instalador pode não permitir. Prefira descarregar uma ISO recente."));

        if (i.Idiomas.Count > 0 && !i.Idiomas.Any(l => string.Equals(l, s.Idioma, StringComparison.OrdinalIgnoreCase)))
            r.Add(new("Idioma", CheckStatus.Aviso, $"A ISO está em {string.Join(", ", i.Idiomas)} e o Windows atual em {s.Idioma}. Funciona, mas o Windows novo ficará no idioma da ISO."));
        else
            r.Add(new("Idioma", CheckStatus.Ok, $"Idioma {string.Join(", ", i.Idiomas)}."));

        var edicoes = string.Join(", ", i.Edicoes.Select(e => e.Nome.Replace("Windows 10 ", "").Replace("Windows 11 ", "")));
        var procura = s.EdicaoAmigavel;
        var temEdicao = i.Edicoes.Any(e => e.Nome.EndsWith(" " + procura, StringComparison.OrdinalIgnoreCase));
        r.Add(temEdicao
            ? new("Edição", CheckStatus.Ok, $"A ISO tem a sua edição ({procura}). Edições na ISO: {edicoes}.")
            : new("Edição", CheckStatus.Aviso, $"Não encontrei a edição {procura} na ISO (tem: {edicoes}). Se o Windows instalado for de outra edição, a licença pode não ativar."));

        return r;
    }

    private static string LimparErro(string erro)
    {
        var linhas = erro.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(l => !l.StartsWith("#< CLIXML") && !l.StartsWith("<Objs"));
        return string.Join(" ", linhas).Trim();
    }
}

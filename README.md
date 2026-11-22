<p align="center">
  <img src="web/public/favicon.svg" width="72" height="72" alt="">
</p>

<h1 align="center">Vinovim Windows Format</h1>

<p align="center">Formata e reinstala o Windows 10 e 11 <b>sem pen-drive</b>, ou repara o "Repor este PC" avariado.</p>

<p align="center">
  <a href="https://vinovim-windows-format.vercel.app">Site e download</a> ·
  <a href="../../releases/latest">Última versão</a> ·
  <a href="#como-funciona">Como funciona</a>
</p>

---

## O que faz

Um assistente em 4 passos, com privilégios de administrador:

| Passo | O que acontece |
| --- | --- |
| 1. Diagnóstico | Espaço livre (mín. 15 GB), energia, arquitetura x64/ARM64, chave e ativação do Windows, saúde do disco, BitLocker |
| 2. Modo | **Modo 1** partição temporária e arranque no instalador · **Modo 2** setup.exe (limpa ou manter ficheiros) · **Modo 3** reparar (DISM, SFC, reagentc) |
| 3. ISO | ISO oficial da Microsoft, verificação SHA-256, compatibilidade de arquitetura, idioma e edição |
| 4. Confirmação | Escrever FORMATAR, ponto de restauro, cópia de drivers, Wi-Fi e chave BitLocker, registo em tempo real, modo de teste |

## Como funciona

O Modo 1 substitui o pen-drive por uma partição no próprio disco:

1. Reduz o `C:` em cerca de 8 GB e cria a partição `VINOVIM_BOOT` (NTFS, sem letra).
2. Copia o conteúdo da ISO para lá.
3. Cria uma entrada no BCD que arranca o `boot.wim` em ramdisk, só no próximo reinício (`bcdedit /bootsequence`).
4. No Windows novo, `SetupComplete.cmd` apaga a partição, devolve o espaço ao `C:` e remove a entrada de arranque.

Se algo falhar antes de reiniciar, a app desfaz a partição sozinha.

## Estrutura

```
vinovim-windows-format/
├── app/                      Aplicação Windows (C# .NET 10, WPF)
│   └── VinovimFormat/
│       ├── Models/           Dados do diagnóstico, ISO e modos
│       ├── Services/         Diagnóstico, partição + BCD, setup, reparação, ISO, backup
│       ├── MainWindow.xaml   Assistente em 4 passos
│       └── App.xaml          Tema (cores, botões, ícones Lucide)
├── web/                      Site oficial (Next.js, na Vercel)
│   ├── app/                  Páginas e rotas /api
│   ├── components/           Hero, pré-visualização da app, como funciona, barra de admin
│   ├── lib/                  GitHub Releases, Supabase (servidor), configuração
│   └── middleware.ts         Manutenção fail-closed
├── supabase/migrations/      Schema "winformat" (manutenção e contador de downloads)
└── .github/workflows/
    ├── ci.yml                Compila a app e o site em cada push
    └── release.yml           Gera os .exe e o SHA256SUMS.txt em cada tag vX.Y.Z
```

## Publicar uma versão

```bash
git tag v1.0.0
git push origin v1.0.0
```

O GitHub Actions compila `VinovimWindowsFormat-1.0.0-x64.exe` e `-arm64.exe` (um ficheiro, sem dependências), gera o `SHA256SUMS.txt` e cria a release. O site mostra a versão nova sozinho (consulta a API do GitHub Releases a cada 5 minutos).

**Assinatura digital (opcional):** adicione os secrets `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_SIGNING_ENDPOINT`, `AZURE_SIGNING_ACCOUNT` e `AZURE_SIGNING_PROFILE` (Azure Trusted Signing). Sem eles, os .exe saem sem assinatura e o Windows mostra o aviso do SmartScreen.

## Desenvolvimento

**App** (Windows, .NET 10 SDK):

```powershell
dotnet run --project app/VinovimFormat
```

Teste sempre primeiro numa máquina virtual (Hyper-V ou VirtualBox) e com o "Modo de teste" ligado no passo 4.

**Site** (Node 20+):

```bash
cd web
npm install
npm run dev
```

Variáveis na Vercel: `ADMIN_PATH` (endereço secreto do login de admin, obrigatório para entrar). As restantes têm valores por omissão (ver `web/.env.example`).

## Dados (Supabase)

O site usa um schema próprio, `winformat`, separado de tudo o resto:

| Objeto | Para quê |
| --- | --- |
| `winformat.definicoes` | Interruptor de manutenção deste site |
| `winformat.downloads` | Contador de downloads por arquitetura |
| `winformat_manutencao_ativa()` | Pública. Sem resposta = manutenção (fail-closed) |
| `winformat_contar(arquitetura)` | Pública. Soma um download |
| `winformat_definir_manutencao(ativa)` | Só o dono |
| `winformat_downloads()` | Só o dono |

## Licença

MIT. Usa ícones [Lucide](https://lucide.dev) (ISC).

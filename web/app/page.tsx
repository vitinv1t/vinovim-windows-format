import { ComoFunciona } from "@/components/ComoFunciona";
import { DownloadButton } from "@/components/DownloadButton";
import { Footer } from "@/components/Footer";
import { Hero } from "@/components/Hero";
import { Icon, type NomeIcone } from "@/components/Icon";
import { Nav } from "@/components/Nav";
import { Reveal } from "@/components/Reveal";
import { GITHUB_URL } from "@/lib/config";
import { URL_RELEASES, formatarData, formatarTamanho, obterRelease } from "@/lib/release";

export const revalidate = 300;

const MODOS = [
  {
    nome: "Formatação limpa",
    tag: "Modo 1 · partição temporária",
    oQue: "Cria a partição VINOVIM_BOOT, arranca no instalador oficial e instala do zero. O equivalente a formatar com pen-drive.",
    apaga: "Tudo",
    reinicia: "Sim, pelo instalador",
    iso: "Sim",
  },
  {
    nome: "Reinstalação direta",
    tag: "Modo 2 · setup.exe",
    oQue: "Corre o instalador oficial a partir do próprio Windows. Escolhe entre limpa ou manter os ficheiros pessoais.",
    apaga: "Tudo, ou só programas",
    reinicia: "Sim, sozinho",
    iso: "Sim",
  },
  {
    nome: "Reparar o sistema",
    tag: "Modo 3 · DISM · SFC · WinRE",
    oQue: "Corrige a imagem do Windows e reativa a recuperação, para o \"Repor este PC\" voltar a funcionar.",
    apaga: "Nada",
    reinicia: "No fim",
    iso: "Opcional",
  },
];

const VERIFICA: { icone: NomeIcone; titulo: string; texto: string }[] = [
  { icone: "hardDrive", titulo: "Espaço em disco", texto: "Pelo menos 15 GB livres para a partição temporária e para a instalação." },
  { icone: "battery", titulo: "Energia", texto: "Num portátil, só avança ligado ao carregador. Uma bateria vazia a meio deixaria o PC sem sistema." },
  { icone: "cpu", titulo: "Arquitetura", texto: "Distingue x64 de ARM64 e recusa uma ISO que não corresponda ao processador." },
  { icone: "key", titulo: "Chave de ativação", texto: "Mostra a chave gravada na placa-mãe e o estado da licença digital, para copiar ou guardar antes de formatar." },
  { icone: "activity", titulo: "Saúde do disco", texto: "Lê o estado do disco físico. Se estiver a falhar, avisa que formatar não o resolve." },
  { icone: "lock", titulo: "BitLocker", texto: "Deteta a criptografia, guarda a chave de recuperação e suspende-a durante os reinícios." },
];

const PERGUNTAS = [
  [
    "Preciso mesmo de não ter pen-drive?",
    "Não. A app serve para quem não tem pen-drive, mas também é útil para quem tem e não quer mexer na BIOS ou na ordem de arranque.",
  ],
  [
    "De onde vem o Windows instalado?",
    "Da ISO oficial que descarrega nos servidores da Microsoft. A app não modifica a imagem do Windows: só a copia para o disco e abre o instalador oficial.",
  ],
  [
    "O Windows fica ativado depois de formatar?",
    "Se o Windows estava ativado com licença digital ou chave gravada na placa-mãe, volta a ativar sozinho ao ligar à internet. A app mostra a chave antes de começar, para a guardar.",
  ],
  [
    "E se algo correr mal a meio?",
    "Antes de reiniciar, qualquer erro desfaz a partição temporária. Se o instalador for cancelado, abra a app outra vez: ela deteta a partição VINOVIM_BOOT e deixa reiniciar no instalador ou removê-la.",
  ],
  [
    "O Windows mostra um aviso do SmartScreen. É normal?",
    "Sim, enquanto o executável não tiver assinatura digital. Pode confirmar que o ficheiro é original comparando o hash com o SHA256SUMS.txt publicado em cada versão.",
  ],
  [
    "Funciona em PCs com BIOS antiga (Legacy)?",
    "Sim. A app deteta UEFI ou BIOS Legacy e configura o arranque de acordo. Em discos MBR com 4 partições primárias, o Modo 1 não consegue criar a partição: use o Modo 2.",
  ],
];

export default async function Inicio() {
  const release = await obterRelease();
  const sha = release?.sha256sums;

  return (
    <>
      <Nav />
      <main>
        <Hero release={release} />

        <section id="como-funciona">
          <div className="wrap">
            <Reveal className="cab">
              <div>
                <div className="rot">
                  <i /> Como funciona sem pen-drive
                </div>
                <h2>O pen-drive passa a ser uma partição.</h2>
              </div>
              <p>
                Em vez de copiar o instalador para um pen-drive, a app guarda-o numa pequena partição do próprio disco e
                diz ao Windows para arrancar por ela uma única vez.
              </p>
            </Reveal>
            <ComoFunciona />
          </div>
        </section>

        <section id="modos">
          <div className="wrap">
            <Reveal className="cab">
              <div>
                <div className="rot">
                  <i /> Três modos
                </div>
                <h2>Escolha o que o PC precisa.</h2>
              </div>
              <p>Formatar do zero, reinstalar sem sair do Windows, ou só reparar. A app recomenda e explica cada um.</p>
            </Reveal>
            <Reveal>
              <table className="tabela">
                <thead>
                  <tr>
                    <th>Modo</th>
                    <th>O que faz</th>
                    <th>Apaga</th>
                    <th>Reinicia</th>
                    <th>Precisa de ISO</th>
                  </tr>
                </thead>
                <tbody>
                  {MODOS.map((m) => (
                    <tr key={m.nome}>
                      <td>
                        <b>{m.nome}</b>
                        <span>{m.tag}</span>
                      </td>
                      <td>{m.oQue}</td>
                      <td data-r="Apaga" className={m.apaga === "Nada" ? "nao" : "sim"}>
                        {m.apaga}
                      </td>
                      <td data-r="Reinicia">{m.reinicia}</td>
                      <td data-r="ISO">{m.iso}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Reveal>
          </div>
        </section>

        <section id="seguranca">
          <div className="wrap">
            <Reveal className="cab">
              <div>
                <div className="rot">
                  <i /> Antes de mexer em nada
                </div>
                <h2>Diagnóstico primeiro, confirmação depois.</h2>
              </div>
              <p>
                Cria um ponto de restauro, mostra um registo em tempo real e só altera o disco depois de escrever FORMATAR.
                Há também um modo de teste que não altera nada.
              </p>
            </Reveal>
            <div className="diag">
              {VERIFICA.map((v, i) => (
                <Reveal key={v.titulo} atraso={(i % 3) * 0.06}>
                  <Icon nome={v.icone} tamanho={20} />
                  <h3>{v.titulo}</h3>
                  <p>{v.texto}</p>
                </Reveal>
              ))}
            </div>
          </div>
        </section>

        <section id="download">
          <div className="wrap">
            <Reveal className="dl">
              <div className="a">
                <div className="rot">
                  <i /> Download
                </div>
                <h2>{release ? `Versão ${release.versao.replace(/^v/, "")}` : "Primeira versão a caminho"}</h2>
                <p className="lead">
                  Um único ficheiro .exe, sem instalação. Abra, aceite o pedido de administrador e siga os 4 passos.
                </p>
                <div className="acoes">
                  <DownloadButton release={release} alternativa />
                </div>
                {sha && (
                  <p className="cmdl">
                    Verificar: <a href={sha.url}>SHA256SUMS.txt</a> · Get-FileHash .\{release?.x64?.nome} -Algorithm SHA256
                  </p>
                )}
              </div>
              <div className="b">
                <dl>
                  <dt>Versão</dt>
                  <dd>{release?.versao ?? "Em preparação"}</dd>
                  <dt>Publicada</dt>
                  <dd>{release ? formatarData(release.data) : "-"}</dd>
                  <dt>Tamanho</dt>
                  <dd>{release?.x64 ? formatarTamanho(release.x64.tamanho) : "-"}</dd>
                  <dt>Sistemas</dt>
                  <dd>Windows 10 e 11</dd>
                  <dt>Processador</dt>
                  <dd>x64 e ARM64</dd>
                  <dt>Todas as versões</dt>
                  <dd>
                    <a href={URL_RELEASES}>GitHub Releases</a>
                  </dd>
                </dl>
                <div className="req">
                  {["15 GB livres no disco do Windows", "ISO oficial do Windows 10 ou 11", "Conta de administrador", "Portátil ligado ao carregador"].map(
                    (r) => (
                      <div key={r}>
                        <Icon nome="check" tamanho={15} />
                        {r}
                      </div>
                    ),
                  )}
                </div>
              </div>
            </Reveal>
          </div>
        </section>

        <section id="perguntas">
          <div className="wrap">
            <Reveal className="cab">
              <div>
                <div className="rot">
                  <i /> Perguntas
                </div>
                <h2>Antes de formatar.</h2>
              </div>
              <p>
                Não encontrou a resposta? Abra uma questão no <a href={`${GITHUB_URL}/issues`}>GitHub</a>.
              </p>
            </Reveal>
            <div className="faq">
              {PERGUNTAS.map(([p, r]) => (
                <details key={p}>
                  <summary>
                    {p}
                    <Icon nome="plus" tamanho={18} />
                  </summary>
                  <p>{r}</p>
                </details>
              ))}
            </div>
          </div>
        </section>
      </main>
      <Footer />
    </>
  );
}

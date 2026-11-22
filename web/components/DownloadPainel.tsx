"use client";

import { DownloadButton } from "./DownloadButton";
import { Icon } from "./Icon";
import { useRelease } from "./useRelease";
import { URL_RELEASES, formatarData, formatarTamanho, type Release } from "@/lib/release";

const REQUISITOS = [
  "15 GB livres no disco do Windows",
  "ISO oficial do Windows 10 ou 11",
  "Conta de administrador",
  "Portátil ligado ao carregador",
];

export function DownloadPainel({ release: inicial }: { release: Release | null }) {
  const release = useRelease(inicial);
  const sha = release?.sha256sums;

  return (
    <>
      <div className="a">
        <div className="rot">
          <i /> Download
        </div>
        <h2>{release ? `Versão ${release.versao.replace(/^v/, "")}` : "Primeira versão a caminho"}</h2>
        <p className="lead">Um único ficheiro .exe, sem instalação. Abra, aceite o pedido de administrador e siga os 4 passos.</p>
        <div className="acoes">
          <DownloadButton release={release} alternativa />
        </div>
        {sha && release?.x64 && (
          <p className="cmdl">
            Verificar: <a href={sha.url}>SHA256SUMS.txt</a> · Get-FileHash .\{release.x64.nome} -Algorithm SHA256
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
          {REQUISITOS.map((r) => (
            <div key={r}>
              <Icon nome="check" tamanho={15} />
              {r}
            </div>
          ))}
        </div>
      </div>
    </>
  );
}

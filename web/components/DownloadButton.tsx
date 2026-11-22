"use client";

import { useEffect, useState } from "react";
import { Icon } from "./Icon";
import type { Release } from "@/lib/release";

type NavegadorComUA = Navigator & {
  userAgentData?: { getHighEntropyValues(dicas: string[]): Promise<{ architecture?: string; bitness?: string }> };
};

function tamanhoMb(bytes: number) {
  return `${(bytes / 1024 / 1024).toFixed(0)} MB`;
}

function contar(arquitetura: "x64" | "arm64") {
  try {
    const corpo = new Blob([JSON.stringify({ arquitetura })], { type: "application/json" });
    if (!navigator.sendBeacon?.("/api/contar", corpo))
      void fetch("/api/contar", { method: "POST", body: corpo, keepalive: true });
  } catch {
    /* nunca bloqueia */
  }
}

/** Botão principal: mostra a versão e o tamanho da release mais recente e escolhe x64 ou ARM64. */
export function DownloadButton({ release, alternativa = false }: { release: Release | null; alternativa?: boolean }) {
  const [arm, setArm] = useState(false);

  useEffect(() => {
    const nav = navigator as NavegadorComUA;
    nav.userAgentData
      ?.getHighEntropyValues(["architecture"])
      .then((v) => setArm(v.architecture === "arm"))
      .catch(() => {});
  }, []);

  if (!release?.x64) {
    return (
      <a className="btn p" aria-disabled="true">
        <Icon nome="download" />
        Primeira versão a sair
      </a>
    );
  }

  const principal = arm && release.arm64 ? release.arm64 : release.x64;
  const outra = principal === release.x64 ? release.arm64 : release.x64;
  const nomeArq = (a: typeof principal) => (a === release.arm64 ? "ARM64" : "x64");

  return (
    <>
      <a className="btn p" href={principal.url} onClick={() => contar(principal === release.arm64 ? "arm64" : "x64")}>
        <Icon nome="download" />
        Descarregar {release.versao}
        <small>
          {nomeArq(principal)} · {tamanhoMb(principal.tamanho)}
        </small>
      </a>
      {alternativa && outra && (
        <a className="btn" href={outra.url} onClick={() => contar(outra === release.arm64 ? "arm64" : "x64")}>
          Versão {nomeArq(outra)}
          <small>{tamanhoMb(outra.tamanho)}</small>
        </a>
      )}
    </>
  );
}

"use client";

import { motion } from "framer-motion";
import { AppPreview } from "./AppPreview";
import { DownloadButton } from "./DownloadButton";
import { Icon } from "./Icon";
import type { Release } from "@/lib/release";

const entrar = (atraso: number) => ({
  initial: { opacity: 0, y: 18 },
  animate: { opacity: 1, y: 0 },
  transition: { duration: 0.6, delay: atraso, ease: [0.22, 1, 0.36, 1] as const },
});

export function Hero({ release }: { release: Release | null }) {
  return (
    <div className="hero">
      <div className="grade" aria-hidden="true" />
      <div className="wrap">
        <div className="topo">
          <div>
            <motion.div className="rot" {...entrar(0)}>
              <i /> {release ? `${release.versao} · ` : ""}Windows 10 e Windows 11
            </motion.div>
            <motion.h1 {...entrar(0.05)}>Formate o Windows sem pen-drive.</motion.h1>
          </div>
          <motion.div className="lado" {...entrar(0.15)}>
            <p className="lead">
              Para quem não tem pen-drive ou tem o &quot;Repor este PC&quot; avariado. A app prepara o instalador oficial da
              Microsoft no próprio disco e arranca diretamente nele.
            </p>
            <div className="acoes">
              <DownloadButton release={release} />
              <a className="btn" href="#como-funciona">
                Como funciona
                <Icon nome="arrowRight" tamanho={16} />
              </a>
            </div>
            <div className="meta">
              <span>
                <b>Gratuito</b> e sem anúncios
              </span>
              <span>
                <b>Sem instalação</b>, um só ficheiro
              </span>
              <span>
                <b>Código aberto</b>
              </span>
            </div>
          </motion.div>
        </div>
        <motion.div
          initial={{ opacity: 0, y: 40 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.9, delay: 0.25, ease: [0.22, 1, 0.36, 1] }}
        >
          <AppPreview />
        </motion.div>
      </div>
    </div>
  );
}

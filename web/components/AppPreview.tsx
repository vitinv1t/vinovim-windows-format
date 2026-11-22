"use client";

import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { Icon, Logo, type NomeIcone } from "./Icon";

const PASSOS: { nome: string; icone: NomeIcone }[] = [
  { nome: "Diagnóstico", icone: "scan" },
  { nome: "Modo", icone: "layers" },
  { nome: "Imagem ISO", icone: "disc" },
  { nome: "Confirmação", icone: "shield" },
];

const DIAGNOSTICO = [
  ["Permissão de administrador", "Ativa"],
  ["Espaço em disco", "86,4 GB livres"],
  ["Energia", "Ligado à corrente"],
  ["Ativação do Windows", "Licença digital"],
  ["Saúde do disco", "Sem falhas"],
];

const LOG = [
  "Ponto de restauro criado.",
  "A reduzir C: em 8,0 GB e a criar a partição VINOVIM_BOOT...",
  "Partição VINOVIM_BOOT criada em V:.",
  "A copiar os ficheiros de instalação (6,4 GB)...",
  "Cópia concluída.",
  "Limpeza automática pós-instalação preparada.",
  "bcdedit /set {ramdiskoptions} ramdisksdipath \\boot\\boot.sdi",
  "Entrada criada: o próximo arranque abre o instalador do Windows.",
];

const DURACAO = 4200;

/** Réplica interativa da janela da app, com os 4 passos a correr sozinhos (clique para navegar). */
export function AppPreview() {
  const [passo, setPasso] = useState(0);
  const [pausado, setPausado] = useState(false);
  const [tick, setTick] = useState(0);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (pausado) return;
    const id = setInterval(() => setPasso((p) => (p + 1) % PASSOS.length), DURACAO);
    return () => clearInterval(id);
  }, [pausado]);

  useEffect(() => {
    setTick(0);
    const id = setInterval(() => setTick((t) => t + 1), 380);
    return () => clearInterval(id);
  }, [passo]);

  const escolher = (i: number) => {
    setPasso(i);
    setPausado(true);
  };

  return (
    <div className="janela" ref={ref} onMouseLeave={() => setPausado(false)}>
      <div className="barra">
        <Logo tamanho={16} />
        Vinovim Windows Format
        <div className="ctl" aria-hidden="true">
          <span>
            <svg width="10" height="10" viewBox="0 0 10 10"><path d="M0 5h10" stroke="currentColor" /></svg>
          </span>
          <span>
            <svg width="10" height="10" viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9" fill="none" stroke="currentColor" /></svg>
          </span>
          <span>
            <svg width="10" height="10" viewBox="0 0 10 10"><path d="M0 0l10 10M10 0 0 10" stroke="currentColor" /></svg>
          </span>
        </div>
      </div>

      <div className="corpo">
        <div className="lat" role="tablist" aria-label="Passos da aplicação">
          {PASSOS.map((p, i) => (
            <button
              key={p.nome}
              role="tab"
              aria-selected={i === passo}
              className={`passo ${i === passo ? "on" : ""} ${i < passo ? "feito" : ""}`}
              onClick={() => escolher(i)}
            >
              <Icon nome={i < passo ? "check" : p.icone} tamanho={16} />
              {p.nome}
            </button>
          ))}
        </div>

        <div className="cont">
          <AnimatePresence mode="wait">
            <motion.div
              key={passo}
              initial={{ opacity: 0, x: 10 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -10 }}
              transition={{ duration: 0.25, ease: "easeOut" }}
            >
              {passo === 0 && (
                <>
                  <div className="p">PASSO 1 DE 4</div>
                  <h4>Diagnóstico do sistema</h4>
                  <p className="d">Windows 11 Pro 24H2 · x64 · UEFI · pt-PT</p>
                  <div className="lista">
                    {DIAGNOSTICO.map(([t, v], i) => (
                      <motion.div
                        key={t}
                        className="item"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: tick > i ? 1 : 0.25 }}
                        transition={{ duration: 0.2 }}
                      >
                        <span className="ok">{tick > i ? <Icon nome="check" tamanho={15} /> : null}</span>
                        <span>{t}</span>
                        <span className="v">{v}</span>
                      </motion.div>
                    ))}
                  </div>
                </>
              )}

              {passo === 1 && (
                <>
                  <div className="p">PASSO 2 DE 4</div>
                  <h4>Modo de formatação</h4>
                  <p className="d">Nenhum dos modos precisa de pen-drive.</p>
                  {[
                    ["Modo 1 · Partição temporária", "Arranca diretamente no instalador oficial e instala do zero."],
                    ["Modo 2 · Reinstalação direta", "Corre o setup.exe a partir do próprio Windows."],
                    ["Modo 3 · Reparar o sistema", "DISM, SFC e reagentc. Não apaga nada."],
                  ].map(([t, d], i) => (
                    <div key={t} className={`opc ${i === 0 ? "on" : ""}`}>
                      <span className="anel" />
                      <div>
                        <b>{t}</b>
                        <span>{d}</span>
                      </div>
                    </div>
                  ))}
                </>
              )}

              {passo === 2 && (
                <>
                  <div className="p">PASSO 3 DE 4</div>
                  <h4>Imagem oficial do Windows</h4>
                  <p className="d">A ISO faz o papel do pen-drive.</p>
                  <div className="campo">
                    <span>Win11_24H2_Portuguese_x64.iso</span>
                    <span>6,4 GB</span>
                  </div>
                  <div className="campo mono">
                    <span>SHA-256</span>
                    <span>{tick > 6 ? "calculado" : "a calcular…"}</span>
                  </div>
                  <div className="trilho">
                    <motion.div animate={{ width: `${Math.min(100, tick * 15)}%` }} transition={{ duration: 0.35 }} />
                  </div>
                  <div className="item" style={{ padding: "4px 0", border: 0 }}>
                    <span className="ok">{tick > 6 ? <Icon nome="check" tamanho={15} /> : null}</span>
                    <span>{tick > 6 ? "Hash verificado · arquitetura e idioma compatíveis" : "A verificar a ISO…"}</span>
                    <span />
                  </div>
                </>
              )}

              {passo === 3 && (
                <>
                  <div className="p">EXECUÇÃO</div>
                  <h4>{tick > 9 ? "Pronto para reiniciar" : "A preparar o arranque…"}</h4>
                  <div className="trilho">
                    <motion.div animate={{ width: `${Math.min(100, tick * 11)}%` }} transition={{ duration: 0.35 }} />
                  </div>
                  <div className="log" aria-live="off">
                    {LOG.slice(0, Math.min(LOG.length, tick + 1)).map((l, i) => (
                      <div key={i}>
                        <span className="t">20:4{Math.min(9, i)}</span>
                        {l}
                      </div>
                    ))}
                  </div>
                </>
              )}
            </motion.div>
          </AnimatePresence>
        </div>
      </div>

      <div className="rod" aria-hidden="true">
        <span className="b">Voltar</span>
        <span className="b p">{passo === 3 ? "Reiniciar agora" : "Continuar"}</span>
      </div>
    </div>
  );
}

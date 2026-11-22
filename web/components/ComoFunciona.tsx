"use client";

import { useEffect, useState } from "react";
import { motion } from "framer-motion";
import { Icon } from "./Icon";

type Segmento = { id: string; classe: string; titulo: string; sub: string; largura: number };

const ETAPAS: { titulo: string; texto: string; cmd: string; disco: Segmento[] }[] = [
  {
    titulo: "O disco como está hoje",
    texto: "O Windows atual ocupa a partição C:. Não é preciso pen-drive, DVD nem outro computador.",
    cmd: "Diagnóstico: espaço, energia, ativação, BitLocker e saúde do disco",
    disco: [{ id: "c", classe: "win", titulo: "C:", sub: "Windows atual", largura: 100 }],
  },
  {
    titulo: "Uma partição temporária no próprio disco",
    texto: "A app reduz o C: em cerca de 8 GB, cria a partição oculta VINOVIM_BOOT e copia para lá o instalador oficial da ISO.",
    cmd: "Resize-Partition · New-Partition VINOVIM_BOOT · robocopy",
    disco: [
      { id: "c", classe: "win", titulo: "C:", sub: "Windows atual", largura: 90 },
      { id: "b", classe: "boot", titulo: "VINOVIM_BOOT", sub: "instalador", largura: 10 },
    ],
  },
  {
    titulo: "Arranque direto no instalador",
    texto: "O Boot Configuration Data passa a abrir o instalador uma única vez, carregado na memória. Sem entrar na BIOS.",
    cmd: "bcdedit ramdisk=[V:]\\sources\\boot.wim · /bootsequence",
    disco: [
      { id: "c", classe: "livre", titulo: "Espaço livre", sub: "C: apagado no instalador", largura: 90 },
      { id: "b", classe: "boot", titulo: "VINOVIM_BOOT", sub: "em uso", largura: 10 },
    ],
  },
  {
    titulo: "Windows novo, sem restos",
    texto: "No fim da instalação, um script remove a partição temporária, devolve o espaço ao C: e apaga a entrada de arranque.",
    cmd: "SetupComplete.cmd · Remove-Partition · Resize-Partition C:",
    disco: [{ id: "c", classe: "novo", titulo: "C:", sub: "Windows novo", largura: 100 }],
  },
];

export function ComoFunciona() {
  const [etapa, setEtapa] = useState(0);
  const [auto, setAuto] = useState(true);

  useEffect(() => {
    if (!auto) return;
    const id = setInterval(() => setEtapa((e) => (e + 1) % ETAPAS.length), 4600);
    return () => clearInterval(id);
  }, [auto]);

  const atual = ETAPAS[etapa];

  return (
    <div className="como">
      <div className="etapas">
        {ETAPAS.map((e, i) => (
          <button
            key={e.titulo}
            className={`etapa ${i === etapa ? "on" : ""}`}
            onClick={() => {
              setEtapa(i);
              setAuto(false);
            }}
            aria-pressed={i === etapa}
          >
            {i === etapa && <motion.span layoutId="marca-etapa" className="marca-on" />}
            <span className="n">0{i + 1}</span>
            <span className="t">{e.titulo}</span>
            <span className="x">{e.texto}</span>
          </button>
        ))}
      </div>

      <div className="disco" aria-live="polite">
        <div className="rot2">
          <span>DISCO 0 · SSD 512 GB</span>
          <span>
            {etapa + 1} / {ETAPAS.length}
          </span>
        </div>
        <div className="barra">
          {atual.disco.map((s) => (
            <motion.div
              key={s.id}
              layout
              className={`seg ${s.classe}`}
              initial={false}
              animate={{ flexBasis: `${s.largura}%` }}
              transition={{ duration: 0.6, ease: [0.22, 1, 0.36, 1] }}
            >
              <b>{s.titulo}</b>
              <small>{s.sub}</small>
            </motion.div>
          ))}
        </div>
        <motion.div key={etapa} className="cmd" initial={{ opacity: 0 }} animate={{ opacity: 1 }} transition={{ duration: 0.4 }}>
          <span>$</span> {atual.cmd}
        </motion.div>
        <div className="legenda">
          <div>
            <Icon nome="hardDrive" tamanho={16} />
            Tudo acontece no disco interno; nada é escrito noutros discos.
          </div>
          <div>
            <Icon nome="disc" tamanho={16} />
            Usa a ISO oficial da Microsoft, verificada por SHA-256.
          </div>
          <div>
            <Icon nome="rotate" tamanho={16} />
            Se algo falhar antes de reiniciar, a app desfaz a partição sozinha.
          </div>
          <div>
            <Icon nome="shield" tamanho={16} />
            Nada é alterado sem escrever FORMATAR para confirmar.
          </div>
        </div>
      </div>
    </div>
  );
}

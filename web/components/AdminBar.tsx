"use client";

import { useCallback, useEffect, useState } from "react";
import { Icon } from "./Icon";

type Estado = { manutencao: boolean; admin: boolean; downloads?: Record<string, number> };

/** Barra flutuante: só aparece com sessão de administrador. Interruptor de manutenção em 1 clique. */
export function AdminBar() {
  const [estado, setEstado] = useState<Estado | null>(null);
  const [aGuardar, setAGuardar] = useState(false);

  const carregar = useCallback(async () => {
    try {
      const r = await fetch("/api/estado", { cache: "no-store" });
      if (r.ok) setEstado((await r.json()) as Estado);
    } catch {
      /* sem ligação */
    }
  }, []);

  useEffect(() => {
    void carregar();
    const id = setInterval(carregar, 20000);
    return () => clearInterval(id);
  }, [carregar]);

  if (!estado?.admin) return null;

  const alternar = async () => {
    setAGuardar(true);
    try {
      const r = await fetch("/api/manutencao", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ativa: !estado.manutencao }),
      });
      if (r.ok) setEstado({ ...estado, manutencao: !estado.manutencao });
    } finally {
      setAGuardar(false);
    }
  };

  const sair = async () => {
    await fetch("/api/sair", { method: "POST" });
    window.location.href = "/";
  };

  const total = (estado.downloads?.x64 ?? 0) + (estado.downloads?.arm64 ?? 0);

  return (
    <div className="barra-admin" role="region" aria-label="Administração">
      <Icon nome="lock" tamanho={15} />
      <span className="email">Admin</span>
      <span className="sep" />
      <span>
        <b>{total}</b> downloads
      </span>
      <span className="sep" />
      <button
        className="interruptor"
        role="switch"
        aria-checked={estado.manutencao}
        onClick={alternar}
        disabled={aGuardar}
        title="Ligar ou desligar a manutenção deste site"
      >
        <i />
        {estado.manutencao ? "Em manutenção" : "Site aberto"}
      </button>
      <button className="sair" onClick={sair} title="Terminar sessão" aria-label="Terminar sessão">
        <Icon nome="logOut" tamanho={15} />
      </button>
    </div>
  );
}

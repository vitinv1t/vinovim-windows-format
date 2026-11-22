"use client";

import { useEffect } from "react";

/** Volta ao início assim que a manutenção terminar. */
export function EstadoManutencao() {
  useEffect(() => {
    const verificar = async () => {
      try {
        const r = await fetch("/api/estado", { cache: "no-store" });
        const e = (await r.json()) as { manutencao: boolean; admin: boolean };
        if (!e.manutencao || e.admin) window.location.replace("/");
      } catch {
        /* continua à espera */
      }
    };
    void verificar();
    const id = setInterval(verificar, 15000);
    return () => clearInterval(id);
  }, []);
  return null;
}

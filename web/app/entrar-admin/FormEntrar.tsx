"use client";

import { useState } from "react";
import { ADMIN_EMAIL } from "@/lib/config";

export function FormEntrar() {
  const [palavraPasse, setPalavraPasse] = useState("");
  const [erro, setErro] = useState("");
  const [aEntrar, setAEntrar] = useState(false);

  const submeter = async (e: React.FormEvent) => {
    e.preventDefault();
    setErro("");
    setAEntrar(true);
    try {
      const r = await fetch("/api/entrar", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ palavraPasse }),
      });
      if (r.ok) {
        window.location.replace("/");
        return;
      }
      const d = (await r.json().catch(() => ({}))) as { erro?: string };
      setErro(d.erro ?? "Não foi possível entrar.");
    } catch {
      setErro("Sem ligação. Tente outra vez.");
    } finally {
      setAEntrar(false);
    }
  };

  return (
    <form className="form" onSubmit={submeter}>
      <label>
        Email
        <input value={ADMIN_EMAIL} readOnly autoComplete="username" />
      </label>
      <label>
        Palavra-passe
        <input
          type="password"
          value={palavraPasse}
          onChange={(e) => setPalavraPasse(e.target.value)}
          autoComplete="current-password"
          autoFocus
          required
        />
      </label>
      {erro && <p className="erro">{erro}</p>}
      <button className="btn p" type="submit" disabled={aEntrar}>
        {aEntrar ? "A entrar…" : "Entrar"}
      </button>
    </form>
  );
}

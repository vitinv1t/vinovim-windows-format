"use client";

import { useEffect, useState } from "react";
import { URL_API_RELEASE, converterRelease, type Release, type ReleaseGitHub } from "@/lib/release";

/**
 * Usa a release vinda do servidor; se o servidor não a conseguiu ler
 * (ex.: limite da API do GitHub nos IPs da Vercel), pede-a a partir do browser.
 */
export function useRelease(inicial: Release | null): Release | null {
  const [release, setRelease] = useState(inicial);

  useEffect(() => {
    if (inicial?.x64) return;
    let ativo = true;
    fetch(URL_API_RELEASE, { headers: { Accept: "application/vnd.github+json" } })
      .then((r) => (r.ok ? (r.json() as Promise<ReleaseGitHub>) : null))
      .then((d) => {
        if (ativo && d) setRelease(converterRelease(d));
      })
      .catch(() => {});
    return () => {
      ativo = false;
    };
  }, [inicial]);

  return release;
}

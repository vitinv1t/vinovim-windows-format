"use client";

import { useEffect, useState } from "react";
import { URL_API_RELEASE, converterRelease, type Release, type ReleaseGitHub } from "@/lib/release";

/**
 * Mostra logo a release vinda do servidor e confirma sempre no GitHub, a partir do browser,
 * se há uma mais recente (o servidor pode ter cache ou estar limitado pela API do GitHub).
 */
export function useRelease(inicial: Release | null): Release | null {
  const [release, setRelease] = useState(inicial);

  useEffect(() => {
    let ativo = true;
    fetch(URL_API_RELEASE, { headers: { Accept: "application/vnd.github+json" } })
      .then((r) => (r.ok ? (r.json() as Promise<ReleaseGitHub>) : null))
      .then((d) => {
        if (!ativo || !d) return;
        const nova = converterRelease(d);
        if (nova.x64 && nova.versao !== inicial?.versao) setRelease(nova);
      })
      .catch(() => {});
    return () => {
      ativo = false;
    };
  }, [inicial]);

  return release;
}

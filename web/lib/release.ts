import { GITHUB_REPO, GITHUB_URL } from "./config";

export type Artefacto = { nome: string; url: string; tamanho: number };

export type Release = {
  versao: string;
  data: string;
  url: string;
  x64?: Artefacto;
  arm64?: Artefacto;
  sha256sums?: Artefacto;
};

type AssetGitHub = { name: string; browser_download_url: string; size: number };
export type ReleaseGitHub = { tag_name: string; published_at: string; html_url: string; assets: AssetGitHub[] };

export const URL_API_RELEASE = `https://api.github.com/repos/${GITHUB_REPO}/releases/latest`;

/** Converte a resposta da API do GitHub (usado no servidor e no browser). */
export function converterRelease(dados: ReleaseGitHub): Release {
  const achar = (teste: (n: string) => boolean): Artefacto | undefined => {
    const a = dados.assets.find((x) => teste(x.name.toLowerCase()));
    return a ? { nome: a.name, url: a.browser_download_url, tamanho: a.size } : undefined;
  };
  return {
    versao: dados.tag_name,
    data: dados.published_at,
    url: dados.html_url,
    x64: achar((n) => n.endsWith(".exe") && n.includes("x64")),
    arm64: achar((n) => n.endsWith(".exe") && n.includes("arm64")),
    sha256sums: achar((n) => n === "sha256sums.txt"),
  };
}

/** Lê a release mais recente na API do GitHub (cache de 5 minutos). */
export async function obterRelease(): Promise<Release | null> {
  try {
    const headers: Record<string, string> = {
      Accept: "application/vnd.github+json",
      "X-GitHub-Api-Version": "2022-11-28",
      "User-Agent": "vinovim-windows-format-web",
    };
    if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

    const r = await fetch(URL_API_RELEASE, {
      headers,
      next: { revalidate: 60 },
    });
    if (!r.ok) return null;
    return converterRelease((await r.json()) as ReleaseGitHub);
  } catch {
    return null;
  }
}

export const URL_RELEASES = `${GITHUB_URL}/releases`;

export function formatarTamanho(bytes: number) {
  return `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
}

export function formatarData(iso: string) {
  return new Intl.DateTimeFormat("pt-PT", { day: "numeric", month: "long", year: "numeric" }).format(new Date(iso));
}

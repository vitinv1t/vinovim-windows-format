// Acesso ao Supabase do lado do servidor (middleware e rotas /api).
// Tudo do Windows Format vive no schema "winformat", exposto só pelas funções public.winformat_*.
// Usa só fetch, por isso funciona no Edge Runtime e no Node.
import type { NextResponse } from "next/server";
import { ADMIN_EMAIL, SUPABASE_ANON_KEY, SUPABASE_URL } from "./config";

export const COOKIE_AT = "vv_at";
export const COOKIE_RT = "vv_rt";
const TEMPO_LIMITE = 3500;

export type Tokens = { access_token: string; refresh_token: string; expires_in: number };

type LeitorCookies = { get(nome: string): { value: string } | undefined };
type EscritorCookies = NextResponse["cookies"];

function cabecalhos(token?: string): Record<string, string> {
  const h: Record<string, string> = { apikey: SUPABASE_ANON_KEY, "Content-Type": "application/json" };
  if (token) h.Authorization = `Bearer ${token}`;
  return h;
}

/** Fail-closed: qualquer erro ao ler o estado conta como "em manutenção". */
export async function manutencaoAtiva(): Promise<boolean> {
  try {
    const r = await fetch(`${SUPABASE_URL}/rest/v1/rpc/winformat_manutencao_ativa`, {
      method: "POST",
      headers: cabecalhos(),
      body: "{}",
      cache: "no-store",
      signal: AbortSignal.timeout(TEMPO_LIMITE),
    });
    if (!r.ok) return true;
    return (await r.json()) !== false;
  } catch {
    return true;
  }
}

async function emailDoToken(at: string): Promise<string | null> {
  try {
    const r = await fetch(`${SUPABASE_URL}/auth/v1/user`, {
      headers: cabecalhos(at),
      cache: "no-store",
      signal: AbortSignal.timeout(TEMPO_LIMITE),
    });
    if (!r.ok) return null;
    const u = (await r.json()) as { email?: string };
    return u.email?.toLowerCase() ?? null;
  } catch {
    return null;
  }
}

async function pedirToken(tipo: "password" | "refresh_token", corpo: Record<string, string>): Promise<Tokens | null> {
  try {
    const r = await fetch(`${SUPABASE_URL}/auth/v1/token?grant_type=${tipo}`, {
      method: "POST",
      headers: cabecalhos(),
      body: JSON.stringify(corpo),
      cache: "no-store",
      signal: AbortSignal.timeout(TEMPO_LIMITE),
    });
    if (!r.ok) return null;
    const t = (await r.json()) as Partial<Tokens>;
    return t.access_token && t.refresh_token
      ? { access_token: t.access_token, refresh_token: t.refresh_token, expires_in: t.expires_in ?? 3600 }
      : null;
  } catch {
    return null;
  }
}

/** Só aceita o email do dono. */
export function entrar(palavraPasse: string) {
  return pedirToken("password", { email: ADMIN_EMAIL, password: palavraPasse });
}

export type Validacao = { admin: boolean; at?: string; novos?: Tokens };

/** Confirma a sessão de admin a partir dos cookies; renova o token se tiver expirado. */
export async function validarAdmin(cookies: LeitorCookies): Promise<Validacao> {
  const at = cookies.get(COOKIE_AT)?.value;
  if (at && (await emailDoToken(at)) === ADMIN_EMAIL) return { admin: true, at };

  const rt = cookies.get(COOKIE_RT)?.value;
  if (rt) {
    const novos = await pedirToken("refresh_token", { refresh_token: rt });
    if (novos && (await emailDoToken(novos.access_token)) === ADMIN_EMAIL)
      return { admin: true, at: novos.access_token, novos };
  }
  return { admin: false };
}

const BASE_COOKIE = { httpOnly: true, secure: true, sameSite: "lax", path: "/" } as const;

export function gravarSessao(cookies: EscritorCookies, t: Tokens) {
  cookies.set(COOKIE_AT, t.access_token, { ...BASE_COOKIE, maxAge: t.expires_in });
  cookies.set(COOKIE_RT, t.refresh_token, { ...BASE_COOKIE, maxAge: 60 * 60 * 24 * 30 });
}

export function apagarSessao(cookies: EscritorCookies) {
  cookies.delete(COOKIE_AT);
  cookies.delete(COOKIE_RT);
}

/** Interruptor de manutenção deste site (só o email do dono, verificado na base de dados). */
export async function definirManutencao(at: string, ativa: boolean): Promise<boolean> {
  try {
    const r = await fetch(`${SUPABASE_URL}/rest/v1/rpc/winformat_definir_manutencao`, {
      method: "POST",
      headers: cabecalhos(at),
      body: JSON.stringify({ p_ativa: ativa }),
      cache: "no-store",
      signal: AbortSignal.timeout(TEMPO_LIMITE),
    });
    return r.ok;
  } catch {
    return false;
  }
}

export async function lerDownloads(at: string): Promise<Record<string, number>> {
  try {
    const r = await fetch(`${SUPABASE_URL}/rest/v1/rpc/winformat_downloads`, {
      method: "POST",
      headers: cabecalhos(at),
      body: "{}",
      cache: "no-store",
      signal: AbortSignal.timeout(TEMPO_LIMITE),
    });
    if (!r.ok) return {};
    const linhas = (await r.json()) as { arquitetura: string; total: number }[];
    return Object.fromEntries(linhas.map((l) => [l.arquitetura, Number(l.total)]));
  } catch {
    return {};
  }
}

export async function contarDownload(arquitetura: "x64" | "arm64") {
  try {
    await fetch(`${SUPABASE_URL}/rest/v1/rpc/winformat_contar`, {
      method: "POST",
      headers: cabecalhos(),
      body: JSON.stringify({ p_arquitetura: arquitetura }),
      cache: "no-store",
      signal: AbortSignal.timeout(TEMPO_LIMITE),
    });
  } catch {
    /* nunca bloqueia o download */
  }
}

import { NextResponse, type NextRequest } from "next/server";
import { gravarSessao, manutencaoAtiva, validarAdmin } from "./lib/servidor";

// Caminhos que continuam acessíveis durante a manutenção.
const LIVRES = [
  /^\/manutencao$/,
  /^\/api\/(estado|entrar|sair|manutencao|contar)$/,
  /^\/robots\.txt$/,
  /^\/favicon\.svg$/,
];

export async function middleware(req: NextRequest) {
  const { pathname } = req.nextUrl;
  const segredo = process.env.ADMIN_PATH;

  // O formulário de login só existe no endereço secreto (variável ADMIN_PATH na Vercel).
  if (pathname.startsWith("/entrar-admin")) return NextResponse.rewrite(new URL("/_nao-existe", req.url));
  if (segredo && pathname === `/${segredo}`) return NextResponse.rewrite(new URL("/entrar-admin", req.url));

  if (LIVRES.some((r) => r.test(pathname))) return NextResponse.next();

  // Fail-closed: se o Supabase não responder, o site fica em manutenção.
  if (!(await manutencaoAtiva())) return NextResponse.next();

  const v = await validarAdmin(req.cookies);
  if (v.admin) {
    const res = NextResponse.next();
    if (v.novos) gravarSessao(res.cookies, v.novos);
    return res;
  }

  const res = NextResponse.rewrite(new URL("/manutencao", req.url), { status: 503 });
  res.headers.set("Retry-After", "600");
  res.headers.set("Cache-Control", "no-store");
  return res;
}

export const config = {
  matcher: ["/((?!_next/static|_next/image|_vercel|favicon.svg).*)"],
};

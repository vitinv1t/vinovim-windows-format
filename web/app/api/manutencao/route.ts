import { cookies } from "next/headers";
import { json, mesmaOrigem } from "@/lib/pedido";
import { definirManutencao, gravarSessao, validarAdmin } from "@/lib/servidor";

export async function POST(req: Request) {
  if (!mesmaOrigem(req)) return json({ erro: "Pedido recusado." }, 403);
  const v = await validarAdmin(await cookies());
  if (!v.admin || !v.at) return json({ erro: "Sem sessão de administrador." }, 401);

  const corpo = (await req.json().catch(() => ({}))) as { ativa?: unknown };
  if (typeof corpo.ativa !== "boolean") return json({ erro: "Valor inválido." }, 400);

  const ok = await definirManutencao(v.at, corpo.ativa);
  const res = ok ? json({ ok: true, ativa: corpo.ativa }) : json({ erro: "Não foi possível guardar." }, 502);
  if (v.novos) gravarSessao(res.cookies, v.novos);
  return res;
}

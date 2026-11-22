import { json, mesmaOrigem } from "@/lib/pedido";
import { apagarSessao } from "@/lib/servidor";

export async function POST(req: Request) {
  if (!mesmaOrigem(req)) return json({ erro: "Pedido recusado." }, 403);
  const res = json({ ok: true });
  apagarSessao(res.cookies);
  return res;
}

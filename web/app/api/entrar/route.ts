import { json, mesmaOrigem } from "@/lib/pedido";
import { entrar, gravarSessao } from "@/lib/servidor";

// Limite simples: 6 tentativas por 10 minutos por IP (por instância).
const tentativas = new Map<string, { n: number; desde: number }>();
const JANELA = 10 * 60 * 1000;

export async function POST(req: Request) {
  if (!mesmaOrigem(req)) return json({ erro: "Pedido recusado." }, 403);

  const ip = req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() || "local";
  const agora = Date.now();
  const t = tentativas.get(ip);
  const atual = t && agora - t.desde < JANELA ? t : { n: 0, desde: agora };
  if (atual.n >= 6) return json({ erro: "Demasiadas tentativas. Tente daqui a 10 minutos." }, 429);

  const corpo = (await req.json().catch(() => ({}))) as { palavraPasse?: string };
  const palavra = typeof corpo.palavraPasse === "string" ? corpo.palavraPasse : "";
  const tokens = palavra ? await entrar(palavra) : null;

  if (!tokens) {
    tentativas.set(ip, { n: atual.n + 1, desde: atual.desde });
    return json({ erro: "Palavra-passe incorreta." }, 401);
  }

  tentativas.delete(ip);
  const res = json({ ok: true });
  gravarSessao(res.cookies, tokens);
  return res;
}

import { cookies } from "next/headers";
import { json } from "@/lib/pedido";
import { gravarSessao, lerDownloads, manutencaoAtiva, validarAdmin } from "@/lib/servidor";

export const dynamic = "force-dynamic";

export async function GET() {
  const jar = await cookies();
  const [manutencao, v] = await Promise.all([manutencaoAtiva(), validarAdmin(jar)]);
  const res = json({
    manutencao,
    admin: v.admin,
    downloads: v.admin && v.at ? await lerDownloads(v.at) : undefined,
  });
  if (v.novos) gravarSessao(res.cookies, v.novos);
  return res;
}

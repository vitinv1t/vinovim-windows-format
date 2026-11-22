import { json } from "@/lib/pedido";
import { contarDownload } from "@/lib/servidor";

// Chamado com navigator.sendBeacon ao clicar em descarregar; nunca bloqueia o download.
export async function POST(req: Request) {
  const texto = await req.text().catch(() => "");
  let arquitetura: "x64" | "arm64" = "x64";
  try {
    if ((JSON.parse(texto) as { arquitetura?: string }).arquitetura === "arm64") arquitetura = "arm64";
  } catch {
    /* corpo vazio */
  }
  await contarDownload(arquitetura);
  return json({ ok: true });
}

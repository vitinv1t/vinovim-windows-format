import { NextResponse } from "next/server";

/** Bloqueia pedidos POST vindos de outros sites (proteção CSRF simples). */
export function mesmaOrigem(req: Request) {
  const origem = req.headers.get("origin");
  return !origem || origem === new URL(req.url).origin;
}

export function json(dados: unknown, status = 200) {
  return NextResponse.json(dados, { status, headers: { "Cache-Control": "no-store" } });
}

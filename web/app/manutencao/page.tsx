import type { Metadata } from "next";
import { Logo } from "@/components/Icon";
import { EstadoManutencao } from "./EstadoManutencao";

export const metadata: Metadata = { title: "Em manutenção · Vinovim Windows Format", robots: { index: false } };
export const dynamic = "force-dynamic";

export default function Manutencao() {
  return (
    <main className="ecra">
      <div className="cx">
        <Logo tamanho={44} />
        <span className="pulso" aria-hidden="true" />
        <h1>Voltamos já.</h1>
        <p>O Vinovim Windows Format está em manutenção por uns minutos. A página abre sozinha quando estiver tudo pronto.</p>
        <EstadoManutencao />
      </div>
    </main>
  );
}

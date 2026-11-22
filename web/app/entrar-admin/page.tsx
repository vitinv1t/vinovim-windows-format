import type { Metadata } from "next";
import { Logo } from "@/components/Icon";
import { FormEntrar } from "./FormEntrar";

export const metadata: Metadata = { title: "Vinovim", robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

export default function EntrarAdmin() {
  return (
    <main className="ecra">
      <div className="cx">
        <Logo tamanho={40} />
        <h1>Administração</h1>
        <FormEntrar />
      </div>
    </main>
  );
}

import { Logo } from "@/components/Icon";

export default function NaoEncontrado() {
  return (
    <main className="ecra">
      <div className="cx">
        <Logo tamanho={40} />
        <h1>Página não encontrada</h1>
        <p>O endereço não existe ou mudou de sítio.</p>
        <a className="btn" href="/">
          Voltar ao início
        </a>
      </div>
    </main>
  );
}

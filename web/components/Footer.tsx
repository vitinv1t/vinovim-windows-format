import { ECOSSISTEMA, GITHUB_URL } from "@/lib/config";
import { Logo } from "./Icon";

export function Footer() {
  return (
    <footer>
      <div className="wrap">
        <a className="marca" href="/">
          <Logo tamanho={22} />
          Vinovim
        </a>
        <nav aria-label="Projetos Vinovim">
          {ECOSSISTEMA.map((p) => (
            <a key={p.nome} href={p.url} className={p.url === "/" ? "atual" : undefined}>
              {p.nome}
            </a>
          ))}
        </nav>
        <span className="e" />
        <a href={GITHUB_URL}>Código no GitHub</a>
        <span>© 2026 Vinovim</span>
      </div>
    </footer>
  );
}

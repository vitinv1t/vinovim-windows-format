import { Logo } from "./Icon";

export function Nav() {
  return (
    <header className="nav">
      <div className="wrap">
        <a className="marca" href="/" aria-label="Vinovim Windows Format, início">
          <Logo />
          Vinovim <span>Windows Format</span>
        </a>
        <nav className="links" aria-label="Secções">
          <a href="#como-funciona">Como funciona</a>
          <a href="#modos">Modos</a>
          <a href="#seguranca">Segurança</a>
          <a href="#perguntas">Perguntas</a>
        </nav>
        <a className="btn s p" href="#download">
          Descarregar
        </a>
      </div>
    </header>
  );
}

export const GITHUB_REPO = process.env.NEXT_PUBLIC_GITHUB_REPO || "vitinv1t/vinovim-windows-format";
export const GITHUB_URL = `https://github.com/${GITHUB_REPO}`;

// Chave pública (publishable): pode estar no browser; o acesso é protegido por RLS no Supabase.
export const SUPABASE_URL = process.env.NEXT_PUBLIC_SUPABASE_URL || "https://aiwgmwkikkfikdgoermm.supabase.co";
export const SUPABASE_ANON_KEY =
  process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY || "sb_publishable_78s0N1WpKGAc1XEy8v0rNg_Z9-2zTBd";

export const ADMIN_EMAIL = "vitin7xisde@gmail.com";

// Links para os outros projetos Vinovim (só links: cada projeto tem o seu código e os seus dados).
export const ECOSSISTEMA = [
  { nome: "Streaming", url: "https://vinovim.vercel.app" },
  { nome: "Mapa GTA VI", url: "https://vinovimgtavi.vercel.app" },
  { nome: "Bot Discord", url: "https://vinovimdiscord.vercel.app" },
  { nome: "Windows Format", url: "/" },
] as const;

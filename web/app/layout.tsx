import type { Metadata, Viewport } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import { Analytics } from "@vercel/analytics/next";
import { SmoothScroll } from "@/components/SmoothScroll";
import { AdminBar } from "@/components/AdminBar";
import "./globals.css";

const geist = Geist({ subsets: ["latin"], variable: "--font-geist-sans", display: "swap" });
const geistMono = Geist_Mono({ subsets: ["latin"], variable: "--font-geist-mono", display: "swap" });

export const metadata: Metadata = {
  metadataBase: new URL("https://vinovim-windows-format.vercel.app"),
  title: "Vinovim Windows Format · Formatar o Windows sem pen-drive",
  description:
    "Formate e reinstale o Windows 10 e 11 sem pen-drive, ou repare o \"Repor este PC\" avariado. Gratuito, com o instalador oficial da Microsoft.",
  icons: { icon: "/favicon.svg" },
  openGraph: {
    title: "Vinovim Windows Format",
    description: "Formate o Windows 10 e 11 sem pen-drive.",
    type: "website",
    locale: "pt_PT",
  },
};

export const viewport: Viewport = { themeColor: "#09090B", colorScheme: "dark" };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pt" className={`${geist.variable} ${geistMono.variable}`}>
      <body>
        <SmoothScroll />
        {children}
        <AdminBar />
        <Analytics />
      </body>
    </html>
  );
}

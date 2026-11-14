// pequenos ajudantes de formatação e entrada (pt-BR)

// "2026-02-15" -> "15/02/2026"
export function dataBr(iso: string): string {
  const [a, m, d] = iso.slice(0, 10).split("-");
  return `${d}/${m}/${a}`;
}

// "2026-02-15" -> "15/02"
export const diaMes = (iso: string) => dataBr(iso).slice(0, 5);

// hoje no fuso do aparelho, "aaaa-mm-dd"
export function hojeIso(agora = new Date()): string {
  const p = (n: number) => String(n).padStart(2, "0");
  return `${agora.getFullYear()}-${p(agora.getMonth() + 1)}-${p(agora.getDate())}`;
}

// número digitado no celular: aceita "10,5" e "10.5". null = vazio ou inválido
export function lerNumero(texto: string): number | null {
  const t = texto.trim().replace(/\s/g, "");
  if (!t) return null;
  // "1.234,5" (milhar pt-BR) -> 1234.5; "10,5" -> 10.5; "10.5" fica
  const normal = t.includes(",") ? t.replace(/\./g, "").replace(",", ".") : t;
  if (!/^-?\d+(\.\d+)?$/.test(normal)) return null;
  const n = Number(normal);
  return Number.isFinite(n) ? n : null;
}

// 1234.5 -> "1.234,5"
export const ha = (n: number) => n.toLocaleString("pt-BR", { maximumFractionDigits: 2 });

export const pct = (n: number) => `${Math.round(n * 100)}%`;

export const COR_CULTURA: Record<string, string> = {
  milho: "var(--milho)",
  sorgo: "var(--sorgo)",
  "braquiária": "var(--braq)",
};

export const corCultura = (c: string) => COR_CULTURA[c] ?? "var(--suave)";

// posição do aparelho (para lat/lon da fazenda ou do talhão)
export function minhaLocalizacao(): Promise<{ lat: number; lon: number }> {
  return new Promise((ok, erro) => {
    if (!("geolocation" in navigator)) return erro(new Error("Este aparelho não informa a localização."));
    navigator.geolocation.getCurrentPosition(
      (p) => ok({ lat: Math.round(p.coords.latitude * 1e6) / 1e6, lon: Math.round(p.coords.longitude * 1e6) / 1e6 }),
      () => erro(new Error("Não foi possível pegar a localização. Libere o acesso ou digite.")),
      { enableHighAccuracy: true, timeout: 15000 },
    );
  });
}

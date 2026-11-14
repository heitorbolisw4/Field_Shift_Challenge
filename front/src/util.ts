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

// milhar pt-BR: 1 a 3 dígitos e depois grupos de exatamente 3 ("1.500", "12.345.678")
const MILHAR = /^\d{1,3}(\.\d{3})+$/;

// número digitado no celular (pt-BR). null = vazio ou inválido.
//  - com vírgula: vírgula é o decimal; pontos só valem como milhar certinho ("1.234,5"; "1.2,5" é recusado)
//  - sem vírgula: "1.500" e "1.000.000" são milhar; outro ponto é decimal ("10.5", "-19.034567" do GPS)
export function lerNumero(texto: string): number | null {
  const t = texto.replace(/\s/g, "");
  const neg = t.startsWith("-");
  const corpo = neg ? t.slice(1) : t;
  let normal: string;
  if (corpo.includes(",")) {
    const partes = corpo.split(",");
    if (partes.length !== 2) return null;
    const [inteiro, fracao] = partes;
    if (!/^\d+$/.test(fracao) || !(/^\d+$/.test(inteiro) || MILHAR.test(inteiro))) return null;
    normal = `${inteiro.replace(/\./g, "")}.${fracao}`;
  } else if (MILHAR.test(corpo)) {
    normal = corpo.replace(/\./g, "");
  } else if (/^\d+(\.\d+)?$/.test(corpo)) {
    normal = corpo;
  } else {
    return null;
  }
  const n = Number((neg ? "-" : "") + normal);
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

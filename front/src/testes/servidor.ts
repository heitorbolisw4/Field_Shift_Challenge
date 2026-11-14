import { vi } from "vitest";
import type { Tokens } from "../api";

// fetch falso: responde por "MÉTODO /rota" e guarda as chamadas feitas
type Resposta = { status?: number; corpo?: unknown };
type Manipulador = (req: { metodo: string; rota: string; corpo: unknown; auth: string | null }) => Resposta | Promise<Resposta>;

export function servidor(rotas: Record<string, Manipulador | Resposta>) {
  const chamadas: { metodo: string; rota: string; corpo: unknown; auth: string | null }[] = [];
  const fetchFalso = vi.fn(async (entrada: RequestInfo | URL, init?: RequestInit) => {
    const rota = String(entrada);
    const metodo = init?.method ?? "GET";
    const corpo = init?.body ? JSON.parse(String(init.body)) : undefined;
    const auth = (init?.headers as Record<string, string> | undefined)?.Authorization ?? null;
    const req = { metodo, rota, corpo, auth };
    chamadas.push(req);
    const chave = `${metodo} ${rota.split("?")[0]}`;
    const m = rotas[chave];
    if (!m) return new Response(JSON.stringify(`sem rota falsa: ${chave}`), { status: 599 });
    const r = typeof m === "function" ? await m(req) : m;
    const status = r.status ?? 200;
    return new Response(status === 204 ? null : JSON.stringify(r.corpo ?? null), { status, headers: { "Content-Type": "application/json" } });
  });
  return { fetch: fetchFalso as unknown as typeof fetch, chamadas };
}

let n = 0;
export function tokens(nome = "Ana"): Tokens {
  n++;
  return {
    accessToken: `at-${n}`, expiraEm: "2026-11-14T20:00:00Z",
    refreshToken: `rt-${n}`, refreshExpiraEm: "2026-12-14T20:00:00Z",
    usuario: { id: "u1", email: "ana@x.com", nome },
  };
}

export class Memoria implements Storage {
  private m = new Map<string, string>();
  get length() { return this.m.size; }
  clear() { this.m.clear(); }
  getItem(k: string) { return this.m.get(k) ?? null; }
  key(i: number) { return [...this.m.keys()][i] ?? null; }
  removeItem(k: string) { this.m.delete(k); }
  setItem(k: string, v: string) { this.m.set(k, v); }
}

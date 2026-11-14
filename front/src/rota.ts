import { useEffect, useState } from "react";

// rotas por hash (#/fazenda/3): funciona em qualquer hospedagem estática, sem configurar o servidor
export type Rota =
  | { tela: "fazendas" }
  | { tela: "fazenda"; id: number; aba: string }
  | { tela: "talhao"; id: number };

export function lerRota(hash: string): Rota {
  const partes = hash.replace(/^#\/?/, "").split("/").filter(Boolean);
  const id = Number(partes[1]);
  if (partes[0] === "fazenda" && Number.isInteger(id) && id > 0) return { tela: "fazenda", id, aba: partes[2] ?? "talhoes" };
  if (partes[0] === "talhao" && Number.isInteger(id) && id > 0) return { tela: "talhao", id };
  return { tela: "fazendas" };
}

export const link = {
  fazendas: () => "#/",
  fazenda: (id: number, aba?: string) => `#/fazenda/${id}${aba ? `/${aba}` : ""}`,
  talhao: (id: number) => `#/talhao/${id}`,
};

export function ir(href: string) {
  window.location.hash = href.slice(1);
}

export function useRota(): Rota {
  const [rota, setRota] = useState(() => lerRota(window.location.hash));
  useEffect(() => {
    const f = () => { setRota(lerRota(window.location.hash)); window.scrollTo(0, 0); };
    window.addEventListener("hashchange", f);
    return () => window.removeEventListener("hashchange", f);
  }, []);
  return rota;
}

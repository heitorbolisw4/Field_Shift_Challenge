import { createContext, useCallback, useContext, useEffect, useState } from "react";
import type { Api, Usuario } from "./api";
import { ApiErro } from "./api";

const ApiContexto = createContext<Api | null>(null);

export const ApiProvider = ApiContexto.Provider;

export function useApi(): Api {
  const api = useContext(ApiContexto);
  if (!api) throw new Error("ApiProvider faltando");
  return api;
}

export function useUsuario(): Usuario | null {
  const api = useApi();
  const [u, setU] = useState(api.usuario);
  useEffect(() => api.ouvir(setU), [api]);
  return u;
}

// carrega algo da API: { dado, erro, carregando, recarregar }
export function useCarregar<T>(buscar: () => Promise<T>, deps: unknown[]) {
  const [dado, setDado] = useState<T | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [versao, setVersao] = useState(0);

  useEffect(() => {
    let vivo = true;
    setCarregando(true);
    setErro(null);
    buscar()
      .then((d) => { if (vivo) setDado(d); })
      .catch((e) => { if (vivo) setErro(textoErro(e)); })
      .finally(() => { if (vivo) setCarregando(false); });
    return () => { vivo = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, versao]);

  const recarregar = useCallback(() => setVersao((v) => v + 1), []);
  return { dado, erro, carregando, recarregar, setDado };
}

export function textoErro(e: unknown): string {
  if (e instanceof ApiErro) return e.message;
  if (e instanceof TypeError) return "Sem conexão com o servidor.";
  if (e instanceof Error) return e.message;
  return "Algo deu errado.";
}

import { useEffect, useState } from "react";
import { useApi, useUsuario } from "./contexto";
import { useRota } from "./rota";
import { Entrar } from "./telas/Entrar";
import { Fazenda } from "./telas/Fazenda";
import { Fazendas } from "./telas/Fazendas";
import { Talhao } from "./telas/Talhao";
import { Carregando } from "./telas/comum";

export function App() {
  const api = useApi();
  const usuario = useUsuario();
  const rota = useRota();
  // ao abrir: se há login guardado no aparelho, volta a sessão antes de mostrar a tela de entrar
  const [restaurando, setRestaurando] = useState(() => api.temSessaoGuardada() && !api.usuario);

  useEffect(() => {
    if (restaurando) api.restaurar().finally(() => setRestaurando(false));
  }, [api, restaurando]);

  if (restaurando) return <Carregando texto="Abrindo…" />;
  if (!usuario) return <Entrar />;
  if (rota.tela === "fazenda") return <Fazenda id={rota.id} aba={rota.aba} />;
  if (rota.tela === "talhao") return <Talhao id={rota.id} />;
  return <Fazendas />;
}

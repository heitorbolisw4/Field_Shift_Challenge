import { useState, type ReactNode } from "react";
import { minhaLocalizacao } from "../util";

export function Topo({ titulo, voltar, direita }: { titulo: string; voltar?: string; direita?: ReactNode }) {
  return (
    <header className="topo">
      {voltar ? <a className="voltar" href={voltar} aria-label="Voltar">‹</a> : <span className="voltar-vazio" />}
      <h1>{titulo}</h1>
      <div className="topo-direita">{direita}</div>
    </header>
  );
}

export function Carregando({ texto = "Carregando…" }: { texto?: string }) {
  return <p className="carregando" role="status">{texto}</p>;
}

export function Erro({ texto, tentar }: { texto: string; tentar?: () => void }) {
  return (
    <div className="erro" role="alert">
      {texto} {tentar && <button className="link" onClick={tentar}>Tentar de novo</button>}
    </div>
  );
}

// botão "usar minha localização" que preenche lat/lon
export function BotaoLocal({ aoPegar }: { aoPegar: (lat: number, lon: number) => void }) {
  const [estado, setEstado] = useState<string | null>(null);
  return (
    <div className="local">
      <button type="button" className="secundario" onClick={async () => {
        setEstado("Pegando localização…");
        try {
          const p = await minhaLocalizacao();
          aoPegar(p.lat, p.lon);
          setEstado(null);
        } catch (e) {
          setEstado((e as Error).message);
        }
      }}>📍 Usar minha localização</button>
      {estado && <small className="dica">{estado}</small>}
    </div>
  );
}

export function Selo({ cultura }: { cultura: string }) {
  return <span className={`selo selo-${cultura.normalize("NFD").replace(/[̀-ͯ]/g, "")}`}>{cultura}</span>;
}

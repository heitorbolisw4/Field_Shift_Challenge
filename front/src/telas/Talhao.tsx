import { useState } from "react";
import type { Colheita } from "../api";
import { textoErro, useApi, useCarregar } from "../contexto";
import { ir, link } from "../rota";
import { dataBr, ha, hojeIso, pct } from "../util";
import { Carregando, Erro, Selo, Topo } from "./comum";

const PORQUE: Record<string, string> = {
  milho: "O milho cabe no risco aceito nessa data: é a safrinha de maior valor.",
  sorgo: "O milho já passa do risco nessa data, mas o sorgo ainda cabe.",
  "braquiária": "Nessa data, milho e sorgo passam do risco aceito. A braquiária cobre o solo e faz palha para a soja.",
};

export function Talhao({ id }: { id: number }) {
  const api = useApi();
  const talhao = useCarregar(() => api.talhao(id), [id]);
  const historico = useCarregar(() => api.colheitas(id), [id]);
  const [data, setData] = useState(hojeIso());
  const [ultima, setUltima] = useState<Colheita | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [calculando, setCalculando] = useState(false);

  async function colhi() {
    setErro(null); setCalculando(true);
    try {
      const c = await api.registrarColheita(id, data);
      setUltima(c);
      historico.recarregar();
    } catch (e) { setErro(textoErro(e)); }
    finally { setCalculando(false); }
  }

  async function apagarColheita(c: Colheita) {
    if (!confirm(`Apagar o registro de ${dataBr(c.data)}?`)) return;
    try {
      await api.apagarColheita(c.id);
      if (ultima?.id === c.id) setUltima(null);
      historico.recarregar();
    } catch (e) { setErro(textoErro(e)); }
  }

  async function apagarTalhao() {
    const t = talhao.dado;
    if (!t || !confirm(`Apagar o talhão ${t.nome} e o histórico dele?`)) return;
    try { await api.apagarTalhao(t.id); ir(link.fazenda(t.fazendaId)); } catch (e) { setErro(textoErro(e)); }
  }

  const t = talhao.dado;
  return (
    <>
      <Topo titulo={t?.nome ?? "Talhão"} voltar={t ? link.fazenda(t.fazendaId) : link.fazendas()} />
      <main className="conteudo">
        {talhao.carregando && !t && <Carregando />}
        {talhao.erro && <Erro texto={talhao.erro} tentar={talhao.recarregar} />}
        {t && <p className="resumo">{ha(t.areaHa)} ha · solo {t.classeSolo} ({t.cadMm} mm)</p>}

        <section className="cartao colhi">
          <h2>Colheu a soja deste talhão?</h2>
          <label>Data da colheita<input type="date" value={data} max={hojeIso()} onChange={(e) => setData(e.target.value)} /></label>
          <button className="principal grande" onClick={colhi} disabled={calculando || !data}>
            {calculando ? "Calculando…" : data === hojeIso() ? "Colhi hoje" : `Colhi em ${dataBr(data)}`}
          </button>
          {calculando && <small className="dica">Busca o clima da NASA: pode levar alguns segundos.</small>}
          {erro && <p className="erro" role="alert">{erro}</p>}
        </section>

        {ultima && <Recomendacao c={ultima} />}

        <h2 className="secao">Histórico</h2>
        {historico.carregando && !historico.dado && <Carregando />}
        {historico.erro && <Erro texto={historico.erro} tentar={historico.recarregar} />}
        {historico.dado?.length === 0 && <p className="dica">Nenhuma colheita registrada.</p>}
        <ul className="lista">
          {historico.dado?.map((c) => (
            <li key={c.id} className="item linha-item">
              <div><strong>{dataBr(c.data)}</strong><span><Selo cultura={c.cultura} /> decêndio {c.decendio}</span></div>
              <button className="perigo-link" onClick={() => apagarColheita(c)} aria-label={`Apagar colheita de ${dataBr(c.data)}`}>Apagar</button>
            </li>
          ))}
        </ul>

        {t && <button className="perigo" onClick={apagarTalhao}>Apagar talhão</button>}
      </main>
    </>
  );
}

function Recomendacao({ c }: { c: Colheita }) {
  return (
    <section className={`cartao recomendacao fundo-${c.cultura.normalize("NFD").replace(/[̀-ͯ]/g, "")}`} aria-live="polite">
      <small>Para plantar depois da colheita de {dataBr(c.data)}</small>
      <p className="cultura">{c.cultura}</p>
      <p>{PORQUE[c.cultura] ?? ""}</p>
      <dl className="riscos">
        {Object.entries(c.risco).map(([g, r]) => (
          <div key={g}><dt>{g}</dt><dd>falha em {pct(r)} dos anos <small>({c.nivel[g]})</small></dd></div>
        ))}
      </dl>
      <small>Risco aceito: {pct(c.riscoMax)}. Decêndio {c.decendio}. Chuva: {c.fonteChuva === "imerg" ? "IMERG (GPM/NASA)" : "NASA POWER"}.</small>
    </section>
  );
}

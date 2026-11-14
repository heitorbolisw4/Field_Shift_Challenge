import { useState, type FormEvent } from "react";
import type { Fazenda as TFazenda, PlanoFazenda } from "../api";
import { textoErro, useApi, useCarregar } from "../contexto";
import { ir, link } from "../rota";
import { corCultura, dataBr, diaMes, ha, hojeIso, lerNumero } from "../util";
import { BotaoLocal, Carregando, Erro, Selo, Topo } from "./comum";

const ABAS = [
  ["talhoes", "Talhões"],
  ["colhedoras", "Colhedoras"],
  ["plano", "Plano"],
  ["ajustes", "Ajustes"],
] as const;

export function Fazenda({ id, aba }: { id: number; aba: string }) {
  const api = useApi();
  const { dado: f, erro, carregando, recarregar } = useCarregar(() => api.fazenda(id), [id]);

  return (
    <>
      <Topo titulo={f?.nome ?? "Fazenda"} voltar={link.fazendas()} />
      <nav className="abas" aria-label="Seções da fazenda">
        {ABAS.map(([chave, nome]) => (
          <a key={chave} href={link.fazenda(id, chave)} aria-current={aba === chave ? "page" : undefined}>{nome}</a>
        ))}
      </nav>
      <main className="conteudo">
        {carregando && !f && <Carregando />}
        {erro && <Erro texto={erro} tentar={recarregar} />}
        {f && aba === "talhoes" && <Talhoes f={f} mudou={recarregar} />}
        {f && aba === "colhedoras" && <Colhedoras f={f} mudou={recarregar} />}
        {f && aba === "plano" && <Plano f={f} />}
        {f && aba === "ajustes" && <Ajustes f={f} mudou={recarregar} />}
      </main>
    </>
  );
}

// ---------- talhões ----------

const SOLOS = [
  ["AD1", "arenoso"], ["AD2", ""], ["AD3", "médio"], ["AD4", ""], ["AD5", ""], ["AD6", "argiloso"],
] as const;

function Talhoes({ f, mudou }: { f: TFazenda; mudou: () => void }) {
  const [novo, setNovo] = useState(f.talhoes.length === 0);
  return (
    <>
      <ul className="lista">
        {f.talhoes.map((t) => (
          <li key={t.id}>
            <a className="item" href={link.talhao(t.id)}>
              <strong>{t.nome}</strong>
              <span>{ha(t.areaHa)} ha · solo {t.classeSolo}{t.lat === null && f.lat === null ? " · ⚠ sem localização" : ""}</span>
            </a>
          </li>
        ))}
      </ul>
      {novo ? <NovoTalhao f={f} pronto={() => { setNovo(false); mudou(); }} cancelar={f.talhoes.length ? () => setNovo(false) : undefined} />
            : <button className="principal" onClick={() => setNovo(true)}>+ Novo talhão</button>}
    </>
  );
}

function NovoTalhao({ f, pronto, cancelar }: { f: TFazenda; pronto: () => void; cancelar?: () => void }) {
  const api = useApi();
  const [nome, setNome] = useState("");
  const [area, setArea] = useState("");
  const [solo, setSolo] = useState("AD3");
  const [lat, setLat] = useState("");
  const [lon, setLon] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    const a = lerNumero(area), la = lerNumero(lat), lo = lerNumero(lon);
    if (a === null || a <= 0) return setErro("Informe a área em hectares.");
    if ((lat || lon) && (la === null || lo === null)) return setErro("Latitude e longitude: informe as duas, com número.");
    if (la === null && f.lat === null) return setErro("Sem localização: informe a do talhão ou cadastre a da fazenda em Ajustes.");
    setEnviando(true);
    try {
      await api.criarTalhao(f.id, { nome, areaHa: a, classeSolo: solo, lat: la, lon: lo });
      pronto();
    } catch (e) {
      setErro(textoErro(e));
      setEnviando(false);
    }
  }

  return (
    <form className="cartao" onSubmit={enviar}>
      <h2>Novo talhão</h2>
      <label>Nome<input required value={nome} onChange={(e) => setNome(e.target.value)} placeholder="Talhão 1" /></label>
      <label>Área (ha)<input inputMode="decimal" value={area} onChange={(e) => setArea(e.target.value)} /></label>
      <fieldset className="segmentado">
        <legend>Solo (classe do ZARC)</legend>
        {SOLOS.map(([c, d]) => (
          <label key={c} className={solo === c ? "marcado" : ""}>
            <input type="radio" name="solo" value={c} checked={solo === c} onChange={() => setSolo(c)} />
            {c}{d && <small>{d}</small>}
          </label>
        ))}
      </fieldset>
      <small className="dica">AD1 segura pouca água, AD6 segura muita. Na dúvida, AD3.</small>
      <p className="dica">Localização do talhão (opcional{f.lat !== null ? "; sem ela, usa a da fazenda" : ""}).</p>
      <BotaoLocal aoPegar={(a, o) => { setLat(String(a)); setLon(String(o)); }} />
      <div className="linha">
        <label>Latitude<input inputMode="decimal" value={lat} onChange={(e) => setLat(e.target.value)} /></label>
        <label>Longitude<input inputMode="decimal" value={lon} onChange={(e) => setLon(e.target.value)} /></label>
      </div>
      {erro && <p className="erro" role="alert">{erro}</p>}
      <div className="acoes">
        {cancelar && <button type="button" className="secundario" onClick={cancelar}>Cancelar</button>}
        <button className="principal" disabled={enviando}>Salvar talhão</button>
      </div>
    </form>
  );
}

// ---------- colhedoras ----------

function Colhedoras({ f, mudou }: { f: TFazenda; mudou: () => void }) {
  const api = useApi();
  const [nome, setNome] = useState("");
  const [haDia, setHaDia] = useState("60");
  const [erro, setErro] = useState<string | null>(null);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    const h = lerNumero(haDia);
    if (h === null || h <= 0) return setErro("Informe quantos ha a colhedora faz por dia.");
    try {
      await api.criarColhedora(f.id, { nome, haDia: h });
      setNome(""); setErro(null);
      mudou();
    } catch (e) { setErro(textoErro(e)); }
  }

  async function apagar(id: number, nome: string) {
    if (!confirm(`Apagar a colhedora ${nome}?`)) return;
    try { await api.apagarColhedora(id); mudou(); } catch (e) { setErro(textoErro(e)); }
  }

  const capacidade = f.haDiaColhedoras * f.diasUteisPct / 100;
  return (
    <>
      {f.colhedoras.length > 0 && (
        <p className="resumo">Juntas: <b>{ha(f.haDiaColhedoras)} ha/dia</b>. Com {f.diasUteisPct}% de dias úteis, <b>{ha(capacidade)} ha por dia</b> de calendário.</p>
      )}
      <ul className="lista">
        {f.colhedoras.map((c) => (
          <li key={c.id} className="item linha-item">
            <div><strong>{c.nome}</strong><span>{ha(c.haDia)} ha/dia</span></div>
            <button className="perigo-link" onClick={() => apagar(c.id, c.nome)} aria-label={`Apagar ${c.nome}`}>Apagar</button>
          </li>
        ))}
      </ul>
      <form className="cartao" onSubmit={enviar}>
        <h2>Nova colhedora</h2>
        <label>Nome<input required value={nome} onChange={(e) => setNome(e.target.value)} placeholder="JD S780" /></label>
        <label>Hectares por dia de trabalho<input required inputMode="decimal" value={haDia} onChange={(e) => setHaDia(e.target.value)} /></label>
        <small className="dica">Estimativa: ajuste para a sua máquina.</small>
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="principal">Adicionar</button>
      </form>
    </>
  );
}

// ---------- plano de colheita ----------

function Plano({ f }: { f: TFazenda }) {
  const api = useApi();
  const [inicio, setInicio] = useState(hojeIso());
  const [plano, setPlano] = useState<PlanoFazenda | null>(null);
  const [erro, setErro] = useState<string | null>(null);
  const [calculando, setCalculando] = useState(false);

  async function calcular(e: FormEvent) {
    e.preventDefault();
    setErro(null); setCalculando(true);
    try { setPlano(await api.plano(f.id, inicio)); } catch (e) { setErro(textoErro(e)); setPlano(null); }
    finally { setCalculando(false); }
  }

  const faltando = !f.talhoes.length ? "Cadastre ao menos um talhão." : !f.colhedoras.length ? "Cadastre ao menos uma colhedora." : null;
  const total = plano?.totais.reduce((s, t) => s + t.ha, 0) ?? 0;

  return (
    <>
      <form className="cartao" onSubmit={calcular}>
        <h2>Quando começa a colheita da soja?</h2>
        <label>Início<input type="date" required value={inicio} onChange={(e) => setInicio(e.target.value)} /></label>
        <small className="dica">Talhões colhidos na ordem do cadastro, um depois do outro.</small>
        {faltando && <p className="erro">{faltando}</p>}
        <button className="principal" disabled={!!faltando || calculando}>{calculando ? "Calculando…" : "Ver plano"}</button>
        {calculando && <small className="dica">Busca o clima da NASA: pode levar alguns segundos.</small>}
      </form>
      {erro && <Erro texto={erro} />}
      {plano && (
        <section className="plano">
          <p className="resumo">Colheita de <b>{dataBr(plano.inicio)}</b> a <b>{dataBr(plano.fim)}</b>, {ha(plano.capacidadeHaDia)} ha/dia.</p>
          <div className="barra" aria-label="Hectares por cultura">
            {plano.totais.map((t) => (
              <span key={t.cultura} style={{ flex: t.ha, background: corCultura(t.cultura) }} title={`${t.cultura}: ${ha(t.ha)} ha`} />
            ))}
          </div>
          <ul className="totais">
            {plano.totais.map((t) => (
              <li key={t.cultura}><Selo cultura={t.cultura} /> <b>{ha(t.ha)} ha</b> <small>({Math.round(100 * t.ha / total)}%)</small></li>
            ))}
          </ul>
          {plano.talhoes.map((t) => (
            <div key={t.talhaoId} className="cartao talhao-plano">
              <a href={link.talhao(t.talhaoId)}><strong>{t.nome}</strong></a>
              <span className="dica">{ha(t.areaHa)} ha · {t.classeSolo} · {diaMes(t.de)} a {diaMes(t.ate)} ({t.dias} dia{t.dias > 1 ? "s" : ""})</span>
              <ul>
                {t.trechos.map((x, i) => (
                  <li key={i}><Selo cultura={x.cultura} /> {diaMes(x.de)}{x.ate !== x.de ? ` a ${diaMes(x.ate)}` : ""}: <b>{ha(x.ha ?? 0)} ha</b></li>
                ))}
              </ul>
            </div>
          ))}
        </section>
      )}
    </>
  );
}

// ---------- ajustes ----------

function Ajustes({ f, mudou }: { f: TFazenda; mudou: () => void }) {
  const api = useApi();
  const [nome, setNome] = useState(f.nome);
  const [lat, setLat] = useState(f.lat?.toString() ?? "");
  const [lon, setLon] = useState(f.lon?.toString() ?? "");
  const [uteis, setUteis] = useState(String(f.diasUteisPct));
  const [risco, setRisco] = useState(String(Math.round(f.riscoMax * 100)));
  const [msg, setMsg] = useState<{ ok: boolean; texto: string } | null>(null);

  async function salvar(e: FormEvent) {
    e.preventDefault();
    const la = lerNumero(lat), lo = lerNumero(lon), u = lerNumero(uteis), r = lerNumero(risco);
    if ((lat || lon) && (la === null || lo === null)) return setMsg({ ok: false, texto: "Latitude e longitude: informe as duas." });
    if (u === null || u <= 0 || u > 100) return setMsg({ ok: false, texto: "Dias úteis: de 1 a 100%." });
    if (r === null || r < 0 || r > 100) return setMsg({ ok: false, texto: "Risco aceito: de 0 a 100%." });
    try {
      await api.editarFazenda(f.id, { nome, lat: la, lon: lo, diasUteisPct: u, riscoMax: r / 100 });
      setMsg({ ok: true, texto: "Salvo." });
      mudou();
    } catch (e) { setMsg({ ok: false, texto: textoErro(e) }); }
  }

  async function apagar() {
    if (!confirm(`Apagar a fazenda ${f.nome} com todos os talhões, colhedoras e colheitas? Não dá para desfazer.`)) return;
    try { await api.apagarFazenda(f.id); ir(link.fazendas()); } catch (e) { setMsg({ ok: false, texto: textoErro(e) }); }
  }

  return (
    <>
      <form className="cartao" onSubmit={salvar}>
        <h2>Dados da fazenda</h2>
        <label>Nome<input required value={nome} onChange={(e) => setNome(e.target.value)} /></label>
        <BotaoLocal aoPegar={(a, o) => { setLat(String(a)); setLon(String(o)); }} />
        <div className="linha">
          <label>Latitude<input inputMode="decimal" value={lat} onChange={(e) => setLat(e.target.value)} /></label>
          <label>Longitude<input inputMode="decimal" value={lon} onChange={(e) => setLon(e.target.value)} /></label>
        </div>
        <label>Dias úteis de colheita (%)<input inputMode="decimal" value={uteis} onChange={(e) => setUteis(e.target.value)} /></label>
        <small className="dica">O resto é chuva e quebra. Padrão 70%.</small>
        <label>Risco aceito (%)<input inputMode="decimal" value={risco} onChange={(e) => setRisco(e.target.value)} /></label>
        <small className="dica">Milho ou sorgo só são indicados se falham em no máximo esse % dos anos. Padrão 20% (como o ZARC).</small>
        {msg && <p className={msg.ok ? "ok" : "erro"} role="status">{msg.texto}</p>}
        <button className="principal">Salvar</button>
      </form>
      <button className="perigo" onClick={apagar}>Apagar fazenda</button>
    </>
  );
}

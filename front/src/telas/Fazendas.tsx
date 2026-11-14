import { useState, type FormEvent } from "react";
import { textoErro, useApi, useCarregar, useUsuario } from "../contexto";
import { ir, link } from "../rota";
import { ha, lerNumero } from "../util";
import { BotaoLocal, Carregando, Erro, Topo } from "./comum";

export function Fazendas() {
  const api = useApi();
  const usuario = useUsuario();
  const { dado: fazendas, erro, carregando, recarregar } = useCarregar(() => api.fazendas(), []);
  const [nova, setNova] = useState(false);

  return (
    <>
      <Topo titulo="Minhas fazendas" direita={<button className="link" onClick={() => api.sair()}>Sair</button>} />
      <main className="conteudo">
        {usuario && <p className="ola">Olá{usuario.nome ? `, ${usuario.nome}` : ""}.</p>}
        {carregando && <Carregando />}
        {erro && <Erro texto={erro} tentar={recarregar} />}
        {fazendas && fazendas.length === 0 && !nova && (
          <div className="vazio">
            <p>Nenhuma fazenda ainda.</p>
            <p>Cadastre a fazenda, os talhões e as colhedoras. Depois, a cada talhão colhido, o app diz o que plantar.</p>
          </div>
        )}
        <ul className="lista">
          {fazendas?.map((f) => (
            <li key={f.id}>
              <a className="item" href={link.fazenda(f.id)}>
                <strong>{f.nome}</strong>
                <span>{ha(f.areaHa)} ha · {f.talhoes.length} talh{f.talhoes.length === 1 ? "ão" : "ões"} · {f.colhedoras.length} colhedora{f.colhedoras.length === 1 ? "" : "s"}</span>
              </a>
            </li>
          ))}
        </ul>
        {nova ? <NovaFazenda cancelar={() => setNova(false)} /> : <button className="principal" onClick={() => setNova(true)}>+ Nova fazenda</button>}
      </main>
    </>
  );
}

function NovaFazenda({ cancelar }: { cancelar: () => void }) {
  const api = useApi();
  const [nome, setNome] = useState("");
  const [lat, setLat] = useState("");
  const [lon, setLon] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    const la = lerNumero(lat), lo = lerNumero(lon);
    if ((lat || lon) && (la === null || lo === null)) return setErro("Latitude e longitude: informe as duas, com número.");
    setEnviando(true);
    try {
      const f = await api.criarFazenda({ nome, lat: la, lon: lo });
      ir(link.fazenda(f.id));
    } catch (e) {
      setErro(textoErro(e));
      setEnviando(false);
    }
  }

  return (
    <form className="cartao" onSubmit={enviar}>
      <h2>Nova fazenda</h2>
      <label>Nome<input required value={nome} onChange={(e) => setNome(e.target.value)} /></label>
      <p className="dica">Localização da sede: usada nos talhões sem localização própria.</p>
      <BotaoLocal aoPegar={(a, o) => { setLat(String(a)); setLon(String(o)); }} />
      <div className="linha">
        <label>Latitude<input inputMode="decimal" value={lat} onChange={(e) => setLat(e.target.value)} placeholder="-19,03" /></label>
        <label>Longitude<input inputMode="decimal" value={lon} onChange={(e) => setLon(e.target.value)} placeholder="-47,92" /></label>
      </div>
      {erro && <p className="erro" role="alert">{erro}</p>}
      <div className="acoes">
        <button type="button" className="secundario" onClick={cancelar}>Cancelar</button>
        <button className="principal" disabled={enviando}>Criar</button>
      </div>
    </form>
  );
}

import { useState, type FormEvent } from "react";
import { textoErro, useApi } from "../contexto";

export function Entrar() {
  const api = useApi();
  const [criar, setCriar] = useState(false);
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [nome, setNome] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    if (criar && senha.length < 8) return setErro("A senha precisa ter ao menos 8 caracteres.");
    setEnviando(true);
    try {
      if (criar) await api.registrar(email, senha, nome);
      else await api.entrar(email, senha);
    } catch (e) {
      setErro(textoErro(e));
    } finally {
      setEnviando(false);
    }
  }

  return (
    <main className="entrar">
      <div className="marca">
        <span className="folha" aria-hidden>🌱</span>
        <h1>Safrinha Certa</h1>
        <p>Colheu a soja? Veja o que plantar em cada talhão.</p>
      </div>

      <form className="cartao" onSubmit={enviar}>
        <h2>{criar ? "Criar conta" : "Entrar"}</h2>
        {criar && (
          <label>Seu nome
            <input value={nome} onChange={(e) => setNome(e.target.value)} autoComplete="name" />
          </label>
        )}
        <label>E-mail
          <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" inputMode="email" />
        </label>
        <label>Senha
          <input type="password" required value={senha} onChange={(e) => setSenha(e.target.value)}
                 autoComplete={criar ? "new-password" : "current-password"} minLength={criar ? 8 : undefined} />
        </label>
        {criar && <small className="dica">Mínimo 8 caracteres.</small>}
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="principal" disabled={enviando}>{enviando ? "Aguarde…" : criar ? "Criar conta" : "Entrar"}</button>
        <button type="button" className="link" onClick={() => { setCriar(!criar); setErro(null); }}>
          {criar ? "Já tenho conta" : "Não tenho conta: criar"}
        </button>
      </form>
    </main>
  );
}

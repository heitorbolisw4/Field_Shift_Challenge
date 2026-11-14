// cliente da Farm.Api: login, renovação automática do token e as rotas da fazenda.
// O JWT de acesso (20 min) fica só em memória; o refresh token (30 dias) fica no aparelho para manter o login.

export type Usuario = { id: string; email: string; nome: string | null };
export type Tokens = { accessToken: string; expiraEm: string; refreshToken: string; refreshExpiraEm: string; usuario: Usuario };

export type Talhao = {
  id: number; fazendaId: number; nome: string; areaHa: number; classeSolo: string; cadMm: number;
  lat: number | null; lon: number | null; poligono: unknown | null;
};
export type Colhedora = { id: number; fazendaId: number; nome: string; haDia: number };
export type Fazenda = {
  id: number; nome: string; lat: number | null; lon: number | null; diasUteisPct: number; riscoMax: number;
  areaHa: number; haDiaColhedoras: number; talhoes: Talhao[]; colhedoras: Colhedora[];
};
export type Colheita = {
  id: number; talhaoId: number; data: string; cultura: string; decendio: string;
  risco: Record<string, number>; nivel: Record<string, string>; cad: number; riscoMax: number; fonteChuva: string; criadoEm: string;
};
export type Trecho = { cultura: string; de: string; ate: string; dias: number; ha: number | null };
export type PlanoFazenda = {
  fazendaId: number; inicio: string; fim: string; capacidadeHaDia: number;
  talhoes: { talhaoId: number; nome: string; classeSolo: string; areaHa: number; de: string; ate: string; dias: number; trechos: Trecho[] }[];
  totais: { cultura: string; ha: number }[];
};
export type Solo = { classe: string; cadMm: number };

export type FazendaIn = { nome: string; lat?: number | null; lon?: number | null; diasUteisPct?: number; riscoMax?: number };
export type TalhaoIn = { nome: string; areaHa?: number | null; classeSolo?: string; lat?: number | null; lon?: number | null };
export type ColhedoraIn = { nome: string; haDia: number };

export class ApiErro extends Error {
  constructor(public status: number, mensagem: string) {
    super(mensagem);
  }
}

const CHAVE = "farm.refresh";

// mensagem legível de uma resposta de erro (a API manda texto JSON, ex.: "e-mail inválido")
async function mensagem(r: Response): Promise<string> {
  const texto = await r.text();
  if (r.status === 401) return "Sessão expirada. Entre de novo.";
  if (r.status === 429) return "Muitas tentativas. Espere um minuto.";
  if (!texto) return `Erro ${r.status}`;
  try {
    const j = JSON.parse(texto);
    if (typeof j === "string") return j;
    if (j?.title) return j.title;
  } catch { /* texto puro */ }
  return texto;
}

export function criarApi(fetchFn: typeof fetch = (...a) => fetch(...a), armazenamento: Storage = localStorage) {
  let access: string | null = null;
  let usuario: Usuario | null = null;
  let renovando: Promise<boolean> | null = null; // um refresh por vez: dois em paralelo derrubariam a sessão (rotação)
  const ouvintes = new Set<(u: Usuario | null) => void>();

  const avisar = () => ouvintes.forEach((f) => f(usuario));

  function guardar(t: Tokens) {
    access = t.accessToken;
    usuario = t.usuario;
    try { armazenamento.setItem(CHAVE, t.refreshToken); } catch { /* modo privado */ }
    avisar();
  }

  function limpar() {
    access = null;
    usuario = null;
    try { armazenamento.removeItem(CHAVE); } catch { /* modo privado */ }
    avisar();
  }

  const refreshGuardado = () => { try { return armazenamento.getItem(CHAVE); } catch { return null; } };

  async function postAuth(rota: string, corpo: unknown): Promise<Tokens> {
    const r = await fetchFn(rota, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(corpo) });
    if (!r.ok) throw new ApiErro(r.status, r.status === 401 && rota === "/auth/login" ? "E-mail ou senha errados." : await mensagem(r));
    const t = (await r.json()) as Tokens;
    guardar(t);
    return t;
  }

  // troca o refresh guardado por um par novo; false = sem sessão
  function renovar(): Promise<boolean> {
    renovando ??= (async () => {
      const rt = refreshGuardado();
      if (!rt) return false;
      try {
        await postAuth("/auth/renovar", { refreshToken: rt });
        return true;
      } catch (e) {
        if (e instanceof ApiErro && e.status === 401) limpar(); // refresh vencido ou revogado
        return false;
      } finally {
        renovando = null;
      }
    })();
    return renovando;
  }

  async function chamar<T>(metodo: string, rota: string, corpo?: unknown, tentou = false): Promise<T> {
    if (!access && !(await renovar())) throw new ApiErro(401, "Entre para continuar.");
    const r = await fetchFn(rota, {
      method: metodo,
      headers: { Authorization: `Bearer ${access}`, ...(corpo !== undefined ? { "Content-Type": "application/json" } : {}) },
      body: corpo !== undefined ? JSON.stringify(corpo) : undefined,
    });
    if (r.status === 401 && !tentou) {
      access = null; // JWT venceu: renova e tenta uma vez
      return chamar<T>(metodo, rota, corpo, true);
    }
    if (r.status === 401) { limpar(); throw new ApiErro(401, "Sessão expirada. Entre de novo."); }
    if (!r.ok) throw new ApiErro(r.status, await mensagem(r));
    if (r.status === 204) return undefined as T;
    return (await r.json()) as T;
  }

  // rotas públicas (sem token)
  async function publica<T>(rota: string): Promise<T> {
    const r = await fetchFn(rota);
    if (!r.ok) throw new ApiErro(r.status, await mensagem(r));
    return (await r.json()) as T;
  }

  return {
    get usuario() { return usuario; },
    temSessaoGuardada: () => refreshGuardado() !== null,
    ouvir(f: (u: Usuario | null) => void) { ouvintes.add(f); return () => { ouvintes.delete(f); }; },

    entrar: (email: string, senha: string) => postAuth("/auth/login", { email, senha }),
    registrar: (email: string, senha: string, nome?: string) => postAuth("/auth/registrar", { email, senha, nome: nome || null }),
    restaurar: renovar, // ao abrir o app: volta a sessão a partir do refresh guardado
    async sair() {
      const rt = refreshGuardado();
      limpar();
      if (rt) await fetchFn("/auth/sair", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ refreshToken: rt }) }).catch(() => {});
    },

    solos: () => publica<Solo[]>("/solos"),

    fazendas: () => chamar<Fazenda[]>("GET", "/fazendas"),
    fazenda: (id: number) => chamar<Fazenda>("GET", `/fazendas/${id}`),
    criarFazenda: (f: FazendaIn) => chamar<Fazenda>("POST", "/fazendas", f),
    editarFazenda: (id: number, f: FazendaIn) => chamar<Fazenda>("PUT", `/fazendas/${id}`, f),
    apagarFazenda: (id: number) => chamar<void>("DELETE", `/fazendas/${id}`),

    talhao: (id: number) => chamar<Talhao>("GET", `/talhoes/${id}`),
    criarTalhao: (fazendaId: number, t: TalhaoIn) => chamar<Talhao>("POST", `/fazendas/${fazendaId}/talhoes`, t),
    editarTalhao: (id: number, t: TalhaoIn) => chamar<Talhao>("PUT", `/talhoes/${id}`, t),
    apagarTalhao: (id: number) => chamar<void>("DELETE", `/talhoes/${id}`),

    criarColhedora: (fazendaId: number, c: ColhedoraIn) => chamar<Colhedora>("POST", `/fazendas/${fazendaId}/colhedoras`, c),
    apagarColhedora: (id: number) => chamar<void>("DELETE", `/colhedoras/${id}`),

    colheitas: (talhaoId: number) => chamar<Colheita[]>("GET", `/talhoes/${talhaoId}/colheitas`),
    registrarColheita: (talhaoId: number, data: string) => chamar<Colheita>("POST", `/talhoes/${talhaoId}/colheitas`, { data }),
    apagarColheita: (id: number) => chamar<void>("DELETE", `/colheitas/${id}`),

    plano: (fazendaId: number, inicio: string, ordem?: number[]) =>
      chamar<PlanoFazenda>("GET", `/fazendas/${fazendaId}/plano-colheita?inicio=${inicio}${ordem?.length ? `&ordem=${ordem.join(",")}` : ""}`),
  };
}

export type Api = ReturnType<typeof criarApi>;

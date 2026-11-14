// Rodada independente de testes do cliente da API (ee683a9): refresh concorrente, refresh vencido, sem rede.
// it.skip("BUG: ...") = bug encontrado e não corrigido (convenção do projeto: os testes só relatam).
import { describe, expect, it } from "vitest";
import { ApiErro, criarApi } from "../api";
import { textoErro } from "../contexto";
import { Memoria, servidor, tokens } from "./servidor";

const semRede = (() => Promise.reject(new TypeError("Failed to fetch"))) as typeof fetch;

// promessa que o teste solta quando quiser
function trava() {
  let soltar!: () => void;
  const p = new Promise<void>((ok) => { soltar = ok; });
  return { p, soltar };
}

const renovacoes = (s: ReturnType<typeof servidor>) => s.chamadas.filter((c) => c.rota === "/auth/renovar");

describe("refresh concorrente", () => {
  it("cinco chamadas em paralelo, com rotas diferentes, fazem um só refresh e todas usam o JWT novo", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const t = tokens();
    const { p, soltar } = trava();
    const s = servidor({
      "POST /auth/renovar": async () => { await p; return { corpo: t }; },
      "GET /fazendas": { corpo: [] },
      "GET /fazendas/1": { corpo: { id: 1 } },
      "GET /talhoes/2": { corpo: { id: 2 } },
      "GET /talhoes/2/colheitas": { corpo: [] },
      "POST /fazendas": { status: 201, corpo: { id: 9 } },
    });
    const api = criarApi(s.fetch, mem);
    const todas = Promise.all([api.fazendas(), api.fazenda(1), api.talhao(2), api.colheitas(2), api.criarFazenda({ nome: "X" })]);
    soltar();
    await todas;
    expect(renovacoes(s)).toHaveLength(1);
    expect(s.chamadas.filter((c) => c.rota !== "/auth/renovar").every((c) => c.auth === `Bearer ${t.accessToken}`)).toBe(true);
    expect(mem.getItem("farm.refresh")).toBe(t.refreshToken);
  });

  it("depois de um refresh terminar, o próximo 401 faz outro refresh com o refresh NOVO (não reusa o velho)", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const t1 = tokens(), t2 = tokens();
    let n = 0;
    let pedeRenovar = false;
    const s = servidor({
      "POST /auth/renovar": () => ({ corpo: n++ === 0 ? t1 : t2 }),
      "GET /fazendas": () => (pedeRenovar ? ((pedeRenovar = false), { status: 401 }) : { corpo: [] }),
    });
    const api = criarApi(s.fetch, mem);
    await api.fazendas();
    pedeRenovar = true;
    await api.fazendas();
    expect(renovacoes(s).map((c) => c.corpo)).toEqual([{ refreshToken: "rt-velho" }, { refreshToken: t1.refreshToken }]);
  });

  it("restaurar() chamado duas vezes (StrictMode monta o efeito duas vezes) faz um só refresh", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const s = servidor({ "POST /auth/renovar": { corpo: tokens() } });
    const api = criarApi(s.fetch, mem);
    const [a, b] = await Promise.all([api.restaurar(), api.restaurar()]);
    expect([a, b]).toEqual([true, true]);
    expect(renovacoes(s)).toHaveLength(1);
  });

  it.skip("BUG: abrir o app sem login guardado trava a renovação para sempre (renovando fica com a promessa resolvida)", async () => {
    // renovar(): sem refresh guardado, a função async retorna sem nenhum await, então o finally (renovando = null)
    // roda ANTES do `renovando ??= ...` atribuir. renovando fica preso numa promessa resolvida com false.
    const mem = new Memoria();
    let jwtVenceu = false;
    const t1 = tokens(), t2 = tokens();
    const s = servidor({
      "POST /auth/login": { corpo: t1 },
      "POST /auth/renovar": { corpo: t2 },
      "GET /fazendas": () => (jwtVenceu ? ((jwtVenceu = false), { status: 401 }) : { corpo: [] }),
    });
    const api = criarApi(s.fetch, mem);
    expect(await api.restaurar()).toBe(false); // App.tsx não chama isso sem refresh guardado, mas a tela de login + 1ª chamada sim (ver abaixo)
    await api.entrar("a@x.com", "senha-boa-1");
    jwtVenceu = true; // 20 min depois
    await expect(api.fazendas()).resolves.toEqual([]); // hoje: ApiErro 401 "Entre para continuar." sem nem chamar /auth/renovar
    expect(renovacoes(s)).toHaveLength(1);
  });

  it.skip("BUG: mesma trava sem restaurar(): qualquer chamada antes do login (ex.: aba aberta após 'Sair') prende a renovação", async () => {
    const mem = new Memoria();
    const t1 = tokens(), t2 = tokens();
    let jwtVenceu = false;
    const s = servidor({
      "POST /auth/login": { corpo: t1 },
      "POST /auth/renovar": { corpo: t2 },
      "GET /fazendas": () => (jwtVenceu ? ((jwtVenceu = false), { status: 401 }) : { corpo: [] }),
    });
    const api = criarApi(s.fetch, mem);
    await api.fazendas().catch(() => {}); // sem sessão: "Entre para continuar." (renovar() sem refresh guardado)
    await api.entrar("a@x.com", "senha-boa-1");
    jwtVenceu = true;
    await expect(api.fazendas()).resolves.toEqual([]);
  });

  it("a trava acima também impede restaurar uma sessão que apareceu depois no aparelho", async () => {
    // documenta o mesmo bug pelo lado observável mais simples; passa hoje e deve continuar passando depois da correção
    const mem = new Memoria();
    const s = servidor({ "POST /auth/renovar": { corpo: tokens() } });
    const api = criarApi(s.fetch, mem);
    expect(await api.restaurar()).toBe(false);
    expect(renovacoes(s)).toHaveLength(0);
  });

  it.skip("BUG: 'Sair' durante um refresh em andamento ressuscita a sessão", async () => {
    // sair() limpa e manda /auth/sair com o refresh velho; o refresh em voo termina depois e guarda o par novo.
    // Na API, /auth/sair com o refresh já rotacionado não revoga nada, então o refresh novo continua valendo.
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const { p, soltar } = trava();
    const s = servidor({
      "POST /auth/renovar": async () => { await p; return { corpo: tokens() }; },
      "POST /auth/sair": { status: 204 },
      "GET /fazendas": { corpo: [] },
    });
    const api = criarApi(s.fetch, mem);
    const carregando = api.fazendas().catch(() => {});
    await api.sair();
    soltar();
    await carregando;
    expect(api.usuario).toBeNull();
    expect(mem.getItem("farm.refresh")).toBeNull();
  });

  it("401 que chega depois de outro pedido já ter renovado: faz um refresh a mais (com o token novo), sem derrubar a sessão", async () => {
    // A e B saem com o JWT velho; A recebe 401 e renova; a resposta 401 de B chega depois disso.
    // Hoje B zera o JWT novo e renova de novo: custo extra, mas válido para a API (usa o refresh novo, não o velho).
    const mem = new Memoria();
    const t0 = tokens(), t1 = tokens(), t2 = tokens();
    const tB = trava();
    let n = 0;
    const s = servidor({
      "POST /auth/login": { corpo: t0 },
      "POST /auth/renovar": () => ({ corpo: n++ === 0 ? t1 : t2 }),
      "GET /fazendas": (r) => (r.auth === `Bearer ${t0.accessToken}` ? { status: 401 } : { corpo: [] }),
      "GET /fazendas/1": async (r) => {
        if (r.auth === `Bearer ${t0.accessToken}`) { await tB.p; return { status: 401 }; }
        return { corpo: { id: 1 } };
      },
    });
    const api = criarApi(s.fetch, mem);
    await api.entrar("a@x.com", "x");
    const b = api.fazenda(1);
    await api.fazendas(); // A: 401 → renova (rt t0 → t1)
    tB.soltar();
    await b;
    const corpos = renovacoes(s).map((c) => (c.corpo as { refreshToken: string }).refreshToken);
    expect(new Set(corpos).size).toBe(corpos.length); // nunca o mesmo refresh duas vezes
    expect(corpos.length).toBeLessThanOrEqual(2);
  });
});

describe("refresh vencido ou revogado", () => {
  it("401 no refresh avisa quem ouve com null (a tela volta ao login)", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const s = servidor({ "POST /auth/renovar": { status: 401 } });
    const api = criarApi(s.fetch, mem);
    const avisos: unknown[] = [];
    api.ouvir((u) => avisos.push(u));
    expect(await api.restaurar()).toBe(false);
    expect(avisos).toEqual([null]);
  });

  it("refresh revogado no meio do uso: JWT 401 → refresh 401 → erro de sessão e aparelho limpo", async () => {
    const mem = new Memoria();
    const s = servidor({
      "POST /auth/login": { corpo: tokens() },
      "POST /auth/renovar": { status: 401 },
      "GET /fazendas": { status: 401 },
    });
    const api = criarApi(s.fetch, mem);
    await api.entrar("a@x.com", "x");
    const e = (await api.fazendas().catch((x) => x)) as ApiErro;
    expect(e).toBeInstanceOf(ApiErro);
    expect(e.status).toBe(401);
    expect(api.usuario).toBeNull();
    expect(mem.getItem("farm.refresh")).toBeNull();
    expect(s.chamadas.filter((c) => c.rota === "/fazendas")).toHaveLength(1); // não repete a chamada sem JWT
  });

  it("chamadas paralelas com refresh vencido: um só /auth/renovar e todas recebem 401", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const s = servidor({ "POST /auth/renovar": { status: 401 } });
    const api = criarApi(s.fetch, mem);
    const r = await Promise.allSettled([api.fazendas(), api.fazenda(1), api.talhao(1)]);
    expect(r.every((x) => x.status === "rejected" && (x.reason as ApiErro).status === 401)).toBe(true);
    expect(renovacoes(s)).toHaveLength(1);
  });

  it("depois do refresh vencido, entrar de novo funciona e renova normalmente", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    let renovarOk = false;
    const t = tokens();
    const s = servidor({
      "POST /auth/renovar": () => (renovarOk ? { corpo: tokens() } : { status: 401 }),
      "POST /auth/login": { corpo: t },
      "GET /fazendas": { corpo: [] },
    });
    const api = criarApi(s.fetch, mem);
    expect(await api.restaurar()).toBe(false);
    await api.entrar("a@x.com", "x");
    expect(mem.getItem("farm.refresh")).toBe(t.refreshToken);
    renovarOk = true;
    expect(await api.restaurar()).toBe(true);
  });

  it("erro 500 no refresh não apaga o login guardado (só 401 apaga)", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const s = servidor({ "POST /auth/renovar": { status: 500 } });
    const api = criarApi(s.fetch, mem);
    expect(await api.restaurar()).toBe(false);
    expect(mem.getItem("farm.refresh")).toBe("rt-velho");
  });

  it("429 no refresh não apaga o login guardado", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const s = servidor({ "POST /auth/renovar": { status: 429 } });
    expect(await criarApi(s.fetch, mem).restaurar()).toBe(false);
    expect(mem.getItem("farm.refresh")).toBe("rt-velho");
  });

  it("localStorage que lança (modo privado) não quebra login nem chamadas", async () => {
    const quebrado = new Memoria();
    quebrado.setItem = () => { throw new Error("QuotaExceededError"); };
    quebrado.getItem = () => { throw new Error("SecurityError"); };
    quebrado.removeItem = () => { throw new Error("SecurityError"); };
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "GET /fazendas": { corpo: [] }, "POST /auth/sair": { status: 204 } });
    const api = criarApi(s.fetch, quebrado);
    expect(api.temSessaoGuardada()).toBe(false);
    await api.entrar("a@x.com", "x");
    await expect(api.fazendas()).resolves.toEqual([]);
    await api.sair();
    expect(api.usuario).toBeNull();
  });
});

describe("sem rede", () => {
  it("falha de rede numa chamada comum vira 'Sem conexão com o servidor.' e mantém a sessão", async () => {
    const mem = new Memoria();
    let online = true;
    const s = servidor({ "POST /auth/login": { corpo: tokens() } });
    const f = ((...a: Parameters<typeof fetch>) => (online ? s.fetch(...a) : semRede(...a))) as typeof fetch;
    const api = criarApi(f, mem);
    await api.entrar("a@x.com", "x");
    online = false;
    const e = await api.fazendas().catch((x) => x);
    expect(textoErro(e)).toBe("Sem conexão com o servidor.");
    expect(api.usuario).not.toBeNull();
    expect(mem.getItem("farm.refresh")).not.toBeNull();
  });

  it("sem rede no login mostra 'Sem conexão'", async () => {
    const e = await criarApi(semRede, new Memoria()).entrar("a@x.com", "x").catch((x) => x);
    expect(textoErro(e)).toBe("Sem conexão com o servidor.");
  });

  it("sair sem rede ainda limpa o aparelho e não lança", async () => {
    const mem = new Memoria();
    let online = true;
    const s = servidor({ "POST /auth/login": { corpo: tokens() } });
    const f = ((...a: Parameters<typeof fetch>) => (online ? s.fetch(...a) : semRede(...a))) as typeof fetch;
    const api = criarApi(f, mem);
    await api.entrar("a@x.com", "x");
    online = false;
    await expect(api.sair()).resolves.toBeUndefined();
    expect(mem.getItem("farm.refresh")).toBeNull();
  });

  it("rede volta depois de falhar no refresh: a próxima chamada renova com o mesmo refresh guardado", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    let online = false;
    const s = servidor({ "POST /auth/renovar": { corpo: tokens() }, "GET /fazendas": { corpo: [] } });
    const f = ((...a: Parameters<typeof fetch>) => (online ? s.fetch(...a) : semRede(...a))) as typeof fetch;
    const api = criarApi(f, mem);
    expect(await api.restaurar()).toBe(false);
    online = true;
    await expect(api.fazendas()).resolves.toEqual([]);
    expect(renovacoes(s)[0].corpo).toEqual({ refreshToken: "rt-velho" });
  });

  it.skip("BUG: sem rede quando o JWT venceu, a tela diz 'Entre para continuar.' em vez de 'Sem conexão'", async () => {
    // chamar(): renovar() devolve false tanto para 'sem refresh' quanto para 'falha de rede', e vira ApiErro(401).
    const mem = new Memoria();
    let online = true;
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "GET /fazendas": { status: 401 } });
    const f = ((...a: Parameters<typeof fetch>) => (online || !String(a[0]).startsWith("/auth/renovar") ? s.fetch(...a) : semRede(...a))) as typeof fetch;
    const api = criarApi(f, mem);
    await api.entrar("a@x.com", "x");
    online = false; // a rede cai bem na hora do refresh
    const e = await api.fazendas().catch((x) => x);
    expect(textoErro(e)).toBe("Sem conexão com o servidor.");
  });

  it("sem rede quando o JWT venceu: pelo menos não apaga o login guardado", async () => {
    const mem = new Memoria();
    let online = true;
    const t = tokens();
    const s = servidor({ "POST /auth/login": { corpo: t }, "GET /fazendas": { status: 401 } });
    const f = ((...a: Parameters<typeof fetch>) => (online || !String(a[0]).startsWith("/auth/renovar") ? s.fetch(...a) : semRede(...a))) as typeof fetch;
    const api = criarApi(f, mem);
    await api.entrar("a@x.com", "x");
    online = false;
    await api.fazendas().catch(() => {});
    expect(mem.getItem("farm.refresh")).toBe(t.refreshToken);
  });

  it("resposta de erro com corpo que não é JSON mostra o texto puro", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() } });
    const f = (async (u: RequestInfo | URL, i?: RequestInit) =>
      String(u) === "/fazendas" ? new Response("<html>502 Bad Gateway</html>", { status: 502 }) : s.fetch(u, i)) as typeof fetch;
    const api = criarApi(f, new Memoria());
    await api.entrar("a@x.com", "x");
    const e = (await api.fazendas().catch((x) => x)) as ApiErro;
    expect(e.status).toBe(502);
    expect(e.message).toContain("502");
  });

  it("problem+json da API usa o title", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "GET /fazendas": { status: 400, corpo: { title: "One or more validation errors occurred." } } });
    const api = criarApi(s.fetch, new Memoria());
    await api.entrar("a@x.com", "x");
    await expect(api.fazendas()).rejects.toThrow("One or more validation errors occurred.");
  });
});

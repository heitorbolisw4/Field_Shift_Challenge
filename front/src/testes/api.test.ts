import { describe, expect, it, vi } from "vitest";
import { ApiErro, criarApi } from "../api";
import { Memoria, servidor, tokens } from "./servidor";

describe("login", () => {
  it("entrar guarda o refresh, avisa quem ouve e usa o JWT nas chamadas", async () => {
    const t = tokens();
    const s = servidor({ "POST /auth/login": { corpo: t }, "GET /fazendas": { corpo: [] } });
    const mem = new Memoria();
    const api = criarApi(s.fetch, mem);
    const ouvinte = vi.fn();
    api.ouvir(ouvinte);

    await api.entrar("ana@x.com", "senha-boa-1");
    expect(mem.getItem("farm.refresh")).toBe(t.refreshToken);
    expect(api.usuario?.nome).toBe("Ana");
    expect(ouvinte).toHaveBeenCalledWith(t.usuario);

    await api.fazendas();
    expect(s.chamadas.at(-1)?.auth).toBe(`Bearer ${t.accessToken}`);
  });

  it("senha errada vira mensagem clara", async () => {
    const s = servidor({ "POST /auth/login": { status: 401 } });
    const api = criarApi(s.fetch, new Memoria());
    await expect(api.entrar("a@x.com", "errada")).rejects.toThrow("E-mail ou senha errados.");
    expect(api.usuario).toBeNull();
  });

  it("erro de cadastro mostra o texto da API", async () => {
    const s = servidor({ "POST /auth/registrar": { status: 409, corpo: "e-mail já cadastrado" } });
    const api = criarApi(s.fetch, new Memoria());
    await expect(api.registrar("a@x.com", "12345678")).rejects.toThrow("e-mail já cadastrado");
  });

  it("429 vira 'espere um minuto'", async () => {
    const s = servidor({ "POST /auth/login": { status: 429 } });
    await expect(criarApi(s.fetch, new Memoria()).entrar("a@x.com", "x")).rejects.toThrow(/Espere um minuto/);
  });
});

describe("renovação do token", () => {
  it("ao abrir o app, restaura a sessão pelo refresh guardado", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const novo = tokens();
    const s = servidor({ "POST /auth/renovar": { corpo: novo } });
    const api = criarApi(s.fetch, mem);

    expect(api.temSessaoGuardada()).toBe(true);
    expect(await api.restaurar()).toBe(true);
    expect(s.chamadas[0].corpo).toEqual({ refreshToken: "rt-velho" });
    expect(mem.getItem("farm.refresh")).toBe(novo.refreshToken); // rotação: guarda o novo
    expect(api.usuario).not.toBeNull();
  });

  it("JWT vencido (401): renova uma vez e repete a chamada", async () => {
    const t1 = tokens(), t2 = tokens();
    let primeira = true;
    const s = servidor({
      "POST /auth/login": { corpo: t1 },
      "POST /auth/renovar": { corpo: t2 },
      "GET /fazendas": (r) => {
        if (primeira) { primeira = false; return { status: 401 }; }
        expect(r.auth).toBe(`Bearer ${t2.accessToken}`);
        return { corpo: [{ id: 1 }] };
      },
    });
    const api = criarApi(s.fetch, new Memoria());
    await api.entrar("a@x.com", "x");
    expect(await api.fazendas()).toEqual([{ id: 1 }]);
    expect(s.chamadas.filter((c) => c.rota === "/auth/renovar")).toHaveLength(1);
  });

  it("duas chamadas ao mesmo tempo sem JWT fazem um só refresh (dois derrubariam a sessão)", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    let soltar!: () => void;
    const espera = new Promise<void>((ok) => { soltar = ok; });
    const s = servidor({
      "POST /auth/renovar": async () => { await espera; return { corpo: tokens() }; },
      "GET /fazendas": { corpo: [] },
      "GET /solos": { corpo: [] },
    });
    const api = criarApi(s.fetch, mem);
    const a = api.fazendas();
    const b = api.fazendas();
    soltar();
    await Promise.all([a, b]);
    expect(s.chamadas.filter((c) => c.rota === "/auth/renovar")).toHaveLength(1);
  });

  it("refresh vencido: apaga a sessão e pede login", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const s = servidor({ "POST /auth/renovar": { status: 401 } });
    const api = criarApi(s.fetch, mem);
    const erro = await api.fazendas().catch((e) => e);
    expect(erro).toBeInstanceOf(ApiErro);
    expect((erro as ApiErro).status).toBe(401);
    expect(mem.getItem("farm.refresh")).toBeNull();
    expect(api.temSessaoGuardada()).toBe(false);
  });

  it("sem rede no refresh: mantém o refresh guardado (tenta de novo depois)", async () => {
    const mem = new Memoria();
    mem.setItem("farm.refresh", "rt-velho");
    const api = criarApi((() => Promise.reject(new TypeError("Failed to fetch"))) as typeof fetch, mem);
    expect(await api.restaurar()).toBe(false);
    expect(mem.getItem("farm.refresh")).toBe("rt-velho");
  });

  it("401 de novo depois de renovar: sai da sessão", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "POST /auth/renovar": { corpo: tokens() }, "GET /fazendas": { status: 401 } });
    const mem = new Memoria();
    const api = criarApi(s.fetch, mem);
    await api.entrar("a@x.com", "x");
    await expect(api.fazendas()).rejects.toThrow(/Sessão expirada/);
    expect(api.usuario).toBeNull();
    expect(mem.getItem("farm.refresh")).toBeNull();
  });
});

describe("rotas", () => {
  it("sair revoga o refresh na API e limpa o aparelho", async () => {
    const t = tokens();
    const s = servidor({ "POST /auth/login": { corpo: t }, "POST /auth/sair": { status: 204 } });
    const mem = new Memoria();
    const api = criarApi(s.fetch, mem);
    await api.entrar("a@x.com", "x");
    await api.sair();
    expect(s.chamadas.at(-1)).toMatchObject({ rota: "/auth/sair", corpo: { refreshToken: t.refreshToken } });
    expect(api.usuario).toBeNull();
    expect(mem.getItem("farm.refresh")).toBeNull();
  });

  it("erro 400 traz a mensagem da API", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "POST /fazendas/3/talhoes": { status: 400, corpo: "classeSolo deve ser AD1 … AD6" } });
    const api = criarApi(s.fetch, new Memoria());
    await api.entrar("a@x.com", "x");
    await expect(api.criarTalhao(3, { nome: "T", areaHa: 1, classeSolo: "AD9" })).rejects.toThrow("classeSolo deve ser AD1 … AD6");
  });

  it("204 (apagar) não tenta ler JSON", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "DELETE /colheitas/7": { status: 204 } });
    const api = criarApi(s.fetch, new Memoria());
    await api.entrar("a@x.com", "x");
    await expect(api.apagarColheita(7)).resolves.toBeUndefined();
  });

  it("plano manda inicio e ordem na query", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "GET /fazendas/2/plano-colheita": { corpo: {} } });
    const api = criarApi(s.fetch, new Memoria());
    await api.entrar("a@x.com", "x");
    await api.plano(2, "2026-02-01", [3, 1]);
    expect(s.chamadas.at(-1)?.rota).toBe("/fazendas/2/plano-colheita?inicio=2026-02-01&ordem=3,1");
  });

  it("colheita manda a data no corpo", async () => {
    const s = servidor({ "POST /auth/login": { corpo: tokens() }, "POST /talhoes/5/colheitas": { status: 201, corpo: { id: 1 } } });
    const api = criarApi(s.fetch, new Memoria());
    await api.entrar("a@x.com", "x");
    await api.registrarColheita(5, "2026-02-15");
    expect(s.chamadas.at(-1)?.corpo).toEqual({ data: "2026-02-15" });
  });
});

import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it } from "vitest";
import { criarApi, type Colheita, type Fazenda, type PlanoFazenda } from "../api";
import { App } from "../App";
import { ApiProvider } from "../contexto";
import { Memoria, servidor, tokens } from "./servidor";

const fazenda: Fazenda = {
  id: 1, nome: "Boa Vista", lat: -19.03, lon: -47.92, diasUteisPct: 70, riscoMax: 0.2, areaHa: 150, haDiaColhedoras: 60,
  talhoes: [
    { id: 10, fazendaId: 1, nome: "Talhão 1", areaHa: 100, classeSolo: "AD3", cadMm: 30, lat: null, lon: null, poligono: null },
    { id: 11, fazendaId: 1, nome: "Talhão 2", areaHa: 50, classeSolo: "AD5", cadMm: 61, lat: null, lon: null, poligono: null },
  ],
  colhedoras: [{ id: 5, fazendaId: 1, nome: "JD S780", haDia: 60 }],
};

const colheita: Colheita = {
  id: 1, talhaoId: 10, data: "2026-02-15", cultura: "sorgo", decendio: "11/02 a 20/02",
  risco: { milho: 0.45, sorgo: 0.1 }, nivel: { milho: "inapto", sorgo: "20%" }, cad: 30, riscoMax: 0.2, fonteChuva: "imerg", criadoEm: "",
};

function montar(rotas: Parameters<typeof servidor>[0], logado = true) {
  const s = servidor({ "POST /auth/renovar": { corpo: tokens() }, ...rotas });
  const mem = new Memoria();
  if (logado) mem.setItem("farm.refresh", "rt");
  const api = criarApi(s.fetch, mem);
  render(<ApiProvider value={api}><App /></ApiProvider>);
  return { s, api, mem };
}

beforeEach(() => { window.location.hash = ""; });

describe("entrar", () => {
  it("sem sessão mostra o login; senha errada mostra o erro", async () => {
    montar({ "POST /auth/login": { status: 401 } }, false);
    await userEvent.type(screen.getByLabelText("E-mail"), "ana@x.com");
    await userEvent.type(screen.getByLabelText("Senha"), "errada-123");
    await userEvent.click(screen.getByRole("button", { name: "Entrar" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("E-mail ou senha errados.");
  });

  it("criar conta com senha curta nem chama a API", async () => {
    const { s } = montar({}, false);
    await userEvent.click(screen.getByRole("button", { name: /criar/i }));
    await userEvent.type(screen.getByLabelText("E-mail"), "ana@x.com");
    await userEvent.type(screen.getByLabelText("Senha"), "123");
    await userEvent.click(screen.getByRole("button", { name: "Criar conta" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("8 caracteres");
    expect(s.chamadas.some((c) => c.rota === "/auth/registrar")).toBe(false);
  });

  it("login certo leva para as fazendas", async () => {
    montar({ "POST /auth/login": { corpo: tokens("Ana") }, "GET /fazendas": { corpo: [fazenda] } }, false);
    await userEvent.type(screen.getByLabelText("E-mail"), "ana@x.com");
    await userEvent.type(screen.getByLabelText("Senha"), "senha-boa-1");
    await userEvent.click(screen.getByRole("button", { name: "Entrar" }));
    expect(await screen.findByText("Boa Vista")).toBeInTheDocument();
    expect(screen.getByText(/150 ha · 2 talhões · 1 colhedora$/)).toBeInTheDocument();
  });
});

describe("fazendas", () => {
  it("com sessão guardada abre direto na lista", async () => {
    montar({ "GET /fazendas": { corpo: [] } });
    expect(await screen.findByText("Nenhuma fazenda ainda.")).toBeInTheDocument();
  });

  it("criar fazenda aceita vírgula na coordenada e abre a fazenda", async () => {
    const { s } = montar({ "GET /fazendas": { corpo: [] }, "POST /fazendas": { status: 201, corpo: { ...fazenda, id: 9 } } });
    await userEvent.click(await screen.findByRole("button", { name: "+ Nova fazenda" }));
    await userEvent.type(screen.getByLabelText("Nome"), "Nova");
    await userEvent.type(screen.getByLabelText("Latitude"), "-19,03");
    await userEvent.type(screen.getByLabelText("Longitude"), "-47,92");
    await userEvent.click(screen.getByRole("button", { name: "Criar" }));
    await waitFor(() => expect(window.location.hash).toBe("#/fazenda/9"));
    expect(s.chamadas.find((c) => c.rota === "/fazendas" && c.metodo === "POST")?.corpo).toEqual({ nome: "Nova", lat: -19.03, lon: -47.92 });
  });
});

describe("talhão", () => {
  it("'Colhi hoje' mostra a cultura indicada e os riscos", async () => {
    window.location.hash = "#/talhao/10";
    const { s } = montar({
      "GET /talhoes/10": { corpo: fazenda.talhoes[0] },
      "GET /talhoes/10/colheitas": { corpo: [] },
      "POST /talhoes/10/colheitas": { status: 201, corpo: colheita },
    });
    await userEvent.click(await screen.findByRole("button", { name: "Colhi hoje" }));
    expect(await screen.findByText("O milho já passa do risco nessa data, mas o sorgo ainda cabe.")).toBeInTheDocument();
    expect(screen.getByText(/falha em 45% dos anos/)).toBeInTheDocument();
    expect(s.chamadas.find((c) => c.metodo === "POST" && c.rota === "/talhoes/10/colheitas")?.corpo).toHaveProperty("data");
  });

  it("erro da API na colheita aparece na tela", async () => {
    window.location.hash = "#/talhao/10";
    montar({
      "GET /talhoes/10": { corpo: fazenda.talhoes[0] },
      "GET /talhoes/10/colheitas": { corpo: [] },
      "POST /talhoes/10/colheitas": { status: 400, corpo: "talhão sem coordenada: informe lat/lon ou polígono (ou lat/lon na fazenda)" },
    });
    await userEvent.click(await screen.findByRole("button", { name: "Colhi hoje" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("talhão sem coordenada");
  });
});

describe("fazenda", () => {
  it("talhões listados com área e solo", async () => {
    window.location.hash = "#/fazenda/1";
    montar({ "GET /fazendas/1": { corpo: fazenda } });
    expect(await screen.findByText("Talhão 1")).toBeInTheDocument();
    expect(screen.getByText("100 ha · solo AD3")).toBeInTheDocument();
  });

  it("novo talhão sem área não chama a API", async () => {
    window.location.hash = "#/fazenda/1";
    const { s } = montar({ "GET /fazendas/1": { corpo: fazenda } });
    await userEvent.click(await screen.findByRole("button", { name: "+ Novo talhão" }));
    await userEvent.type(screen.getByLabelText("Nome"), "T3");
    await userEvent.click(screen.getByRole("button", { name: "Salvar talhão" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("área");
    expect(s.chamadas.some((c) => c.rota.endsWith("/talhoes") && c.metodo === "POST")).toBe(false);
  });

  it("novo talhão manda área com vírgula e o solo escolhido", async () => {
    window.location.hash = "#/fazenda/1";
    const { s } = montar({ "GET /fazendas/1": { corpo: fazenda }, "POST /fazendas/1/talhoes": { status: 201, corpo: {} } });
    await userEvent.click(await screen.findByRole("button", { name: "+ Novo talhão" }));
    await userEvent.type(screen.getByLabelText("Nome"), "T3");
    await userEvent.type(screen.getByLabelText("Área (ha)"), "12,5");
    await userEvent.click(screen.getByLabelText(/AD6/));
    await userEvent.click(screen.getByRole("button", { name: "Salvar talhão" }));
    await waitFor(() => expect(s.chamadas.some((c) => c.rota === "/fazendas/1/talhoes")).toBe(true));
    expect(s.chamadas.find((c) => c.rota === "/fazendas/1/talhoes")?.corpo).toMatchObject({ nome: "T3", areaHa: 12.5, classeSolo: "AD6" });
  });

  it("plano mostra totais por cultura e os trechos de cada talhão", async () => {
    window.location.hash = "#/fazenda/1/plano";
    const plano: PlanoFazenda = {
      fazendaId: 1, inicio: "2026-02-01", fim: "2026-02-04", capacidadeHaDia: 42,
      talhoes: [{ talhaoId: 10, nome: "Talhão 1", classeSolo: "AD3", areaHa: 100, de: "2026-02-01", ate: "2026-02-03", dias: 3,
        trechos: [{ cultura: "milho", de: "2026-02-01", ate: "2026-02-02", dias: 2, ha: 84 }, { cultura: "sorgo", de: "2026-02-03", ate: "2026-02-03", dias: 1, ha: 16 }] }],
      totais: [{ cultura: "milho", ha: 84 }, { cultura: "sorgo", ha: 16 }],
    };
    const { s } = montar({ "GET /fazendas/1": { corpo: fazenda }, "GET /fazendas/1/plano-colheita": { corpo: plano } });
    await userEvent.click(await screen.findByRole("button", { name: "Ver plano" }));
    expect(await screen.findByText("84%", { exact: false })).toBeInTheDocument();
    expect(screen.getByText(/01\/02 a 02\/02/)).toBeInTheDocument();
    expect(s.chamadas.find((c) => c.rota.startsWith("/fazendas/1/plano-colheita"))?.rota).toMatch(/inicio=\d{4}-\d{2}-\d{2}/);
  });

  it("plano sem colhedora avisa e não deixa calcular", async () => {
    window.location.hash = "#/fazenda/1/plano";
    montar({ "GET /fazendas/1": { corpo: { ...fazenda, colhedoras: [] } } });
    expect(await screen.findByText("Cadastre ao menos uma colhedora.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ver plano" })).toBeDisabled();
  });
});

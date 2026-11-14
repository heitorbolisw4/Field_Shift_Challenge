import { describe, expect, it } from "vitest";
import { lerRota, link } from "../rota";
import { dataBr, diaMes, hojeIso, lerNumero } from "../util";

describe("lerNumero (teclado do celular)", () => {
  it.each([
    ["10", 10], ["10,5", 10.5], ["10.5", 10.5], [" 1.234,5 ", 1234.5], ["-19,03", -19.03], ["0", 0],
  ])("%s -> %s", (t, n) => expect(lerNumero(t)).toBe(n));

  it.each(["", "  ", "abc", "1,2,3", "1e3", "NaN", "Infinity", "10,", ",5", "1.2.3"])("%s -> null", (t) =>
    expect(lerNumero(t)).toBeNull());
});

describe("datas", () => {
  it("formata em pt-BR", () => {
    expect(dataBr("2026-02-05")).toBe("05/02/2026");
    expect(diaMes("2026-02-05")).toBe("05/02");
  });

  it("hoje usa o fuso do aparelho, não UTC", () => {
    expect(hojeIso(new Date(2026, 0, 5, 23, 30))).toBe("2026-01-05");
  });
});

describe("rotas por hash", () => {
  it.each([
    ["", { tela: "fazendas" }],
    ["#/", { tela: "fazendas" }],
    ["#/fazenda/3", { tela: "fazenda", id: 3, aba: "talhoes" }],
    ["#/fazenda/3/plano", { tela: "fazenda", id: 3, aba: "plano" }],
    ["#/talhao/12", { tela: "talhao", id: 12 }],
    ["#/talhao/abc", { tela: "fazendas" }],
    ["#/fazenda/-1", { tela: "fazendas" }],
    ["#/qualquer", { tela: "fazendas" }],
  ])("%s", (h, r) => expect(lerRota(h)).toEqual(r));

  it("link e lerRota se entendem", () => {
    expect(lerRota(link.fazenda(4, "colhedoras"))).toEqual({ tela: "fazenda", id: 4, aba: "colhedoras" });
    expect(lerRota(link.talhao(9))).toEqual({ tela: "talhao", id: 9 });
  });
});

// Rodada independente: lerNumero (entrada pt-BR do celular), commit ee683a9.
// it.skip("BUG: ...") = bug encontrado e não corrigido (convenção do projeto: os testes só relatam).
import { describe, expect, it } from "vitest";
import { lerNumero } from "../util";

describe("lerNumero: o que o produtor digita", () => {
  it.each([
    ["1,5", 1.5], ["0,5", 0.5], ["100", 100], ["100,25", 100.25], ["-47,92", -47.92], ["-19.034567", -19.034567],
    ["1.234,56", 1234.56], ["12.345.678,9", 12345678.9], ["007", 7], ["0,000001", 0.000001],
  ])("%s -> %s", (t, n) => expect(lerNumero(t)).toBe(n));

  it.each([
    [" 10,5 ", 10.5], // espaço não separável (colar do Excel/WhatsApp)
    ["\t60\n", 60],
    ["1 234,5", 1234.5], // milhar com espaço
    ["1 234,5", 1234.5], // espaço fino (toLocaleString de alguns idiomas)
  ])("espaços: %j -> %s", (t, n) => expect(lerNumero(t)).toBe(n));

  it("o que ha() escreve, lerNumero lê de volta", async () => {
    const { ha } = await import("../util");
    for (const n of [0.5, 10.25, 1234.5, 98765.43, -19.03]) expect(lerNumero(ha(n))).toBe(n);
  });

  it("String() de lat/lon do GPS (o BotaoLocal preenche assim) é lido igual", () => {
    for (const n of [-19.034567, -47.92, 0.000001, -0.5, 179.999999]) expect(lerNumero(String(n))).toBe(n);
  });

  it("número enorme vira Infinity e é recusado", () => {
    expect(lerNumero("9".repeat(400))).toBeNull();
    expect(lerNumero("1" + "0".repeat(309) + ",5")).toBeNull();
  });

  it.each(["+5", "5-", "--5", "-", ",", ".", "5,,0", "1.000,", "1,5e2", "0x10", "١٢", "５", "10 ha", "R$ 10", "½", "1/2", "−5"])(
    "%j -> null", (t) => expect(lerNumero(t)).toBeNull());

  it("-0 é lido como zero (vai como 0 no JSON)", () => {
    expect(JSON.stringify(lerNumero("-0"))).toBe("0");
  });
});

describe("lerNumero: ambiguidades do ponto", () => {
  it.skip("BUG: '1.500' (mil e quinhentos, como se escreve em pt-BR) vira 1,5", () => {
    // Sem vírgula, o ponto é tratado como decimal. Uma fazenda de "1.500" ha vira 1,5 ha sem aviso nenhum.
    expect(lerNumero("1.500")).toBe(1500);
  });

  it.skip("BUG: '1.000.000' é recusado, mas '1.000' é aceito (como 1): o mesmo jeito de escrever dá resultados diferentes", () => {
    expect(lerNumero("1.000.000")).toBe(1000000);
    expect(lerNumero("1.000")).toBe(1000);
  });

  it.skip("BUG: pontos fora do lugar com vírgula são aceitos em silêncio ('1.2,5' -> 12,5; '10,5.3' -> 10,53)", () => {
    // Com vírgula, todos os pontos são apagados sem conferir se estão de 3 em 3 dígitos.
    expect(lerNumero("1.2,5")).toBeNull();
    expect(lerNumero("10,5.3")).toBeNull();
    expect(lerNumero("1.23.4,5")).toBeNull();
  });

  it("'10.5' (ponto decimal, teclado em inglês) continua 10,5", () => {
    expect(lerNumero("10.5")).toBe(10.5);
  });

  it("'1,234' é 1,234 (vírgula é sempre decimal)", () => {
    expect(lerNumero("1,234")).toBe(1.234);
  });
});

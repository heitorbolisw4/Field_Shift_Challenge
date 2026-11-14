// E2E (ee683a9): cadastro, fazenda, talhão, colhedora, "colhi hoje" e plano, contra a Farm.Api real.
// test.fixme("BUG: ...") = bug encontrado e não corrigido (convenção do projeto: os testes só relatam).
import { expect, test, type Page } from "@playwright/test";

// Uma aba só para o fluxo todo (a sessão fica na memória, como no celular). Trocar de tela = trocar o hash, sem recarregar,
// para não gastar o limite de 10 pedidos/minuto em /auth com um /auth/renovar a cada teste.

// um usuário para o arquivo todo: /auth aceita só 10 pedidos por minuto por IP
const email = `e2e-${Date.now()}@teste.com`;
const senha = "senha-boa-123";

async function cadastrar(page: Page) {
  await page.goto("/");
  await page.getByRole("button", { name: /não tenho conta/i }).click();
  await page.getByLabel("Seu nome").fill("Teste E2E");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(senha);
  await page.getByRole("button", { name: "Criar conta" }).click();
  await expect(page.getByRole("heading", { name: "Minhas fazendas" })).toBeVisible();
}

const irPara = (page: Page, url: string) => page.evaluate((h) => { location.hash = h; }, new URL(url).hash);

test.describe.serial("fluxo do produtor", () => {
  let fazendaUrl = "";
  let talhaoUrl = "";
  let page: Page;
  test.beforeAll(async ({ browser }) => { page = await (await browser.newContext()).newPage(); });
  test.afterAll(async () => { await page.context().close(); });

  test("cadastro → nova fazenda com vírgula na localização", async () => {
    await cadastrar(page);
    await expect(page.getByText("Olá, Teste E2E.")).toBeVisible();
    await expect(page.getByText("Nenhuma fazenda ainda.")).toBeVisible();

    await page.getByRole("button", { name: "+ Nova fazenda" }).click();
    await page.getByLabel("Nome").fill("Fazenda E2E");
    await page.getByLabel("Latitude").fill("-19,03");
    await page.getByLabel("Longitude").fill("-47,92");
    const criada = page.waitForResponse((r) => r.request().method() === "POST" && r.url().endsWith("/fazendas"));
    await page.getByRole("button", { name: "Criar" }).click();
    const r = await criada;
    expect(r.request().postDataJSON()).toEqual({ nome: "Fazenda E2E", lat: -19.03, lon: -47.92 }); // número, não "-19,03"
    expect(await r.json()).toMatchObject({ lat: -19.03, lon: -47.92 });

    await expect(page).toHaveURL(/#\/fazenda\/\d+$/);
    fazendaUrl = page.url();
    await expect(page.getByRole("heading", { name: "Fazenda E2E" })).toBeVisible();
    // sem talhões, o formulário de novo talhão já vem aberto
    await expect(page.getByRole("heading", { name: "Novo talhão" })).toBeVisible();
  });

  test("talhão com área decimal e solo AD5", async () => {
    await expect(page.getByRole("heading", { name: "Novo talhão" })).toBeVisible();
    await page.getByLabel("Nome").fill("Talhão Norte");
    await page.getByLabel("Área (ha)").fill("1.234,5");
    await page.getByText("AD5").click();
    const criado = page.waitForResponse((r) => r.request().method() === "POST" && r.url().includes("/talhoes"));
    await page.getByRole("button", { name: "Salvar talhão" }).click();
    expect((await criado).request().postDataJSON()).toMatchObject({ areaHa: 1234.5, classeSolo: "AD5", lat: null, lon: null });

    const item = page.getByRole("link", { name: /Talhão Norte/ });
    await expect(item).toContainText("1.234,5 ha · solo AD5");
    await item.click();
    await expect(page).toHaveURL(/#\/talhao\/\d+$/);
    talhaoUrl = page.url();
    await expect(page.getByText(/1\.234,5 ha · solo AD5 \(\d+ mm\)/)).toBeVisible();
  });

  test("área inválida não chega na API", async () => {
    await irPara(page, fazendaUrl);
    await page.getByRole("button", { name: "+ Novo talhão" }).click();
    await page.getByLabel("Nome").fill("Ruim");
    await page.getByLabel("Área (ha)").fill("10,5.3x");
    const pedidos: string[] = [];
    page.on("request", (r) => { if (r.method() === "POST") pedidos.push(r.url()); });
    await page.getByRole("button", { name: "Salvar talhão" }).click();
    await expect(page.getByRole("alert")).toHaveText("Informe a área em hectares.");
    expect(pedidos.filter((u) => u.includes("/talhoes"))).toEqual([]);
  });

  test("colhedora com ha/dia decimal; capacidade usa os dias úteis", async () => {
    await irPara(page, fazendaUrl + "/colhedoras");
    await page.getByLabel("Nome").fill("JD S780");
    await page.getByLabel("Hectares por dia de trabalho").fill("55,5");
    await page.getByRole("button", { name: "Adicionar" }).click();
    await expect(page.locator("li", { hasText: "JD S780" })).toContainText("55,5 ha/dia");
    // 55,5 × 70% = 38,85
    await expect(page.locator(".resumo")).toContainText("38,85 ha por dia");

    // apagar pede confirmação; cancelar não apaga
    page.once("dialog", (d) => d.dismiss());
    await page.getByRole("button", { name: "Apagar JD S780" }).click();
    await expect(page.getByText("JD S780")).toBeVisible();
  });

  test("colhi hoje: mostra a cultura indicada e entra no histórico", async () => {
    await irPara(page, talhaoUrl);
    await expect(page.getByText("Nenhuma colheita registrada.")).toBeVisible();
    const botao = page.getByRole("button", { name: "Colhi hoje" });
    await expect(botao).toBeEnabled();
    await botao.click();

    const rec = page.locator(".recomendacao");
    await expect(rec).toBeVisible({ timeout: 90_000 });
    await expect(rec.locator(".cultura")).toHaveText(/^(milho|sorgo|braquiária)$/);
    await expect(rec).toContainText(/Risco aceito: 20%/);
    const hoje = new Date().toLocaleDateString("pt-BR", { timeZone: "America/Sao_Paulo" });
    await expect(rec).toContainText(`colheita de ${hoje}`);
    await expect(page.getByRole("button", { name: `Apagar colheita de ${hoje}` })).toBeVisible();
  });

  test("colheita em outra data muda o texto do botão; data futura é bloqueada pelo campo", async () => {
    await irPara(page, talhaoUrl);
    const campo = page.getByLabel("Data da colheita");
    await campo.fill("2026-02-15");
    await expect(page.getByRole("button", { name: "Colhi em 15/02/2026" })).toBeVisible();
    await page.getByRole("button", { name: "Colhi em 15/02/2026" }).click();
    await expect(page.locator(".recomendacao")).toContainText("colheita de 15/02/2026", { timeout: 90_000 });
    await expect(page.getByRole("button", { name: "Apagar colheita de 15/02/2026" })).toBeVisible();
  });

  test("plano de colheita a partir de uma data", async () => {
    await irPara(page, fazendaUrl + "/plano");
    await page.getByLabel("Início").fill("2026-02-10");
    await page.getByRole("button", { name: "Ver plano" }).click();
    const plano = page.locator(".plano");
    await expect(plano).toBeVisible({ timeout: 90_000 });
    await expect(plano.locator(".resumo")).toContainText("Colheita de 10/02/2026 a");
    await expect(plano.locator(".resumo")).toContainText("38,85 ha/dia");
    await expect(plano.getByRole("link", { name: "Talhão Norte" })).toBeVisible();
    // 1.234,5 ha a 38,85 ha/dia = 32 dias
    await expect(plano).toContainText("(32 dias)");
    // as culturas somam a área do talhão
    const total = await plano.locator(".totais b").allTextContents();
    const soma = total.map((t) => Number(t.replace(" ha", "").replace(/\./g, "").replace(",", "."))).reduce((a, b) => a + b, 0);
    expect(soma).toBeCloseTo(1234.5, 1);
  });

  test("JWT vencido no meio do uso: renova sozinho e a tela carrega", async () => {
    await irPara(page, fazendaUrl);
    let vezes = 0;
    await page.route("**/fazendas", (r) => (vezes++ === 0 ? r.fulfill({ status: 401, body: "" }) : r.continue()));
    const renovou = page.waitForRequest("**/auth/renovar");
    await page.getByLabel("Voltar").click(); // vai para a lista: GET /fazendas leva 401 uma vez
    await renovou;
    await expect(page.getByText("Fazenda E2E")).toBeVisible();
    await expect(page.getByRole("alert")).toHaveCount(0);
    await page.unroute("**/fazendas");
  });

  test("recarregar a página mantém o login (refresh guardado no aparelho)", async () => {
    await page.reload();
    await expect(page.getByText("Fazenda E2E")).toBeVisible();
    await expect(page.getByText("Olá, Teste E2E.")).toBeVisible();
  });

  test("sem rede: mensagem clara e 'Tentar de novo' funciona quando volta", async () => {
    await irPara(page, fazendaUrl);
    await expect(page.getByRole("link", { name: /Talhão Norte/ })).toBeVisible();
    await page.context().setOffline(true);
    await page.getByRole("link", { name: "Colhedoras" }).click(); // só muda o hash: não recarrega
    await page.getByRole("link", { name: "Voltar" }).click();
    await expect(page.getByRole("alert")).toContainText("Sem conexão com o servidor.");
    await expect(page.getByRole("heading", { name: "Minhas fazendas" })).toBeVisible(); // não caiu para o login
    await page.context().setOffline(false);
    await page.getByRole("button", { name: "Tentar de novo" }).click();
    await expect(page.getByText("Fazenda E2E")).toBeVisible();
  });

  test("sair e entrar de novo; refresh velho não serve mais", async () => {
    await irPara(page, "http://x/#/");
    const rtAntes = await page.evaluate(() => localStorage.getItem("farm.refresh"));
    const saiu = page.waitForResponse("**/auth/sair"); // a tela troca antes de a revogação chegar na API
    await page.getByRole("button", { name: "Sair" }).click();
    await expect(page.getByRole("heading", { name: "Entrar" })).toBeVisible();
    expect((await saiu).status()).toBe(204);
    expect(await page.evaluate(() => localStorage.getItem("farm.refresh"))).toBeNull();
    const r = await page.request.post("/auth/renovar", { data: { refreshToken: rtAntes } });
    expect(r.status()).toBe(401);

    await page.getByLabel("E-mail").fill(email.toUpperCase()); // e-mail sem diferença de maiúsculas
    await page.getByLabel("Senha").fill(senha);
    await page.getByRole("button", { name: "Entrar" }).click();
    await expect(page.getByText("Fazenda E2E")).toBeVisible();
  });

  test.fixme("BUG: duas abas abertas ao mesmo tempo derrubam a sessão (as duas renovam com o mesmo refresh)", async () => {
    const context = page.context();
    // O 'um refresh por vez' vale só dentro da aba. Ao abrir duas abas (ou o navegador restaurar as abas),
    // cada uma manda /auth/renovar com o mesmo refresh do localStorage; a API vê o token rotacionado de novo,
    // trata como vazamento e revoga todas as sessões. As duas abas caem para o login.
    const a = await context.newPage();
    await a.goto("/");
    await expect(a.getByText("Fazenda E2E")).toBeVisible();
    const b = await context.newPage();
    // rede lenta (celular): segura cada /auth/renovar até as duas abas terem pedido
    const presos: (() => Promise<void>)[] = [];
    await context.route("**/auth/renovar", async (r) => {
      presos.push(() => r.continue());
      if (presos.length === 2) for (const solta of presos.splice(0)) await solta();
    });
    await Promise.all([a.reload(), b.goto("/")]);
    await context.unroute("**/auth/renovar");
    await expect(a.getByText("Fazenda E2E")).toBeVisible();
    await expect(b.getByText("Fazenda E2E")).toBeVisible();
  });

  test.fixme("BUG: 'Sair' em outra aba deixa esta aba presa em 'Entre para continuar.', e o login seguinte não renova mais", async () => {
    // Aba B sai: apaga o refresh do localStorage. Na aba A, o próximo 401 chama renovar() sem refresh guardado:
    // devolve false sem limpar() (o usuário continua na tela, só com o erro) e, como não houve await,
    // o finally roda antes do `renovando ??=` e a promessa resolvida fica presa. Mesmo entrando de novo pela aba A,
    // o JWT seguinte que vencer não é mais renovado até recarregar a página.
    const a = page;
    await irPara(a, "http://x/#/");
    await expect(a.getByText("Fazenda E2E")).toBeVisible();
    const b = await a.context().newPage();
    await b.goto("/");
    await expect(b.getByText("Fazenda E2E")).toBeVisible();
    await b.getByRole("button", { name: "Sair" }).click();
    await b.close();

    let vence = true;
    await a.route("**/fazendas", (r) => (vence ? ((vence = false), r.fulfill({ status: 401, body: "" })) : r.continue()));
    await a.getByRole("link", { name: /Fazenda E2E/ }).click();
    await a.getByLabel("Voltar").click();
    await expect(a.getByRole("heading", { name: "Entrar" })).toBeVisible(); // hoje: fica em "Minhas fazendas" com o alerta

    await a.getByLabel("E-mail").fill(email);
    await a.getByLabel("Senha").fill(senha);
    await a.getByRole("button", { name: "Entrar" }).click();
    await expect(a.getByText("Fazenda E2E")).toBeVisible();
    vence = true; // 20 min depois
    const renovou = a.waitForRequest("**/auth/renovar", { timeout: 5_000 });
    await a.getByRole("link", { name: /Fazenda E2E/ }).click();
    await a.getByLabel("Voltar").click();
    await renovou;
    await expect(a.getByRole("alert")).toHaveCount(0);
    await a.unroute("**/fazendas");
  });

  test("apagar talhão volta para a fazenda", async () => {
    await irPara(page, talhaoUrl);
    page.once("dialog", (d) => d.accept());
    await page.getByRole("button", { name: "Apagar talhão" }).click();
    await expect(page).toHaveURL(fazendaUrl);
    await expect(page.getByRole("heading", { name: "Novo talhão" })).toBeVisible();
  });
});

// E2E: Playwright contra a Farm.Api de verdade (banco SQLite temporário) e o Vite.
// Rodar: npm run e2e   (sobe a API na porta 5197 e o Vite na 5174; não mexe no data/farm.db nem nas portas de dev)
import { defineConfig } from "@playwright/test";
import { tmpdir } from "node:os";
import { join } from "node:path";

const API = 5197;
const WEB = 5174;
const banco = join(tmpdir(), `farm-e2e-${Date.now()}.db`);

export default defineConfig({
  testDir: "./e2e",
  outputDir: join(tmpdir(), "farm-e2e-resultados"), // fora do repositório
  testMatch: /.*\.e2e\.ts$/, // não confunde com os testes do Vitest
  workers: 1, // /auth tem limite de 10 por minuto por IP: um teste por vez
  timeout: 120_000, // "colhi" e o plano buscam o clima da NASA
  expect: { timeout: 15_000 },
  reporter: "list",
  use: { baseURL: `http://localhost:${WEB}`, locale: "pt-BR", timezoneId: "America/Sao_Paulo", trace: "off" },
  webServer: [
    {
      command: "dotnet run --project ../src/Farm.Api --no-launch-profile",
      url: `http://localhost:${API}/solos`,
      env: { ASPNETCORE_URLS: `http://localhost:${API}`, ASPNETCORE_ENVIRONMENT: "Development", ConnectionStrings__Farm: `Data Source=${banco}` },
      timeout: 180_000,
      reuseExistingServer: false,
    },
    {
      command: `npx vite --port ${WEB} --strictPort`,
      url: `http://localhost:${WEB}`,
      env: { FARM_API: `http://localhost:${API}` },
      reuseExistingServer: false,
    },
  ],
});

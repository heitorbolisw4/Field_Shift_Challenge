/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// em dev, o Vite repassa as rotas da API para o Farm.Api (dotnet run: http://localhost:5196)
const api = process.env.FARM_API ?? "http://localhost:5196";
const rotas = ["/auth", "/fazendas", "/talhoes", "/colhedoras", "/colheitas", "/solos", "/plano-colheita", "/recomendacao", "/openapi"];

export default defineConfig({
  plugins: [react()],
  server: { proxy: Object.fromEntries(rotas.map((r) => [r, api])) },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/testes/setup.ts"],
  },
});

# Front (React + Vite)

App web para o produtor, pensado para o celular: login, fazendas, talhões, colhedoras,
"colhi este talhão" com a cultura indicada e o plano de colheita da fazenda.

## Rodar em desenvolvimento

1. API: `dotnet run --project src/Farm.Api` (http://localhost:5196)
2. Front: `cd front`, `npm install` (1ª vez), `npm run dev` → http://localhost:5173

O Vite repassa `/auth`, `/fazendas`, `/talhoes`... para a API (ver `vite.config.ts`; outra porta: variável `FARM_API`).

## Testes e build

- `npm test`: Vitest + Testing Library (cliente da API, renovação do token, telas).
- `npm run e2e`: Playwright contra a Farm.Api de verdade (sobe a API na porta 5197 com um SQLite temporário e o Vite na 5174;
  não toca no `data/farm.db`). Usa o Chromium já instalado do Playwright 1.57. "Colhi" e o plano buscam o clima da NASA (precisa de internet).
- Testes marcados `it.skip("BUG: ...")` / `test.fixme("BUG: ...")` são bugs encontrados e ainda não corrigidos.
- `npm run build`: confere os tipos e gera `front/dist` (arquivos estáticos).

## Hospedar

`front/dist` é estático: Nginx serve os arquivos e repassa as rotas da API para o Farm.Api
(mesmo domínio, sem CORS). As telas usam rota por `#`, então não precisa de regra de reescrita.

## Login

- JWT de acesso (20 min) só na memória; refresh token (30 dias) no `localStorage` para manter o login.
- Um refresh por vez: chamadas em paralelo esperam o mesmo (a API derruba a sessão se o mesmo refresh for usado duas vezes).

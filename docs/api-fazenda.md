# API "Minha fazenda"

Documento completo: `/openapi/v1.json`. Tela para testar: `/docs`.

## Banco

- SQLite em `data/farm.db` (fora do git). Tabelas criadas/atualizadas sozinhas ao subir a API.
- Trocar o caminho: `ConnectionStrings:Farm` no appsettings.
- Postgres (VPS): trocar `UseSqlite` por `UseNpgsql` e gerar migração nova (`dotnet ef migrations add ... --project src/Farm.Api --output-dir Dados/Migracoes`).

## Usuário (provisório)

Ainda não tem login. O dono vem do header `X-Usuario` (sem header = `"local"`).
**Não é seguro.** Antes de expor na internet, entra JWT e o dono passa a vir do claim `sub`.

## Endpoints

| Método | Rota | O que faz |
|---|---|---|
| GET | `/solos` | classes AD1–AD6 e a CAD (mm) |
| GET/POST | `/fazendas` | listar / criar |
| GET/PUT/DELETE | `/fazendas/{id}` | uma fazenda (com talhões e colhedoras) |
| POST | `/fazendas/{id}/talhoes` | novo talhão |
| GET/PUT/DELETE | `/talhoes/{id}` | um talhão |
| POST | `/fazendas/{id}/colhedoras` | nova colhedora (`haDia`) |
| PUT/DELETE | `/colhedoras/{id}` | uma colhedora |
| POST | `/talhoes/{id}/colheitas` | "colhi este talhão no dia D" → cultura indicada (fica gravada) |
| GET | `/talhoes/{id}/colheitas` | histórico do talhão |
| DELETE | `/colheitas/{id}` | apagar registro |
| GET | `/fazendas/{id}/plano-colheita?inicio=AAAA-MM-DD&ordem=3,1,2` | talhões colhidos em sequência; ha por cultura |
| GET | `/plano-colheita?lat&lon&inicio&area` | mesma conta sem cadastro (o que o front fazia em JS) |

Rotas antigas (`/clima`, `/deficit`, `/recomendacao`) continuam iguais.

## Talhão

```json
{ "nome": "Talhão 1", "classeSolo": "AD3",
  "poligono": { "type": "Polygon", "coordinates": [[[-47.93,-19.04],[-47.92,-19.04],[-47.92,-19.03],[-47.93,-19.03],[-47.93,-19.04]]] } }
```

- Com polígono: lat/lon = centroide; `areaHa` sai do polígono se não vier.
- Sem polígono: `areaHa` obrigatório; `lat`/`lon` opcionais (sem eles, usa os da fazenda).

## Colheita

```json
POST /talhoes/1/colheitas  { "data": "2026-02-15" }
→ { "cultura": "braquiária", "decendio": "11/02 a 20/02", "risco": { "milho": 0.95, "sorgo": 0.38 }, ... }
```

Usa a CAD da classe do talhão e o `riscoMax` da fazenda (padrão 0,2).

## Plano da fazenda

- Capacidade = soma de `haDia` das colhedoras × `diasUteisPct` da fazenda (padrão 70%).
- Um talhão por vez, na `ordem` pedida; cada um com o próprio solo e coordenada.
- Simplificação: o talhão seguinte começa no dia seguinte (sobra de capacidade do último dia não passa para ele).

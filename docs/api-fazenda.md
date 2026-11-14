# API "Minha fazenda"

Documento completo: `/openapi/v1.json`. Tela para testar: `/docs`.

## Banco

- SQLite em `data/farm.db` (fora do git). Tabelas criadas/atualizadas sozinhas ao subir a API.
- Trocar o caminho: `ConnectionStrings:Farm` no appsettings.
- Postgres (VPS): trocar `UseSqlite` por `UseNpgsql` e gerar migração nova (`dotnet ef migrations add ... --project src/Farm.Api --output-dir Dados/Migracoes`).

## Login (JWT)

1. `POST /auth/registrar {email, senha, nome?}` ou `POST /auth/login {email, senha}` → `accessToken` + `refreshToken`.
2. Mandar `Authorization: Bearer <accessToken>` nas rotas da fazenda (sem token = 401).
3. O `accessToken` vale 20 min. Antes de vencer: `POST /auth/renovar {refreshToken}` → par novo. O refresh antigo deixa de valer.
4. `POST /auth/sair {refreshToken}` encerra a sessão. `GET /auth/eu` mostra a conta.

- Senha: mínimo 8 caracteres, guardada com hash (PBKDF2 do ASP.NET Identity).
- Refresh token: vale 30 dias, guardado só o hash. Se um refresh já usado aparecer de novo, todas as sessões do usuário caem (sinal de vazamento).
- `/auth/*`: 10 tentativas por minuto por IP (429 depois disso).
- Cada usuário só vê as próprias fazendas (outro usuário recebe 404).
- `/clima`, `/deficit`, `/recomendacao`, `/plano-colheita` e `/solos` continuam abertos.

### Chave do JWT

- Desenvolvimento: `Jwt:Chave` em `appsettings.Development.json` (só para dev).
- **Produção (VPS): variável de ambiente `Jwt__Chave`** com 32+ caracteres aleatórios. Sem ela a API não sobe.
- Opcionais: `Jwt__MinutosAcesso`, `Jwt__DiasRefresh`, `Jwt__Emissor`, `Jwt__Publico`.
- Na VPS atrás de Nginx: ligar ForwardedHeaders, senão o limite por IP enxerga só o IP do proxy.

## Endpoints

| Método | Rota | O que faz |
|---|---|---|
| POST | `/auth/registrar`, `/auth/login`, `/auth/renovar`, `/auth/sair` | conta e tokens |
| GET | `/auth/eu` | conta logada |
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

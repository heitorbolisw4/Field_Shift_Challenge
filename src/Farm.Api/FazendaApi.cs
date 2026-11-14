using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using static Fontes;

// "Minha fazenda": cadastro de fazenda, talhões e colhedoras, e o registro "colhi o talhão X no dia D"
static class FazendaApi
{
    // ---- entrada e saída (o que o app manda e recebe) ----
    public record FazendaIn(string Nome, double? Lat = null, double? Lon = null, double? DiasUteisPct = null, double? RiscoMax = null);
    public record FazendaOut(int Id, string Nome, double? Lat, double? Lon, double DiasUteisPct, double RiscoMax,
                             double AreaHa, double HaDiaColhedoras, List<TalhaoOut> Talhoes, List<ColhedoraOut> Colhedoras);

    // poligono: GeoJSON Polygon (ou Feature com Polygon), lon/lat. Com polígono, lat/lon viram o centroide
    // e, se areaHa não vier, a área sai do polígono.
    public record TalhaoIn(string Nome, double? AreaHa = null, string? ClasseSolo = null, double? Lat = null, double? Lon = null,
                           JsonElement? Poligono = null);
    public record TalhaoOut(int Id, int FazendaId, string Nome, double AreaHa, string ClasseSolo, double CadMm,
                            double? Lat, double? Lon, JsonElement? Poligono);

    public record ColhedoraIn(string Nome, double HaDia);
    public record ColhedoraOut(int Id, int FazendaId, string Nome, double HaDia);

    public record ColheitaIn(DateOnly Data, string FonteChuva = "auto", string AguaInicial = "auto");
    public record ColheitaOut(int Id, int TalhaoId, DateOnly Data, string Cultura, string Decendio,
                              Dictionary<string, double> Risco, Dictionary<string, string> Nivel,
                              double Cad, double RiscoMax, string FonteChuva, DateTimeOffset CriadoEm);

    public record TalhaoPlano(int TalhaoId, string Nome, string ClasseSolo, double AreaHa, DateOnly De, DateOnly Ate, int Dias,
                              List<ColheitaApi.TrechoOut> Trechos);
    public record TotalCultura(string Cultura, double Ha);
    public record PlanoFazendaOut(int FazendaId, DateOnly Inicio, DateOnly Fim, double CapacidadeHaDia,
                                  List<TalhaoPlano> Talhoes, List<TotalCultura> Totais);

    static TalhaoOut Saida(Talhao t) => new(t.Id, t.FazendaId, t.Nome, t.AreaHa, t.ClasseSolo, Solos.Cad[t.ClasseSolo], t.Lat, t.Lon,
        t.PoligonoGeoJson is null ? null : JsonDocument.Parse(t.PoligonoGeoJson).RootElement.Clone());
    static ColhedoraOut Saida(Colhedora c) => new(c.Id, c.FazendaId, c.Nome, c.HaDia);
    static FazendaOut Saida(Fazenda f) => new(f.Id, f.Nome, f.Lat, f.Lon, f.DiasUteisPct, f.RiscoMax,
        f.Talhoes.Sum(t => t.AreaHa), f.Colhedoras.Sum(c => c.HaDia),
        f.Talhoes.OrderBy(t => t.Id).Select(Saida).ToList(), f.Colhedoras.OrderBy(c => c.Id).Select(Saida).ToList());
    static ColheitaOut Saida(ColheitaEvento e)
    {
        var risco = new Dictionary<string, double>();
        if (e.RiscoMilho is { } m) risco["milho"] = m;
        if (e.RiscoSorgo is { } s) risco["sorgo"] = s;
        return new(e.Id, e.TalhaoId, e.Data, e.Cultura, e.Decendio, risco,
                   risco.ToDictionary(kv => kv.Key, kv => Modelo.Nivel(kv.Value)), e.Cad, e.RiscoMax, e.FonteChuva, e.CriadoEm);
    }

    // PROVISÓRIO, sem login: o dono vem do claim "sub" (quando houver JWT), senão do header X-Usuario, senão "local".
    // O header NÃO é seguro: qualquer um se passa por outro. Trocar por autenticação antes de expor na internet.
    public static string Dono(HttpContext ctx) =>
        ctx.User.FindFirst("sub")?.Value
        ?? (ctx.Request.Headers["X-Usuario"].FirstOrDefault() is { Length: > 0 and <= 128 } u ? u : "local");

    // ---- validação ----
    static string? Validar(FazendaIn f) =>
        string.IsNullOrWhiteSpace(f.Nome) ? "nome é obrigatório"
        : (f.Lat is null) != (f.Lon is null) ? "informe lat e lon juntos"
        : f.Lat is < -90 or > 90 || f.Lon is < -180 or > 180 ? "lat/lon fora do intervalo"
        : f.DiasUteisPct is <= 0 or > 100 ? "diasUteisPct deve ficar entre 0 e 100"
        : f.RiscoMax is < 0 or > 1 ? "riscoMax deve ficar entre 0 e 1 (0,2 = 20%)"
        : null;

    // GeoJSON -> anel externo (lon, lat). Aceita Polygon ou Feature com Polygon.
    static List<(double Lon, double Lat)>? Anel(JsonElement g, out JsonElement geometria)
    {
        geometria = g;
        try
        {
            if (g.TryGetProperty("type", out var tipo) && tipo.GetString() == "Feature") geometria = g = g.GetProperty("geometry");
            if (g.GetProperty("type").GetString() != "Polygon") return null;
            var anel = g.GetProperty("coordinates")[0].EnumerateArray()
                .Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
            return anel.Count >= 4 && anel.All(p => p.Item1 is >= -180 and <= 180 && p.Item2 is >= -90 and <= 90) ? anel : null;
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException) { return null; }
    }

    // preenche o talhão a partir da entrada; devolve a mensagem de erro, se houver
    static string? Aplicar(TalhaoIn i, Talhao t)
    {
        if (string.IsNullOrWhiteSpace(i.Nome)) return "nome é obrigatório";
        var classe = (i.ClasseSolo ?? Solos.Padrao).ToUpperInvariant();
        if (!Solos.Valida(classe)) return "classeSolo deve ser AD1 … AD6";
        if ((i.Lat is null) != (i.Lon is null)) return "informe lat e lon juntos";
        if (i.Lat is < -90 or > 90 || i.Lon is < -180 or > 180) return "lat/lon fora do intervalo";

        double? lat = i.Lat, lon = i.Lon, area = i.AreaHa;
        string? geo = null;
        if (i.Poligono is { ValueKind: not JsonValueKind.Null } pol)
        {
            if (Anel(pol, out var geometria) is not { } anel) return "poligono deve ser GeoJSON Polygon (ou Feature) com ao menos 4 pontos lon/lat";
            var (cx, cy) = Geo.Centroide(anel);
            (lon, lat) = (Math.Round(cx, 6), Math.Round(cy, 6)); // ~10 cm
            area ??= Math.Round(Geo.AreaHa(anel), 2);
            geo = geometria.GetRawText();
        }
        if (area is not > 0) return "areaHa deve ser maior que zero (ou mande o polígono)";

        t.Nome = i.Nome.Trim();
        t.AreaHa = area.Value;
        t.ClasseSolo = classe;
        t.Lat = lat; t.Lon = lon;
        t.PoligonoGeoJson = geo;
        return null;
    }

    static IQueryable<Fazenda> DoDono(FarmDb db, HttpContext ctx) { var d = Dono(ctx); return db.Fazendas.Where(f => f.DonoId == d); }
    static IQueryable<Talhao> TalhoesDoDono(FarmDb db, HttpContext ctx) { var d = Dono(ctx); return db.Talhoes.Where(t => t.Fazenda.DonoId == d); }

    public static void MapFazenda(this WebApplication app)
    {
        var fz = app.MapGroup("/fazendas").WithTags("Fazenda");

        fz.MapGet("/", async (FarmDb db, HttpContext ctx) =>
            (await DoDono(db, ctx).Include(f => f.Talhoes).Include(f => f.Colhedoras).OrderBy(f => f.Id).ToListAsync())
                .Select(Saida).ToList())
          .WithSummary("Fazendas do usuário, com talhões e colhedoras");

        fz.MapGet("/{id:int}", async Task<Results<Ok<FazendaOut>, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
            await DoDono(db, ctx).Include(f => f.Talhoes).Include(f => f.Colhedoras).FirstOrDefaultAsync(f => f.Id == id) is { } f
                ? TypedResults.Ok(Saida(f)) : TypedResults.NotFound());

        fz.MapPost("/", async Task<Results<Created<FazendaOut>, BadRequest<string>>> (FazendaIn i, FarmDb db, HttpContext ctx) =>
        {
            if (Validar(i) is { } erro) return TypedResults.BadRequest(erro);
            var f = new Fazenda { DonoId = Dono(ctx), Nome = i.Nome.Trim(), Lat = i.Lat, Lon = i.Lon,
                                  DiasUteisPct = i.DiasUteisPct ?? 70, RiscoMax = i.RiscoMax ?? 0.2 };
            db.Add(f);
            await db.SaveChangesAsync();
            return TypedResults.Created($"/fazendas/{f.Id}", Saida(f));
        }).WithSummary("Cria uma fazenda (diasUteisPct padrão 70, riscoMax padrão 0,2)");

        fz.MapPut("/{id:int}", async Task<Results<Ok<FazendaOut>, NotFound, BadRequest<string>>> (int id, FazendaIn i, FarmDb db, HttpContext ctx) =>
        {
            if (Validar(i) is { } erro) return TypedResults.BadRequest(erro);
            if (await DoDono(db, ctx).Include(f => f.Talhoes).Include(f => f.Colhedoras).FirstOrDefaultAsync(f => f.Id == id) is not { } f)
                return TypedResults.NotFound();
            f.Nome = i.Nome.Trim(); f.Lat = i.Lat; f.Lon = i.Lon;
            if (i.DiasUteisPct is { } u) f.DiasUteisPct = u;
            if (i.RiscoMax is { } r) f.RiscoMax = r;
            await db.SaveChangesAsync();
            return TypedResults.Ok(Saida(f));
        });

        fz.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
            await DoDono(db, ctx).Where(f => f.Id == id).ExecuteDeleteAsync() > 0 ? TypedResults.NoContent() : TypedResults.NotFound())
          .WithSummary("Apaga a fazenda com talhões, colhedoras e colheitas");

        // ---- talhões ----
        fz.MapPost("/{id:int}/talhoes", async Task<Results<Created<TalhaoOut>, NotFound, BadRequest<string>>> (int id, TalhaoIn i, FarmDb db, HttpContext ctx) =>
        {
            if (!await DoDono(db, ctx).AnyAsync(f => f.Id == id)) return TypedResults.NotFound();
            var t = new Talhao { FazendaId = id, Nome = "" };
            if (Aplicar(i, t) is { } erro) return TypedResults.BadRequest(erro);
            db.Add(t);
            await db.SaveChangesAsync();
            return TypedResults.Created($"/talhoes/{t.Id}", Saida(t));
        }).WithTags("Talhão").WithSummary("Cria um talhão: área (ha), classe de solo AD1–AD6 e ponto lat/lon ou polígono GeoJSON");

        var th = app.MapGroup("/talhoes").WithTags("Talhão");

        th.MapGet("/{id:int}", async Task<Results<Ok<TalhaoOut>, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
            await TalhoesDoDono(db, ctx).FirstOrDefaultAsync(t => t.Id == id) is { } t ? TypedResults.Ok(Saida(t)) : TypedResults.NotFound());

        th.MapPut("/{id:int}", async Task<Results<Ok<TalhaoOut>, NotFound, BadRequest<string>>> (int id, TalhaoIn i, FarmDb db, HttpContext ctx) =>
        {
            if (await TalhoesDoDono(db, ctx).FirstOrDefaultAsync(t => t.Id == id) is not { } t) return TypedResults.NotFound();
            if (Aplicar(i, t) is { } erro) return TypedResults.BadRequest(erro);
            await db.SaveChangesAsync();
            return TypedResults.Ok(Saida(t));
        });

        th.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
            await TalhoesDoDono(db, ctx).Where(t => t.Id == id).ExecuteDeleteAsync() > 0 ? TypedResults.NoContent() : TypedResults.NotFound());

        // ---- colhedoras ----
        fz.MapPost("/{id:int}/colhedoras", async Task<Results<Created<ColhedoraOut>, NotFound, BadRequest<string>>> (int id, ColhedoraIn i, FarmDb db, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(i.Nome) || i.HaDia <= 0) return TypedResults.BadRequest("nome é obrigatório e haDia deve ser maior que zero");
            if (!await DoDono(db, ctx).AnyAsync(f => f.Id == id)) return TypedResults.NotFound();
            var c = new Colhedora { FazendaId = id, Nome = i.Nome.Trim(), HaDia = i.HaDia };
            db.Add(c);
            await db.SaveChangesAsync();
            return TypedResults.Created($"/colhedoras/{c.Id}", Saida(c));
        }).WithTags("Colhedora").WithSummary("Cadastra uma colhedora com a capacidade em ha por dia de trabalho");

        var ch = app.MapGroup("/colhedoras").WithTags("Colhedora");
        ch.MapPut("/{id:int}", async Task<Results<Ok<ColhedoraOut>, NotFound, BadRequest<string>>> (int id, ColhedoraIn i, FarmDb db, HttpContext ctx) =>
        {
            if (string.IsNullOrWhiteSpace(i.Nome) || i.HaDia <= 0) return TypedResults.BadRequest("nome é obrigatório e haDia deve ser maior que zero");
            var d = Dono(ctx);
            if (await db.Colhedoras.FirstOrDefaultAsync(c => c.Id == id && c.Fazenda.DonoId == d) is not { } c) return TypedResults.NotFound();
            c.Nome = i.Nome.Trim(); c.HaDia = i.HaDia;
            await db.SaveChangesAsync();
            return TypedResults.Ok(Saida(c));
        });
        ch.MapDelete("/{id:int}", async Task<Results<NoContent, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
        {
            var d = Dono(ctx);
            return await db.Colhedoras.Where(c => c.Id == id && c.Fazenda.DonoId == d).ExecuteDeleteAsync() > 0
                ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        // ---- "colhi o talhão X no dia D" ----
        th.MapPost("/{id:int}/colheitas", async Task<IResult> (int id, ColheitaIn i, FarmDb db, HttpContext ctx, IHttpClientFactory http) =>
        {
            if (await TalhoesDoDono(db, ctx).Include(t => t.Fazenda).FirstOrDefaultAsync(t => t.Id == id) is not { } t)
                return TypedResults.NotFound();
            if ((t.Lat ?? t.Fazenda.Lat, t.Lon ?? t.Fazenda.Lon) is not (double lat, double lon))
                return TypedResults.BadRequest("talhão sem coordenada: informe lat/lon ou polígono (ou lat/lon na fazenda)");

            var cad = Solos.Cad[t.ClasseSolo];
            var (cal, erro) = await CalendarioAsync(http, lat, lon, i.Data, i.Data, cad, t.Fazenda.RiscoMax, i.FonteChuva, i.AguaInicial);
            if (cal is null) return erro!;
            var r = cal.Decendios[0];

            var e = new ColheitaEvento
            {
                TalhaoId = t.Id, Data = i.Data, Cultura = r.Recomendacao, Decendio = r.Decendio.ToString(),
                RiscoMilho = r.Risco.TryGetValue("milho", out var m) ? Math.Round(m, 4) : null,
                RiscoSorgo = r.Risco.TryGetValue("sorgo", out var s) ? Math.Round(s, 4) : null,
                Cad = cad, RiscoMax = t.Fazenda.RiscoMax, FonteChuva = cal.FonteChuva,
            };
            db.Add(e);
            await db.SaveChangesAsync();
            return TypedResults.Created($"/colheitas/{e.Id}", Saida(e));
        })
        .WithTags("Colheita")
        .WithSummary("Registra \"colhi a soja deste talhão no dia D\" e devolve a cultura indicada para plantar")
        .WithDescription("Usa a classe de solo e a coordenada do talhão e o riscoMax da fazenda. A recomendação fica gravada.")
        .Produces<ColheitaOut>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound).Produces<string>(StatusCodes.Status400BadRequest);

        th.MapGet("/{id:int}/colheitas", async Task<Results<Ok<List<ColheitaOut>>, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
        {
            if (!await TalhoesDoDono(db, ctx).AnyAsync(t => t.Id == id)) return TypedResults.NotFound();
            var lista = await db.Colheitas.Where(e => e.TalhaoId == id).OrderByDescending(e => e.Data).ThenByDescending(e => e.Id).ToListAsync();
            return TypedResults.Ok(lista.Select(Saida).ToList());
        }).WithTags("Colheita").WithSummary("Colheitas registradas no talhão, da mais recente para a mais antiga");

        app.MapDelete("/colheitas/{id:int}", async Task<Results<NoContent, NotFound>> (int id, FarmDb db, HttpContext ctx) =>
        {
            var d = Dono(ctx);
            return await db.Colheitas.Where(e => e.Id == id && e.Talhao.Fazenda.DonoId == d).ExecuteDeleteAsync() > 0
                ? TypedResults.NoContent() : TypedResults.NotFound();
        }).WithTags("Colheita");

        // ---- plano da fazenda inteira: talhões colhidos um depois do outro ----
        fz.MapGet("/{id:int}/plano-colheita", async Task<IResult> (int id, DateOnly inicio, FarmDb db, HttpContext ctx, IHttpClientFactory http,
            string? ordem = null, string fonteChuva = "auto", string aguaInicial = "auto") =>
        {
            if (await DoDono(db, ctx).Include(f => f.Talhoes).Include(f => f.Colhedoras).FirstOrDefaultAsync(f => f.Id == id) is not { } f)
                return TypedResults.NotFound();
            var capacidade = Colheita.Capacidade(f.Colhedoras.Sum(c => c.HaDia), f.DiasUteisPct);
            if (capacidade <= 0) return TypedResults.BadRequest("cadastre ao menos uma colhedora");
            if (f.Talhoes.Count == 0) return TypedResults.BadRequest("cadastre ao menos um talhão");

            // ordem de colheita: ids informados primeiro, o resto pela ordem de cadastro
            var pos = (ordem ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((s, k) => (ok: int.TryParse(s, out var n), n, k)).Where(x => x.ok).GroupBy(x => x.n).ToDictionary(g => g.Key, g => g.First().k);
            var fila = f.Talhoes.OrderBy(t => pos.GetValueOrDefault(t.Id, int.MaxValue)).ThenBy(t => t.Id).ToList();

            var cursor = inicio;
            var talhoes = new List<TalhaoPlano>();
            foreach (var t in fila)
            {
                if ((t.Lat ?? f.Lat, t.Lon ?? f.Lon) is not (double lat, double lon))
                    return TypedResults.BadRequest($"talhão \"{t.Nome}\" sem coordenada: informe lat/lon ou polígono (ou lat/lon na fazenda)");
                var fim = Colheita.Fim(cursor, Colheita.Dias(t.AreaHa, capacidade));
                if (fim.DayNumber - inicio.DayNumber >= 365) return TypedResults.BadRequest("a colheita passaria de 365 dias: confira áreas e colhedoras");

                var (cal, erro) = await CalendarioAsync(http, lat, lon, cursor, fim, Solos.Cad[t.ClasseSolo], f.RiscoMax, fonteChuva, aguaInicial);
                if (cal is null) return erro!;
                var trechos = Colheita.Trechos(cursor, fim, d => cal.NoDia(d)!.Recomendacao, t.AreaHa, capacidade)
                    .Select(x => new ColheitaApi.TrechoOut(x.Cultura, x.De, x.Ate, x.Dias, x.Ha)).ToList();
                talhoes.Add(new(t.Id, t.Nome, t.ClasseSolo, t.AreaHa, cursor, fim, fim.DayNumber - cursor.DayNumber + 1, trechos));
                cursor = fim.AddDays(1);
            }
            var totais = talhoes.SelectMany(t => t.Trechos).GroupBy(x => x.Cultura)
                .Select(g => new TotalCultura(g.Key, Math.Round(g.Sum(x => x.Ha ?? 0), 1))).OrderByDescending(x => x.Ha).ToList();
            return TypedResults.Ok(new PlanoFazendaOut(f.Id, inicio, cursor.AddDays(-1), Math.Round(capacidade, 2), talhoes, totais));
        })
        .WithTags("Colheita")
        .WithSummary("Plano de colheita da fazenda: cada talhão, quando é colhido e quantos ha vão para cada cultura")
        .WithDescription("Talhões colhidos em sequência com a capacidade somada das colhedoras × diasUteisPct. " +
                         "`ordem` = ids separados por vírgula (os não citados vêm depois, por ordem de cadastro). " +
                         "Cada talhão usa a própria classe de solo e coordenada.")
        .Produces<PlanoFazendaOut>().Produces(StatusCodes.Status404NotFound).Produces<string>(StatusCodes.Status400BadRequest);
    }
}

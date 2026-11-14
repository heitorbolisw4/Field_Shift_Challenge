using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

// POST /talhoes/{id}/colheitas: "colhi este talhão no dia D" → cultura indicada, gravada
[Collection("api")]
public class ColheitaTests(ApiFabrica api)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;

    async Task<(HttpClient C, JsonElement F, JsonElement T)> Talhao(double riscoMax, string solo, object? poligono = null, bool coordNaFazenda = true)
    {
        var c = await api.Cliente();
        var f = coordNaFazenda
            ? await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon, riscoMax })
            : await c.PostOk("/fazendas", new { nome = "F", riscoMax });
        var t = poligono is null
            ? await c.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", areaHa = 50, classeSolo = solo })
            : await c.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", classeSolo = solo, poligono });
        return (c, f, t);
    }

    // a cultura gravada é a mesma que /recomendacao dá para o decêndio da data, com a CAD do solo e o riscoMax da fazenda
    [Theory]
    [InlineData("2026-01-05", "AD3", 0.2)]
    [InlineData("2026-01-15", "AD3", 0.2)]
    [InlineData("2026-01-25", "AD5", 0.3)]
    [InlineData("2026-02-05", "AD1", 0.4)]
    [InlineData("2026-02-15", "AD6", 0.2)]
    [InlineData("2026-12-25", "AD3", 0.2)]
    [InlineData("2028-02-29", "AD4", 0.2)]   // bissexto: vira 28/02 no ano-modelo
    public async Task Igual_a_recomendacao_da_data(string data, string solo, double riscoMax)
    {
        var (c, _, t) = await Talhao(riscoMax, solo);
        var d = DateOnly.Parse(data);
        var r = await c.PostAsJsonAsync($"/talhoes/{t.Id()}/colheitas", new { data });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var e = await r.Json();

        var rec = (await Apoio.Recomendacao(c, Lat, Lon, d, d, t.GetProperty("cadMm").GetDouble(), riscoMax)).Single().Value;
        Assert.Equal(Apoio.DecendioDe(d), e.GetProperty("decendio").GetString());
        Assert.Equal(rec.GetProperty("decendio").GetString(), e.GetProperty("decendio").GetString());
        Assert.Equal(rec.GetProperty("recomendacao").GetString(), e.GetProperty("cultura").GetString());
        foreach (var g in new[] { "milho", "sorgo" })
        {
            Assert.Equal(rec.GetProperty("risco").GetProperty(g).GetDouble(), Math.Round(e.GetProperty("risco").GetProperty(g).GetDouble(), 2));
            Assert.Equal(rec.GetProperty("nivel").GetProperty(g).GetString(), e.GetProperty("nivel").GetProperty(g).GetString());
        }
        Assert.Equal(t.GetProperty("cadMm").GetDouble(), e.GetProperty("cad").GetDouble());
        Assert.Equal(riscoMax, e.GetProperty("riscoMax").GetDouble());
        Assert.Equal("power", e.GetProperty("fonteChuva").GetString());
        Assert.Equal(d, e.Data("data"));
    }

    // talhão com polígono usa o centroide, não a sede da fazenda
    [Fact]
    public async Task Usa_o_centroide_do_poligono()
    {
        var (lon0, lat0) = (-56.2, -14.8); // longe da sede
        var pol = new { type = "Polygon", coordinates = new[] { new[] {
            new[] { lon0, lat0 }, new[] { lon0 + 0.02, lat0 }, new[] { lon0 + 0.02, lat0 + 0.02 }, new[] { lon0, lat0 + 0.02 }, new[] { lon0, lat0 } } } };
        var (c, _, t) = await Talhao(0.2, "AD3", pol);
        var data = new DateOnly(2026, 1, 25);
        var e = await c.PostOk($"/talhoes/{t.Id()}/colheitas", new { data });

        var rec = (await Apoio.Recomendacao(c, t.GetProperty("lat").GetDouble(), t.GetProperty("lon").GetDouble(), data, data, 30, 0.2)).Single().Value;
        Assert.Equal(rec.GetProperty("recomendacao").GetString(), e.GetProperty("cultura").GetString());
        Assert.Equal(rec.GetProperty("risco").GetProperty("milho").GetDouble(), Math.Round(e.GetProperty("risco").GetProperty("milho").GetDouble(), 2));
    }

    [Fact]
    public async Task Talhao_sem_coordenada_herda_a_da_fazenda_e_sem_nenhuma_da_400()
    {
        var (c, f, t) = await Talhao(0.2, "AD3");
        Assert.Equal(HttpStatusCode.Created, (await c.PostAsJsonAsync($"/talhoes/{t.Id()}/colheitas", new { data = "2026-01-15" })).StatusCode);

        var (c2, _, t2) = await Talhao(0.2, "AD3", coordNaFazenda: false);
        var r = await c2.PostAsJsonAsync($"/talhoes/{t2.Id()}/colheitas", new { data = "2026-01-15" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c2.GetOk($"/talhoes/{t2.Id()}/colheitas")).EnumerateArray());
    }

    [Theory]
    [InlineData("""{"data":"2026-01-15","fonteChuva":"imerg"}""")]
    [InlineData("""{"data":"2026-01-15","fonteChuva":"xyz"}""")]
    [InlineData("""{"data":"2026-01-15","aguaInicial":"meio"}""")]
    public async Task Parametro_invalido_da_400_e_nao_grava(string corpo)
    {
        var (c, _, t) = await Talhao(0.2, "AD3");
        var r = await c.PostAsync($"/talhoes/{t.Id()}/colheitas", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c.GetOk($"/talhoes/{t.Id()}/colheitas")).EnumerateArray());
    }

    // a recomendação fica gravada: mudar o riscoMax da fazenda ou o solo depois não reescreve o histórico
    [Fact]
    public async Task Historico_gravado_ordem_e_apagar()
    {
        var (c, f, t) = await Talhao(0.2, "AD3");
        var a = await c.PostOk($"/talhoes/{t.Id()}/colheitas", new { data = "2026-01-05" });
        var b = await c.PostOk($"/talhoes/{t.Id()}/colheitas", new { data = "2026-02-15" });
        var b2 = await c.PostOk($"/talhoes/{t.Id()}/colheitas", new { data = "2026-02-15" }); // mesmo dia: grava de novo

        await c.PutAsJsonAsync($"/fazendas/{f.Id()}", new { nome = "F", lat = Lat, lon = Lon, riscoMax = 0.9 });
        await c.PutAsJsonAsync($"/talhoes/{t.Id()}", new { nome = "T", areaHa = 50, classeSolo = "AD6" });

        var lista = (await c.GetOk($"/talhoes/{t.Id()}/colheitas")).EnumerateArray().ToList();
        Assert.Equal([b2.Id(), b.Id(), a.Id()], lista.Select(x => x.Id()));   // data desc, depois id desc
        Assert.All(lista, x => Assert.Equal(0.2, x.GetProperty("riscoMax").GetDouble()));
        Assert.All(lista, x => Assert.Equal(30, x.GetProperty("cad").GetDouble()));

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/colheitas/{b.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/colheitas/{b.Id()}")).StatusCode);
        Assert.Equal(2, (await c.GetOk($"/talhoes/{t.Id()}/colheitas")).GetArrayLength());
    }

    [Fact]
    public async Task Talhao_inexistente_da_404()
    {
        var c = await api.Cliente();
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync("/talhoes/999999/colheitas", new { data = "2026-01-15" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/talhoes/999999/colheitas")).StatusCode);
    }
}

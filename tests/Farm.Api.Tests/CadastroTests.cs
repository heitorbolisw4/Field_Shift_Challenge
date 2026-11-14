using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

[Collection("api")]
public class CadastroTests(ApiFabrica api)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;

    // quadrado de 0,01° × 0,01° (o exemplo do docs/api-fazenda.md deslocado para longe dos CSVs reais)
    static object Quadrado(double lon0 = Lon, double lat0 = Lat, double lado = 0.01) => new
    {
        type = "Polygon",
        coordinates = new[] { new[] {
            new[] { lon0, lat0 }, new[] { lon0 + lado, lat0 }, new[] { lon0 + lado, lat0 + lado },
            new[] { lon0, lat0 + lado }, new[] { lon0, lat0 } } }
    };

    // ---------- fazendas ----------

    [Fact]
    public async Task Fazenda_crud_completo()
    {
        var c = await api.Cliente();
        var r = await c.PostAsJsonAsync("/fazendas", new { nome = "  Boa Vista  ", lat = Lat, lon = Lon });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var f = await r.Json();
        Assert.Equal($"/fazendas/{f.Id()}", r.Headers.Location!.OriginalString);
        Assert.Equal("Boa Vista", f.GetProperty("nome").GetString());              // trim
        Assert.Equal(70, f.GetProperty("diasUteisPct").GetDouble());                // padrões
        Assert.Equal(0.2, f.GetProperty("riscoMax").GetDouble());
        Assert.Equal(0, f.GetProperty("areaHa").GetDouble());
        Assert.Empty(f.GetProperty("talhoes").EnumerateArray());

        var lista = await c.GetOk("/fazendas");
        Assert.Equal([f.Id()], lista.EnumerateArray().Select(x => x.Id()));

        var put = await c.PutAsJsonAsync($"/fazendas/{f.Id()}", new { nome = "Boa Vista II", lat = Lat, lon = Lon, diasUteisPct = 80, riscoMax = 0.3 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var g = await c.GetOk($"/fazendas/{f.Id()}");
        Assert.Equal("Boa Vista II", g.GetProperty("nome").GetString());
        Assert.Equal(80, g.GetProperty("diasUteisPct").GetDouble());
        Assert.Equal(0.3, g.GetProperty("riscoMax").GetDouble());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/fazendas/{f.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/fazendas/{f.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/fazendas/{f.Id()}")).StatusCode);
    }

    // PUT sem diasUteisPct/riscoMax mantém os valores; mas lat/lon omitidos viram null (semântica mista)
    [Fact]
    public async Task Fazenda_put_parcial_mantem_percentuais_mas_apaga_coordenada()
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "A", lat = Lat, lon = Lon, diasUteisPct = 55, riscoMax = 0.4 });
        await c.PutAsJsonAsync($"/fazendas/{f.Id()}", new { nome = "A" });
        var g = await c.GetOk($"/fazendas/{f.Id()}");
        Assert.Equal(55, g.GetProperty("diasUteisPct").GetDouble());
        Assert.Equal(0.4, g.GetProperty("riscoMax").GetDouble());
        Assert.Equal(JsonValueKind.Null, g.GetProperty("lat").ValueKind);
    }

    public static TheoryData<string> FazendasInvalidas => new()
    {
        """{"nome":""}""",
        """{"nome":"   "}""",
        """{"nome":"x","lat":-15}""",
        """{"nome":"x","lat":-91,"lon":0}""",
        """{"nome":"x","lat":0,"lon":181}""",
        """{"nome":"x","diasUteisPct":0}""",
        """{"nome":"x","diasUteisPct":101}""",
        """{"nome":"x","riscoMax":-0.1}""",
        """{"nome":"x","riscoMax":1.5}""",
    };

    [Theory, MemberData(nameof(FazendasInvalidas))]
    public async Task Fazenda_invalida_da_400(string corpo)
    {
        var c = await api.Cliente();
        var r = await c.PostAsync("/fazendas", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c.GetOk("/fazendas")).EnumerateArray());
    }

    // apagar a fazenda tem que levar talhões, colhedoras e colheitas (cascade no SQLite via ExecuteDelete)
    [Fact]
    public async Task Apagar_fazenda_apaga_filhos_no_banco()
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon });
        var t = await c.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", areaHa = 10 });
        await c.PostOk($"/fazendas/{f.Id()}/colhedoras", new { nome = "C", haDia = 50 });
        await c.PostOk($"/talhoes/{t.Id()}/colheitas", new { data = "2026-01-15" });

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/fazendas/{f.Id()}")).StatusCode);

        using var escopo = api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<FarmDb>();
        Assert.False(await db.Talhoes.AnyAsync(x => x.FazendaId == f.Id()));
        Assert.False(await db.Colhedoras.AnyAsync(x => x.FazendaId == f.Id()));
        Assert.False(await db.Colheitas.AnyAsync(x => x.TalhaoId == t.Id()));
    }

    [Fact]
    public async Task Banco_e_o_temporario_nao_o_data_farm_db()
    {
        await (await api.Cliente()).GetOk("/fazendas");
        Assert.True(File.Exists(api.Banco));
    }

    // ---------- talhões ----------

    [Fact]
    public async Task Talhao_com_poligono_vira_centroide_e_area()
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F" });
        var r = await c.PostAsJsonAsync($"/fazendas/{f.Id()}/talhoes", new { nome = "T1", classeSolo = "ad5", poligono = Quadrado() });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var t = await r.Json();
        Assert.Equal($"/talhoes/{t.Id()}", r.Headers.Location!.OriginalString);

        // 0,01° de lat = 1111,95 m; 0,01° de lon × cos(-15,545°) = 1071,25 m  →  ~119,1 ha
        var esperado = 1111.95 * 1111.95 * Math.Cos(-15.545 * Math.PI / 180) / 10_000;
        Assert.InRange(t.GetProperty("areaHa").GetDouble(), esperado - 0.2, esperado + 0.2);
        Assert.Equal(Lat + 0.005, t.GetProperty("lat").GetDouble(), 5);
        Assert.Equal(Lon + 0.005, t.GetProperty("lon").GetDouble(), 5);
        Assert.Equal("AD5", t.GetProperty("classeSolo").GetString());   // normaliza caixa
        Assert.Equal(61, t.GetProperty("cadMm").GetDouble());
        Assert.Equal("Polygon", t.GetProperty("poligono").GetProperty("type").GetString());

        // aparece na fazenda e soma na área
        var g = await c.GetOk($"/fazendas/{f.Id()}");
        Assert.Equal(t.GetProperty("areaHa").GetDouble(), g.GetProperty("areaHa").GetDouble());
    }

    [Fact]
    public async Task Talhao_feature_guarda_so_a_geometria_e_areaHa_explicita_vence()
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F" });
        var feature = new { type = "Feature", properties = new { x = 1 }, geometry = Quadrado() };
        var t = await c.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", areaHa = 42.5, poligono = feature });
        Assert.Equal(42.5, t.GetProperty("areaHa").GetDouble());
        Assert.Equal("Polygon", t.GetProperty("poligono").GetProperty("type").GetString());
        Assert.False(t.GetProperty("poligono").TryGetProperty("properties", out _));
    }

    public static TheoryData<string> TalhoesInvalidos => new()
    {
        """{"nome":"T","areaHa":10,"classeSolo":"AD7"}""",
        """{"nome":"T","areaHa":10,"classeSolo":"AD0"}""",
        """{"nome":"T","areaHa":10,"classeSolo":"argiloso"}""",
        """{"nome":"T","areaHa":10,"classeSolo":""}""",
        """{"nome":"","areaHa":10}""",
        """{"nome":"T"}""",
        """{"nome":"T","areaHa":0}""",
        """{"nome":"T","areaHa":-5}""",
        """{"nome":"T","areaHa":10,"lat":-15}""",
        """{"nome":"T","areaHa":10,"lat":-95,"lon":0}""",
        """{"nome":"T","poligono":{"type":"Point","coordinates":[-55,-15]}}""",
        """{"nome":"T","poligono":{"type":"Polygon","coordinates":[[[-55,-15],[-54.9,-15],[-55,-15]]]}}""",
        """{"nome":"T","poligono":{"type":"Polygon","coordinates":[[[-200,-15],[-54.9,-15],[-54.9,-14.9],[-200,-15]]]}}""",
        """{"nome":"T","poligono":{"type":"Polygon","coordinates":[[["a","b"],[1,2],[3,4],[5,6]]]}}""",
        """{"nome":"T","poligono":{"type":"Polygon","coordinates":[[1,2],[3,4],[5,6],[1,2]]}}""",
        """{"nome":"T","poligono":{"type":"Polygon"}}""",
        """{"nome":"T","poligono":{"type":"Feature","geometry":null}}""",
        """{"nome":"T","poligono":{"type":"Feature"}}""",
        """{"nome":"T","poligono":"abc"}""",
        """{"nome":"T","poligono":[1,2,3]}""",
        // polígono degenerado (área 0) sem areaHa
        """{"nome":"T","poligono":{"type":"Polygon","coordinates":[[[-55,-15],[-55,-15],[-55,-15],[-55,-15]]]}}""",
    };

    [Theory, MemberData(nameof(TalhoesInvalidos))]
    public async Task Talhao_invalido_da_400_e_nao_grava(string corpo)
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F" });
        var r = await c.PostAsync($"/fazendas/{f.Id()}/talhoes", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty((await c.GetOk($"/fazendas/{f.Id()}")).GetProperty("talhoes").EnumerateArray());
    }

    [Fact]
    public async Task Talhao_get_put_delete()
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F" });
        var t = await c.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", poligono = Quadrado() });

        // PUT sem polígono: some a geometria e a coordenada antiga
        var put = await c.PutAsJsonAsync($"/talhoes/{t.Id()}", new { nome = "T2", areaHa = 33.3, classeSolo = "AD1" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var g = await c.GetOk($"/talhoes/{t.Id()}");
        Assert.Equal("T2", g.GetProperty("nome").GetString());
        Assert.Equal(33.3, g.GetProperty("areaHa").GetDouble());
        Assert.Equal(17, g.GetProperty("cadMm").GetDouble());
        Assert.Equal(JsonValueKind.Null, g.GetProperty("poligono").ValueKind);
        Assert.Equal(JsonValueKind.Null, g.GetProperty("lat").ValueKind);

        // PUT inválido não altera nada
        var ruim = await c.PutAsJsonAsync($"/talhoes/{t.Id()}", new { nome = "T3", areaHa = 10, classeSolo = "AD9" });
        Assert.Equal(HttpStatusCode.BadRequest, ruim.StatusCode);
        Assert.Equal("T2", (await c.GetOk($"/talhoes/{t.Id()}")).GetProperty("nome").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/talhoes/{t.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/talhoes/{t.Id()}")).StatusCode);
    }

    [Fact]
    public async Task Talhao_em_fazenda_inexistente_da_404()
    {
        var r = await (await api.Cliente()).PostAsJsonAsync("/fazendas/999999/talhoes", new { nome = "T", areaHa = 1 });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Solos_lista_AD1_a_AD6()
    {
        var s = await (await api.Cliente()).GetOk("/solos");
        Assert.Equal(["AD1", "AD2", "AD3", "AD4", "AD5", "AD6"], s.EnumerateArray().Select(x => x.GetProperty("classe").GetString()));
    }

    // ---------- colhedoras ----------

    [Fact]
    public async Task Colhedora_crud_e_soma_na_fazenda()
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F" });
        var a = await c.PostOk($"/fazendas/{f.Id()}/colhedoras", new { nome = " S680 ", haDia = 60 });
        var b = await c.PostOk($"/fazendas/{f.Id()}/colhedoras", new { nome = "S790", haDia = 45.5 });
        Assert.Equal("S680", a.GetProperty("nome").GetString());
        Assert.Equal(105.5, (await c.GetOk($"/fazendas/{f.Id()}")).GetProperty("haDiaColhedoras").GetDouble());

        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/colhedoras/{b.Id()}", new { nome = "S790", haDia = 40 })).StatusCode);
        Assert.Equal(100, (await c.GetOk($"/fazendas/{f.Id()}")).GetProperty("haDiaColhedoras").GetDouble());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/colhedoras/{a.Id()}")).StatusCode);
        Assert.Equal(40, (await c.GetOk($"/fazendas/{f.Id()}")).GetProperty("haDiaColhedoras").GetDouble());
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/colhedoras/{a.Id()}")).StatusCode);
    }

    [Theory]
    [InlineData("""{"nome":"C","haDia":0}""")]
    [InlineData("""{"nome":"C","haDia":-10}""")]
    [InlineData("""{"nome":"","haDia":10}""")]
    public async Task Colhedora_invalida_da_400(string corpo)
    {
        var c = await api.Cliente();
        var f = await c.PostOk("/fazendas", new { nome = "F" });
        var r = await c.PostAsync($"/fazendas/{f.Id()}/colhedoras", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var b = await c.PostOk($"/fazendas/{f.Id()}/colhedoras", new { nome = "ok", haDia = 10 });
        var put = await c.PutAsync($"/colhedoras/{b.Id()}", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    // ---------- isolamento por dono (X-Usuario) ----------

    [Fact]
    public async Task Outro_usuario_recebe_404_em_tudo()
    {
        var dono = await api.Cliente();
        var intruso = await api.Cliente();
        var f = await dono.PostOk("/fazendas", new { nome = "Minha", lat = Lat, lon = Lon });
        var t = await dono.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", areaHa = 10 });
        var m = await dono.PostOk($"/fazendas/{f.Id()}/colhedoras", new { nome = "C", haDia = 50 });
        var e = await dono.PostOk($"/talhoes/{t.Id()}/colheitas", new { data = "2026-01-15" });

        Assert.Empty((await intruso.GetOk("/fazendas")).EnumerateArray());

        async Task NaoAcha(Task<HttpResponseMessage> r, string o) => Assert.True((await r).StatusCode == HttpStatusCode.NotFound, o);
        await NaoAcha(intruso.GetAsync($"/fazendas/{f.Id()}"), "GET fazenda");
        await NaoAcha(intruso.PutAsJsonAsync($"/fazendas/{f.Id()}", new { nome = "roubada" }), "PUT fazenda");
        await NaoAcha(intruso.PostAsJsonAsync($"/fazendas/{f.Id()}/talhoes", new { nome = "X", areaHa = 1 }), "POST talhão");
        await NaoAcha(intruso.PostAsJsonAsync($"/fazendas/{f.Id()}/colhedoras", new { nome = "X", haDia = 1 }), "POST colhedora");
        await NaoAcha(intruso.GetAsync($"/talhoes/{t.Id()}"), "GET talhão");
        await NaoAcha(intruso.PutAsJsonAsync($"/talhoes/{t.Id()}", new { nome = "X", areaHa = 1 }), "PUT talhão");
        await NaoAcha(intruso.PutAsJsonAsync($"/colhedoras/{m.Id()}", new { nome = "X", haDia = 1 }), "PUT colhedora");
        await NaoAcha(intruso.PostAsJsonAsync($"/talhoes/{t.Id()}/colheitas", new { data = "2026-01-15" }), "POST colheita");
        await NaoAcha(intruso.GetAsync($"/talhoes/{t.Id()}/colheitas"), "GET colheitas");
        await NaoAcha(intruso.GetAsync($"/fazendas/{f.Id()}/plano-colheita?inicio=2026-01-15"), "GET plano");
        await NaoAcha(intruso.DeleteAsync($"/colheitas/{e.Id()}"), "DELETE colheita");
        await NaoAcha(intruso.DeleteAsync($"/colhedoras/{m.Id()}"), "DELETE colhedora");
        await NaoAcha(intruso.DeleteAsync($"/talhoes/{t.Id()}"), "DELETE talhão");
        await NaoAcha(intruso.DeleteAsync($"/fazendas/{f.Id()}"), "DELETE fazenda");

        // nada mudou para o dono
        var g = await dono.GetOk($"/fazendas/{f.Id()}");
        Assert.Equal("Minha", g.GetProperty("nome").GetString());
        Assert.Single(g.GetProperty("talhoes").EnumerateArray());
        Assert.Single(g.GetProperty("colhedoras").EnumerateArray());
        Assert.Single((await dono.GetOk($"/talhoes/{t.Id()}/colheitas")).EnumerateArray());
    }

    // sem token: 401 em todas as rotas da fazenda; o antigo X-Usuario não vale mais nada
    [Fact]
    public async Task Sem_token_da_401_e_XUsuario_foi_aposentado()
    {
        var dono = await api.Cliente();
        var f = await dono.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon });
        var t = await dono.PostOk($"/fazendas/{f.Id()}/talhoes", new { nome = "T", areaHa = 10 });
        var m = await dono.PostOk($"/fazendas/{f.Id()}/colhedoras", new { nome = "C", haDia = 50 });
        var e = await dono.PostOk($"/talhoes/{t.Id()}/colheitas", new { data = "2026-01-15" });

        var anon = api.Anonimo();
        anon.DefaultRequestHeaders.Add("X-Usuario", "local");
        var rotas = new (HttpMethod, string)[]
        {
            (HttpMethod.Get, "/fazendas"), (HttpMethod.Post, "/fazendas"),
            (HttpMethod.Get, $"/fazendas/{f.Id()}"), (HttpMethod.Put, $"/fazendas/{f.Id()}"), (HttpMethod.Delete, $"/fazendas/{f.Id()}"),
            (HttpMethod.Post, $"/fazendas/{f.Id()}/talhoes"), (HttpMethod.Post, $"/fazendas/{f.Id()}/colhedoras"),
            (HttpMethod.Get, $"/fazendas/{f.Id()}/plano-colheita?inicio=2026-01-15"),
            (HttpMethod.Get, $"/talhoes/{t.Id()}"), (HttpMethod.Put, $"/talhoes/{t.Id()}"), (HttpMethod.Delete, $"/talhoes/{t.Id()}"),
            (HttpMethod.Post, $"/talhoes/{t.Id()}/colheitas"), (HttpMethod.Get, $"/talhoes/{t.Id()}/colheitas"),
            (HttpMethod.Put, $"/colhedoras/{m.Id()}"), (HttpMethod.Delete, $"/colhedoras/{m.Id()}"),
            (HttpMethod.Delete, $"/colheitas/{e.Id()}"), (HttpMethod.Get, "/auth/eu"),
        };
        foreach (var (metodo, url) in rotas)
        {
            var req = new HttpRequestMessage(metodo, url);
            if (metodo == HttpMethod.Post || metodo == HttpMethod.Put) req.Content = JsonContent.Create(new { nome = "x", areaHa = 1, haDia = 1, data = "2026-01-15" });
            var r = await anon.SendAsync(req);
            Assert.True(r.StatusCode == HttpStatusCode.Unauthorized, $"{metodo} {url} -> {(int)r.StatusCode}");
        }
        Assert.Single((await dono.GetOk("/fazendas")).EnumerateArray()); // nada foi criado nem apagado

        // rotas sem cadastro continuam abertas
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/solos")).StatusCode);
    }
}

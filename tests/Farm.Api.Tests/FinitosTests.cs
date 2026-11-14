using System.Net;
using System.Text;
using System.Text.Json;

// lacunas da cobertura de 4ff6eb4: PUTs, 1e309 (estoura para Infinity), formato pt-BR na query,
// e os tetos exatos (haDia 1000 × 1000.0001, colhedoras 0/1/100/101) na rota sem cadastro.
[Collection("api")]
public class FinitosTests(ApiFabrica api)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;
    static readonly string Ponto = $"lat={Http.Q(Lat)}&lon={Http.Q(Lon)}";
    const string Inicio = "2026-01-05";

    static StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    static async Task Da400(HttpResponseMessage r, string caso) =>
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{caso} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");

    // ---------- query de /plano-colheita ----------

    [Theory]
    [InlineData("area=1e309")]                 // double.Parse dá Infinity, não erro
    [InlineData("area=100&haDia=1e309")]
    [InlineData("area=100&uteis=-1e309")]
    [InlineData("area=100&haDia=1.000,5")]     // milhar pt-BR: nem 1000,5 nem 1,0005
    [InlineData("area=1.000,5")]
    [InlineData("area=100&haDia=1000.00000001")]
    [InlineData("area=100&haDia=1e3001")]
    [InlineData("area=100&colhedoras=0")]
    [InlineData("area=100&colhedoras=-1")]
    [InlineData("area=100&colhedoras=1.5")]
    [InlineData("area=100&colhedoras=2147483648")]
    [InlineData("area=100&haDia=nan")]          // minúsculas
    [InlineData("area=100&haDia=-NaN")]
    [InlineData("fim=2026-02-01&cad=1e309")]
    public async Task Plano_query_ruim_da_400(string q) =>
        await Da400(await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio={Inicio}&{q}"), q);

    [Theory]
    [InlineData("area=100&haDia=1000")]
    [InlineData("area=100&haDia=1000.0")]
    [InlineData("area=100&haDia=1e3")]
    [InlineData("area=100&haDia=1000&colhedoras=100&uteis=100")]
    [InlineData("area=100&colhedoras=100")]
    [InlineData("area=100&colhedoras=1")]
    public async Task Plano_no_teto_passa(string q)
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio={Inicio}&{q}");
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{q} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=1e309")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=-Infinity")]
    [InlineData("/deficit?plantio=02-15&cultura=milho&fatorChuva=1e309")]
    [InlineData("/deficit?plantio=02-15&cultura=milho&cad=NaN")]
    public async Task Outras_rotas_query_ruim_da_400(string url) =>
        await Da400(await api.Anonimo().GetAsync($"{url}&{Ponto}"), url);

    // O binding da query lê com InvariantCulture + AllowThousands: "60,5" vira 605 e "0,2" vira 2, sem erro.
    // Fontes.ComCoordenadas já recusa vírgula em lat/lon pelo mesmo motivo; os outros doubles passam.
    [Theory(Skip = "BUG: vírgula decimal na query vira separador de milhar (haDia=60,5 -> 605; riscoMax=0,2 -> 2; cad=30,5 -> 305), 200 com dado errado")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=100&haDia=60,5")]
    [InlineData("/plano-colheita?inicio=2026-01-05&fim=2026-02-01&riscoMax=0,2")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=30,5")]
    public async Task Virgula_decimal_na_query_da_400(string url) =>
        await Da400(await api.Anonimo().GetAsync($"{url}&{Ponto}"), url);

    // cad, riscoMax e fatorChuva da query não têm faixa: só o filtro de finitos. No corpo, riscoMax é 0..1.
    [Theory(Skip = "BUG: cad/riscoMax/fatorChuva da query sem faixa: riscoMax=2 ou -1, cad<=0 e fatorChuva=-1 (chuva negativa) dão 200")]
    [InlineData("/plano-colheita?inicio=2026-01-05&fim=2026-02-01&riscoMax=2")]
    [InlineData("/plano-colheita?inicio=2026-01-05&fim=2026-02-01&riscoMax=-1")]
    [InlineData("/plano-colheita?inicio=2026-01-05&fim=2026-02-01&cad=-1")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=0")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=1.5")]
    [InlineData("/deficit?plantio=02-15&cultura=milho&fatorChuva=-1")]
    public async Task Faixa_dos_parametros_da_query_da_400(string url) =>
        await Da400(await api.Anonimo().GetAsync($"{url}&{Ponto}"), url);

    // ---------- corpo JSON: POST ----------

    [Theory]
    [InlineData("colhedoras", """{"nome":"C","haDia":1e309}""")]
    [InlineData("colhedoras", """{"nome":"C","haDia":-Infinity}""")]
    [InlineData("colhedoras", """{"nome":"C","haDia":"1000,5"}""")]
    [InlineData("colhedoras", """{"nome":"C","haDia":0}""")]
    [InlineData("talhoes", """{"nome":"T","areaHa":1e309}""")]
    [InlineData("talhoes", """{"nome":"T","areaHa":"NaN"}""")]
    [InlineData("talhoes", """{"nome":"T","areaHa":10,"lat":0,"lon":"Infinity"}""")]
    public async Task Post_corpo_ruim_da_400(string rota, string corpo)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon })).Id();
        await Da400(await c.PostAsync($"/fazendas/{f}/{rota}", Json(corpo)), corpo);
        Assert.Equal(0, (await c.GetOk($"/fazendas/{f}")).GetProperty(rota).GetArrayLength());
    }

    [Theory]
    [InlineData("""{"nome":"F","diasUteisPct":"Infinity"}""")]
    [InlineData("""{"nome":"F","diasUteisPct":1e309}""")]
    [InlineData("""{"nome":"F","riscoMax":"-Infinity"}""")]
    [InlineData("""{"nome":"F","lat":1e309,"lon":0}""")]
    [InlineData("""{"nome":"F","lat":0,"lon":"NaN"}""")]
    public async Task Post_fazenda_corpo_ruim_da_400(string corpo)
    {
        var c = await api.Cliente();
        await Da400(await c.PostAsync("/fazendas", Json(corpo)), corpo);
        Assert.Equal(0, (await c.GetOk("/fazendas")).GetArrayLength());
    }

    // ---------- corpo JSON: PUT (nenhum teste cobria) ----------

    [Theory]
    [InlineData("""{"nome":"C","haDia":"NaN"}""")]
    [InlineData("""{"nome":"C","haDia":NaN}""")]
    [InlineData("""{"nome":"C","haDia":"Infinity"}""")]
    [InlineData("""{"nome":"C","haDia":1e309}""")]
    [InlineData("""{"nome":"C","haDia":1000.0001}""")]
    [InlineData("""{"nome":"C","haDia":0}""")]
    public async Task Put_colhedora_ruim_da_400_e_nao_altera(string corpo)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon })).Id();
        var h = (await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = 50 })).Id();
        await Da400(await c.PutAsync($"/colhedoras/{h}", Json(corpo)), corpo);
        var fz = await c.GetOk($"/fazendas/{f}");
        Assert.Equal(50, fz.GetProperty("colhedoras")[0].GetProperty("haDia").GetDouble());
        Assert.Equal(50, fz.GetProperty("haDiaColhedoras").GetDouble());
    }

    [Fact]
    public async Task Put_colhedora_no_teto_passa()
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon })).Id();
        var h = (await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = 50 })).Id();
        var r = await c.PutAsync($"/colhedoras/{h}", Json("""{"nome":"C","haDia":1000}"""));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(1000, (await r.Json()).GetProperty("haDia").GetDouble());
    }

    [Theory]
    [InlineData("""{"nome":"F","diasUteisPct":"NaN"}""")]
    [InlineData("""{"nome":"F","diasUteisPct":"Infinity"}""")]
    [InlineData("""{"nome":"F","riscoMax":"NaN"}""")]
    [InlineData("""{"nome":"F","riscoMax":1e309}""")]
    [InlineData("""{"nome":"F","lat":"NaN","lon":"NaN"}""")]
    [InlineData("""{"nome":"F","lat":0,"lon":-1e309}""")]
    public async Task Put_fazenda_ruim_da_400_e_nao_altera(string corpo)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon, diasUteisPct = 70, riscoMax = 0.2 })).Id();
        await Da400(await c.PutAsync($"/fazendas/{f}", Json(corpo)), corpo);
        var fz = await c.GetOk($"/fazendas/{f}");
        Assert.Equal(70, fz.GetProperty("diasUteisPct").GetDouble());
        Assert.Equal(0.2, fz.GetProperty("riscoMax").GetDouble());
        Assert.Equal(Lat, fz.GetProperty("lat").GetDouble());
    }

    [Theory]
    [InlineData("""{"nome":"T","areaHa":"NaN"}""")]
    [InlineData("""{"nome":"T","areaHa":"Infinity"}""")]
    [InlineData("""{"nome":"T","areaHa":1e309}""")]
    [InlineData("""{"nome":"T","areaHa":10,"lat":"NaN","lon":0}""")]
    [InlineData("""{"nome":"T","areaHa":10,"lat":0,"lon":"-Infinity"}""")]
    public async Task Put_talhao_ruim_da_400_e_nao_altera(string corpo)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon })).Id();
        var t = (await c.PostOk($"/fazendas/{f}/talhoes", new { nome = "T", areaHa = 10 })).Id();
        await Da400(await c.PutAsync($"/talhoes/{t}", Json(corpo)), corpo);
        var j = await c.GetOk($"/talhoes/{t}");
        Assert.Equal(10, j.GetProperty("areaHa").GetDouble());
        Assert.Equal(JsonValueKind.Null, j.GetProperty("lat").ValueKind);
    }
}

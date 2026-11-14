using System.Net;

// leitor único dos números da query (src/Farm.Api/Numeros.cs): vírgula, finitos e faixa, em todas as rotas
[Collection("api")]
public class NumerosQueryTests(ApiFabrica api)
{
    static readonly string Ponto = $"lat={Http.Q(ApiFabrica.Lat)}&lon={Http.Q(ApiFabrica.Lon)}";

    static async Task<HttpStatusCode> Get(ApiFabrica api, string url) => (await api.Anonimo().GetAsync(url)).StatusCode;

    // bordas que valem: os limites da faixa são inclusivos (menos onde o mínimo é aberto)
    [Theory]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=0")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=1")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=500")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=0.5")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&fatorChuva=0")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&fatorChuva=5")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=2e-1")]     // notação científica com ponto
    [InlineData("/deficit?plantio=02-15&cultura=milho&fatorChuva=1.25")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=100&uteis=100")]
    public async Task Bordas_validas_passam(string url) =>
        Assert.Equal(HttpStatusCode.OK, await Get(api, $"{url}&{Ponto}"));

    [Theory]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=1.0001")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=-0.0001")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=500.1")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&cad=-0")]          // -0 == 0: mínimo aberto
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&fatorChuva=5.01")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=0,2")]
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=")]       // vazio
    [InlineData("/recomendacao?inicio=01-01&fim=01-10&riscoMax=abc")]
    [InlineData("/deficit?plantio=02-15&cultura=milho&cad=30,5")]
    [InlineData("/deficit?plantio=02-15&cultura=milho&fatorChuva=Infinity")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=100&uteis=0")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=100&uteis=100.5")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=0")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=1000001")]
    [InlineData("/plano-colheita?inicio=2026-01-05&area=100&colhedoras=2.0,0")]
    public async Task Fora_da_faixa_ou_mal_escrito_da_400(string url) =>
        Assert.Equal(HttpStatusCode.BadRequest, await Get(api, $"{url}&{Ponto}"));

    // parâmetro repetido: todos os valores são conferidos, não só o primeiro
    [Fact]
    public async Task Valor_repetido_ruim_da_400() =>
        Assert.Equal(HttpStatusCode.BadRequest,
            await Get(api, $"/recomendacao?inicio=01-01&fim=01-10&{Ponto}&riscoMax=0.2&riscoMax=9"));

    // o nome do parâmetro não diferencia maiúsculas (o binding também não)
    [Fact]
    public async Task Nome_em_maiusculas_tambem_e_conferido() =>
        Assert.Equal(HttpStatusCode.BadRequest, await Get(api, $"/recomendacao?inicio=01-01&fim=01-10&{Ponto}&RISCOMAX=2"));

    // a mensagem diz qual parâmetro e o que está errado
    [Fact]
    public async Task Mensagem_explica_o_erro()
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?inicio=01-01&fim=01-10&{Ponto}&cad=30,5");
        var texto = await r.Content.ReadAsStringAsync();
        Assert.Contains("cad", texto);
        Assert.Contains("ponto", texto);
    }

    // rotas da fazenda também passam pelo leitor (antes do login): número ruim na query dá 400
    [Fact]
    public async Task Vale_tambem_nas_rotas_da_fazenda()
    {
        var c = await api.Cliente();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/fazendas?lat=1,5")).StatusCode);
    }

    // parâmetro que não é número não é mexido (ordem do plano é texto "3,1,2")
    [Fact]
    public async Task Texto_com_virgula_que_nao_e_numero_passa()
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = ApiFabrica.Lat, lon = ApiFabrica.Lon })).Id();
        await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia = 50 });
        await c.PostOk($"/fazendas/{f}/talhoes", new { nome = "T", areaHa = 10 });
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/fazendas/{f}/plano-colheita?inicio=2026-01-05&ordem=3,1,2")).StatusCode);
    }
}

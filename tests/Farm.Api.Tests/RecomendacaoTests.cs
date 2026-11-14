using System.Net;
using System.Text.Json;

// /recomendacao é rota antiga e não podia mudar com a fazenda nem com o login
[Collection("api")]
public class RecomendacaoTests(ApiFabrica api)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;

    public static TheoryData<string, string, double, double, string, double, string> Casos => new()
    {
        { "01-01", "02-28", 30, 0.2, "isna", 1, "pm" },
        { "01-10", "03-10", 61, 0.3, "isna", 1, "pm" },
        { "01-01", "02-10", 17, 0.2, "perda", 1, "pm" },
        { "01-01", "02-10", 30, 0.2, "isna", 0.8, "hs" },
        { "12-01", "01-31", 30, 0.2, "isna", 1, "pm" },   // cruza o ano
        { "02-25", "03-05", 30, 0.2, "isna", 1, "pm" },   // passa por 29/02 no ano-modelo (não existe)
    };

    // mesmo resultado que o Farm.Core calcula direto com o mesmo clima
    [Theory, MemberData(nameof(Casos))]
    public async Task Igual_ao_Calendario_do_Core(string ini, string fim, double cad, double riscoMax, string criterio, double fatorChuva, string et0)
    {
        var j = await api.Anonimo().GetOk($"/recomendacao?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio={ini}&fim={fim}&cad={Http.Q(cad)}" +
                                          $"&riscoMax={Http.Q(riscoMax)}&criterio={criterio}&fatorChuva={Http.Q(fatorChuva)}&et0={et0}");

        var dias = Apoio.ClimaDaApi().ToDictionary(d => d.Data);
        var p = new Parametros(Lat, cad, fatorChuva, et0 == "pm", criterio, riscoMax);
        var (di, df) = Calendario.Janela(DateOnly.Parse($"2001-{ini}"), DateOnly.Parse($"2001-{fim}"));
        var esperado = Calendario.Calcular(di, df, dias, p);

        var obtido = j.GetProperty("decendios").EnumerateArray().ToList();
        Assert.Equal(esperado.Count, obtido.Count);
        for (int i = 0; i < esperado.Count; i++)
        {
            var (e, o) = (esperado[i], obtido[i]);
            Assert.Equal(e.Decendio.ToString(), o.GetProperty("decendio").GetString());
            Assert.Equal(e.Recomendacao, o.GetProperty("recomendacao").GetString());
            foreach (var (g, v) in e.Risco)
            {
                Assert.Equal(Math.Round(v, 2), o.GetProperty("risco").GetProperty(g).GetDouble());
                Assert.Equal(Modelo.Nivel(v), o.GetProperty("nivel").GetProperty(g).GetString());
            }
        }
    }

    [Fact]
    public async Task Formato_da_resposta_e_padroes()
    {
        var j = await api.Anonimo().GetOk($"/recomendacao?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=01-01&fim=01-31");
        Assert.Equal(["cad", "et0", "criterio", "riscoMax", "fonteChuva", "aguaInicial", "decendios"],
                     j.EnumerateObject().Select(p => p.Name));
        Assert.Equal(30, j.GetProperty("cad").GetDouble());
        Assert.Equal("pm", j.GetProperty("et0").GetString());
        Assert.Equal("isna", j.GetProperty("criterio").GetString());
        Assert.Equal(0.2, j.GetProperty("riscoMax").GetDouble());
        Assert.Equal("power", j.GetProperty("fonteChuva").GetString());  // pasta do IMERG vazia
        Assert.Equal("cheio", j.GetProperty("aguaInicial").GetString()); // sem SMAP
        Assert.Equal(["01/01 a 10/01", "11/01 a 20/01", "21/01 a 31/01"],
                     j.GetProperty("decendios").EnumerateArray().Select(d => d.GetProperty("decendio").GetString()));
        // o clima falso foi feito para ter as três culturas; se isso quebrar, os testes de plano perdem força
        var todas = (await api.Anonimo().GetOk($"/recomendacao?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=01-01&fim=03-31"))
            .GetProperty("decendios").EnumerateArray().Select(d => d.GetProperty("recomendacao").GetString()).Distinct();
        Assert.Equal(["milho", "sorgo", "braquiária"], todas);
    }

    [Fact]
    public async Task Continua_publica_sem_token()
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=01-01&fim=01-10");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("fonteChuva=imerg")]   // sem CSV do IMERG para o ponto
    [InlineData("fonteChuva=chute")]
    [InlineData("aguaInicial=meio")]
    [InlineData("criterio=outro")]
    public async Task Parametro_invalido_da_400(string extra)
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&inicio=01-01&fim=01-10&{extra}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // o POWER é chamado uma vez por coordenada; depois sai do cache estático do Power.cs
    [Fact]
    public async Task Power_chamado_uma_vez_por_ponto()
    {
        var (lat, lon) = (-16.123, -54.321); // ponto que nenhum outro teste usa
        var antes = api.Power.Chamadas;
        for (int i = 0; i < 3; i++)
            await api.Anonimo().GetOk($"/recomendacao?lat={Http.Q(lat)}&lon={Http.Q(lon)}&inicio=01-01&fim=01-10");
        Assert.Equal(antes + 1, api.Power.Chamadas);
    }

    // BUG: lat/lon não são validados. Com o PC em pt-BR, "-15,55" vira -1555 (vírgula lida como separador de milhar)
    // e a conta roda com uma latitude impossível; com o POWER real, vira 500. Deveria ser 400.
    [Theory(Skip = "BUG: /recomendacao (e /clima, /deficit, /plano-colheita) aceitam lat/lon fora do intervalo")]
    [InlineData("lat=-15,55&lon=-55,55")]
    [InlineData("lat=95&lon=0")]
    [InlineData("lat=0&lon=200")]
    public async Task Lat_lon_invalidos_deveriam_dar_400(string q)
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?{q}&inicio=01-01&fim=01-10");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // trava o comportamento atual do bug acima (para perceber quando for corrigido)
    [Fact]
    public async Task Hoje_lat_com_virgula_passa_como_milhar()
    {
        var r = await api.Anonimo().GetAsync("/recomendacao?lat=-15,55&lon=-55,55&inicio=01-01&fim=01-10");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // BUG: inicio/fim são lidos com DateOnly.ParseExact sem tratamento: 29/02 (data real de plantio/colheita
    // em ano bissexto) e qualquer formato errado derrubam a rota com 500. Deveria ser 200 (29/02 = 28/02) ou 400.
    [Theory(Skip = "BUG: /recomendacao com inicio/fim inválido ou 02-29 dá 500")]
    [InlineData("inicio=02-29&fim=02-29", HttpStatusCode.OK)]
    [InlineData("inicio=2-5&fim=03-01", HttpStatusCode.BadRequest)]
    [InlineData("inicio=13-01&fim=03-01", HttpStatusCode.BadRequest)]
    [InlineData("inicio=jan&fim=03-01", HttpStatusCode.BadRequest)]
    public async Task Inicio_fim_invalidos_nao_podem_dar_500(string q, HttpStatusCode esperado)
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?lat={Http.Q(Lat)}&lon={Http.Q(Lon)}&{q}");
        Assert.Equal(esperado, r.StatusCode);
    }
}

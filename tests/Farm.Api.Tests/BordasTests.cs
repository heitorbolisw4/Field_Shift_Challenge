using System.Data.Common;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit.Abstractions;

// bordas das correções: lat/lon (ComCoordenadas), MM-dd (MesDia), fila/arredondamento pela API, JWT de conta apagada
[Collection("api")]
public class BordasTests(ApiFabrica api, ITestOutputHelper saida)
{
    const double Lat = ApiFabrica.Lat, Lon = ApiFabrica.Lon;
    static readonly string Ponto = $"lat={Http.Q(Lat)}&lon={Http.Q(Lon)}";

    // ---------- lat/lon ----------

    [Theory]
    [InlineData("lat=NaN&lon=0")]
    [InlineData("lat=0&lon=NaN")]
    [InlineData("lat=Infinity&lon=0")]
    [InlineData("lat=-Infinity&lon=0")]
    [InlineData("lat=90.0001&lon=0")]
    [InlineData("lat=-90.0001&lon=0")]
    [InlineData("lat=0&lon=180.0001")]
    [InlineData("lat=0&lon=-180.0001")]
    [InlineData("lat=1,5&lon=0")]          // binding lê 15 (separador de milhar); o filtro tem que recusar
    [InlineData("lat=0&lon=1,000")]        // idem: viraria 1000
    [InlineData("lat=1.000,5&lon=0")]
    [InlineData("lat=&lon=0")]
    [InlineData("lat=abc&lon=0")]
    [InlineData("lon=0")]
    [InlineData("lat=1e3&lon=0")]          // notação científica passa no parse, mas 1000 > 90
    public async Task Lat_lon_ruins_dao_400(string q)
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?{q}&inicio=01-01&fim=01-10");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // limites do intervalo são válidos; ponto e notação científica dentro do intervalo também
    [Theory]
    [InlineData("lat=-15.55&lon=-55.55")]
    [InlineData("lat=-1.555e1&lon=-55.55")]
    [InlineData("lat=0&lon=180")]
    [InlineData("lat=0&lon=-180")]
    [InlineData("lat=-0&lon=0")]
    [InlineData("lat=90&lon=0")]
    [InlineData("lat=-90&lon=0")]
    public async Task Lat_lon_validos_passam(string q)
    {
        var r = await api.Anonimo().GetAsync($"/recomendacao?{q}&inicio=01-01&fim=01-10");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // lat=±90 passa no filtro; no polo o Acos do Ra saía de [-1, 1] e a ET0 virava NaN (JSON não serializa NaN -> 500)
    [Theory]
    [InlineData("/clima?lat=90&lon=0")]
    [InlineData("/clima?lat=-90&lon=0")]
    public async Task Polo_nao_pode_dar_500(string url)
    {
        var r = await api.Anonimo().GetAsync(url);
        Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"{(int)r.StatusCode}");
    }

    // ---------- MM-dd ----------

    [Theory]
    [InlineData("02-30")]
    [InlineData("02-31")]
    [InlineData("04-31")]
    [InlineData("06-31")]
    [InlineData("09-31")]
    [InlineData("11-31")]
    [InlineData("00-10")]
    [InlineData("01-00")]
    [InlineData("01-32")]
    [InlineData("13-01")]
    [InlineData("1-01")]
    [InlineData("01-1")]
    [InlineData("0101")]
    [InlineData("01/01")]
    [InlineData("01.01")]
    [InlineData("2026-01-01")]
    [InlineData("-01-01")]
    [InlineData("01-01-")]
    [InlineData("%2001-01")]   // espaço antes
    [InlineData("01-01%20")]   // espaço depois
    [InlineData("")]
    [InlineData("--")]
    [InlineData("jan-01")]
    public async Task MesDia_invalido_da_400_no_inicio_e_no_fim(string md)
    {
        var c = api.Anonimo();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/recomendacao?{Ponto}&inicio={md}&fim=03-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/recomendacao?{Ponto}&inicio=01-01&fim={md}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/deficit?{Ponto}&plantio={md}&cultura=milho")).StatusCode);
    }

    // 29/02 vira 28/02: a resposta tem que ser idêntica
    [Fact]
    public async Task Dia_29_de_fevereiro_igual_a_28()
    {
        var c = api.Anonimo();
        var a = await c.GetStringAsync($"/recomendacao?{Ponto}&inicio=02-29&fim=03-10");
        var b = await c.GetStringAsync($"/recomendacao?{Ponto}&inicio=02-28&fim=03-10");
        Assert.Equal(b, a);
        // como fim: janela 02-01 a 02-29 = 02-01 a 02-28
        Assert.Equal(await c.GetStringAsync($"/recomendacao?{Ponto}&inicio=02-01&fim=02-28"),
                     await c.GetStringAsync($"/recomendacao?{Ponto}&inicio=02-01&fim=02-29"));
        // /deficit com plantio 29/02 não pode dar 500 em ano normal
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/deficit?{Ponto}&plantio=02-29&cultura=milho")).StatusCode);
    }

    // 1 dia só e janela que cruza o ano com 29/02 no meio
    [Theory]
    [InlineData("12-31", "12-31")]
    [InlineData("01-01", "01-01")]
    [InlineData("12-21", "01-10")]
    [InlineData("02-29", "02-28")]  // fim = início depois do ajuste: um decêndio só
    public async Task Janelas_de_borda_dao_200(string ini, string fim)
    {
        var j = await api.Anonimo().GetOk($"/recomendacao?{Ponto}&inicio={ini}&fim={fim}");
        Assert.NotEmpty(j.GetProperty("decendios").EnumerateArray());
    }

    // ---------- plano: capacidade e arredondamento pela API ----------

    async Task<(HttpClient C, int F)> Fazenda(double uteis, double haDia, params double[] areas)
    {
        var c = await api.Cliente();
        var f = (await c.PostOk("/fazendas", new { nome = "F", lat = Lat, lon = Lon, diasUteisPct = uteis })).Id();
        await c.PostOk($"/fazendas/{f}/colhedoras", new { nome = "C", haDia });
        for (int i = 0; i < areas.Length; i++) await c.PostOk($"/fazendas/{f}/talhoes", new { nome = $"T{i}", areaHa = areas[i] });
        return (c, f);
    }

    static long Cent(double x) => (long)Math.Round(x * 100, MidpointRounding.AwayFromZero);

    [Fact]
    public async Task Muitos_talhoes_fracionarios_cada_um_fecha_na_area()
    {
        var rnd = new Random(42);
        var areas = Enumerable.Range(0, 30).Select(_ => Math.Round(0.01 + rnd.NextDouble() * 15, 2)).ToArray();
        var (c, f) = await Fazenda(33, 10, areas); // 3,3 ha/dia
        var p = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");

        var talhoes = p.GetProperty("talhoes").EnumerateArray().ToList();
        Assert.Equal(areas.Length, talhoes.Count);
        foreach (var (t, a) in talhoes.Zip(areas))
            Assert.Equal(Cent(a), t.GetProperty("trechos").EnumerateArray().Sum(x => Cent(x.GetProperty("ha").GetDouble())));
        Assert.Equal(areas.Sum(Cent), p.GetProperty("totais").EnumerateArray().Sum(x => Cent(x.GetProperty("ha").GetDouble())));

        // contínuo: próximo talhão começa no mesmo dia ou no seguinte
        for (int i = 1; i < talhoes.Count; i++)
            Assert.InRange(talhoes[i].Data("de").DayNumber - talhoes[i - 1].Data("ate").DayNumber, 0, 1);
    }

    [Fact]
    public async Task Talhao_que_fecha_o_dia_exato()
    {
        var (c, f) = await Fazenda(100, 50, 100, 50);
        var t = (await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05")).GetProperty("talhoes").EnumerateArray().ToList();
        Assert.Equal(new DateOnly(2026, 1, 6), t[0].Data("ate"));
        Assert.Equal(new DateOnly(2026, 1, 7), t[1].Data("de"));
    }

    // colhedora com haDia minúsculo passa na validação (> 0); o plano tem que barrar antes da Fila estourar AddDays
    [Fact]
    public async Task Capacidade_minuscula_da_400_no_plano_da_fazenda()
    {
        var (c, f) = await Fazenda(70, 1e-9, 10);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/fazendas/{f}/plano-colheita?inicio=2026-01-05")).StatusCode);
    }

    [Fact]
    public async Task Capacidade_minuscula_da_400_sem_cadastro()
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio=2026-01-05&area=100&haDia=1e-9");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // haDia e uteis negativos se cancelariam (capacidade positiva); uteis > 100 e area <= 0 também não valem
    [Theory]
    [InlineData("&haDia=-60&uteis=-70")]
    [InlineData("&uteis=500")]
    [InlineData("&colhedoras=-1&haDia=-60")]
    [InlineData("&uteis=-1")]
    [InlineData("&haDia=0")]
    public async Task Parametros_negativos_ou_acima_de_100_dao_400(string q)
    {
        var r = await api.Anonimo().GetAsync($"/plano-colheita?{Ponto}&inicio=2026-01-05&area=100{q}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    // mesmo talhão, mesma capacidade: sem cadastro tem que dar os mesmos dias que a fazenda
    [Fact]
    public async Task Sem_cadastro_e_fazenda_concordam_com_capacidade_fracionaria()
    {
        var (c, f) = await Fazenda(33, 10, 9.9);
        var fz = await c.GetOk($"/fazendas/{f}/plano-colheita?inicio=2026-01-05");
        var s = await c.GetOk($"/plano-colheita?{Ponto}&inicio=2026-01-05&area=9.9&haDia=10&uteis=33");
        Assert.Equal(fz.Data("fim"), s.Data("fim"));
    }

    // ---------- JWT: OnTokenValidated ----------

    static string Emitir(params Claim[] claims)
    {
        var exp = DateTime.UtcNow.AddMinutes(10);
        var jwt = new JwtSecurityToken("farm-api", "farm-app", claims, notBefore: exp.AddHours(-1), expires: exp,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFabrica.ChaveJwt)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Fact]
    public async Task Jwt_assinado_de_usuario_que_nunca_existiu_da_401()
    {
        var c = ApiFabrica.ComoDono(api.Anonimo(), Emitir(new Claim("sub", Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/fazendas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/auth/eu")).StatusCode);
    }

    [Fact]
    public async Task Jwt_assinado_sem_sub_da_401()
    {
        var c = ApiFabrica.ComoDono(api.Anonimo(), Emitir(new Claim("nome", "x")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/fazendas")).StatusCode);
        var vazio = ApiFabrica.ComoDono(api.Anonimo(), Emitir(new Claim("sub", "")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await vazio.GetAsync("/fazendas")).StatusCode);
    }

    // conta apagada: o refresh também não pode trazer a conta de volta, e rota pública segue respondendo
    [Fact]
    public async Task Conta_apagada_refresh_401_e_rota_publica_200()
    {
        var t = await api.Anonimo().PostOk("/auth/registrar", new { email = $"u{Guid.NewGuid():N}@teste.dev", senha = "senha-boa-123" });
        var id = t.GetProperty("usuario").GetProperty("id").GetString()!;
        using (var escopo = api.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<FarmDb>().Usuarios.Where(u => u.Id == id).ExecuteDeleteAsync();

        var r = await api.Anonimo().PostAsJsonAsync("/auth/renovar", new { refreshToken = t.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);

        var c = ApiFabrica.ComoDono(api.Anonimo(), t.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/solos")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/recomendacao?{Ponto}&inicio=01-01&fim=01-10")).StatusCode);
    }

    // ---------- custo do OnTokenValidated ----------

    sealed class Contador : DbCommandInterceptor
    {
        int n;
        public int N => Volatile.Read(ref n);
        public void Zerar() => Interlocked.Exchange(ref n, 0);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default)
        { Interlocked.Increment(ref n); return base.ReaderExecutingAsync(c, e, r, ct); }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default)
        { Interlocked.Increment(ref n); return base.ScalarExecutingAsync(c, e, r, ct); }
    }

    // documenta o custo: TODA requisição com Bearer faz 1 consulta ao banco, inclusive rota pública e arquivo estático.
    // Sem token: zero. Se isso pesar, a saída é cache curto por sub (ou só checar em rotas com RequireAuthorization).
    [Fact]
    public async Task Bearer_custa_uma_consulta_por_requisicao_em_qualquer_rota()
    {
        var contador = new Contador();
        using var fab = api.WithWebHostBuilder(b => b.ConfigureServices(s => s.ConfigureDbContext<FarmDb>(o => o.AddInterceptors(contador))));
        var anon = fab.CreateClient();
        anon.DefaultRequestHeaders.Add(ApiFabrica.HeaderIp, ApiFabrica.IpNovo());
        var t = await anon.PostOk("/auth/registrar", new { email = $"u{Guid.NewGuid():N}@teste.dev", senha = "senha-boa-123" });
        var logado = ApiFabrica.ComoDono(fab.CreateClient(), t.GetProperty("accessToken").GetString()!);

        async Task<int> Consultas(HttpClient c, string url)
        {
            contador.Zerar();
            var r = await c.GetAsync(url);
            Assert.True(r.IsSuccessStatusCode, $"{url} -> {(int)r.StatusCode}");
            return contador.N;
        }

        Assert.Equal(0, await Consultas(anon, "/solos"));
        Assert.Equal(1, await Consultas(logado, "/solos"));              // rota pública: paga a consulta à toa
        Assert.Equal(0, await Consultas(anon, "/"));
        Assert.Equal(1, await Consultas(logado, "/"));                   // index.html também
        Assert.Equal(2, await Consultas(logado, "/fazendas"));           // 1 do token + 1 da rota

        // medida grosseira (não é assert): latência média com e sem token, SQLite local
        const int n = 200;
        double Media(Func<Task> f) { var sw = Stopwatch.StartNew(); for (int i = 0; i < n; i++) f().GetAwaiter().GetResult(); return sw.Elapsed.TotalMilliseconds / n; }
        await anon.GetAsync("/solos"); await logado.GetAsync("/solos"); // aquece
        var sem = Media(() => anon.GetAsync("/solos"));
        var com = Media(() => logado.GetAsync("/solos"));
        saida.WriteLine($"/solos: sem token {sem:F3} ms, com token {com:F3} ms (+{com - sem:F3} ms: validação JWT + 1 SELECT)");
    }
}

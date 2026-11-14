using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// sobe a API inteira (Program.cs) com:
//  - SQLite num arquivo temporário (nunca o data/farm.db)
//  - IHttpClientFactory falso: o POWER responde com ClimaFalso, qualquer outro host explode
//  - Imerg.Pasta/Smap.Pasta numa pasta vazia: "auto" cai em power/cheio
//  - Jwt:Chave própria dos testes (não depende do appsettings.Development.json)
//  - IP do cliente vindo do header X-Teste-IP: o rate limit de /auth é por IP e no TestServer todo mundo seria o mesmo IP
// WebApplicationFactory<FarmDb>: FarmDb é público e mora no assembly da API, então não precisa mexer no Program.
public sealed class ApiFabrica : WebApplicationFactory<FarmDb>
{
    public readonly string Pasta = Path.Combine(Path.GetTempPath(), "farm-api-tests-" + Guid.NewGuid().ToString("N"));
    public string Banco => Path.Combine(Pasta, "farm.db");
    public readonly PowerFalso Power = new();
    public const string ChaveJwt = "chave-dos-testes-de-integracao-0123456789-abcdef";
    public const string HeaderIp = "X-Teste-IP";

    // coordenada sem CSV do IMERG/SMAP no repositório (Mato Grosso), por garantia mesmo se a pasta real vazar
    public const double Lat = -15.55, Lon = -55.55;

    public ApiFabrica()
    {
        Directory.CreateDirectory(Path.Combine(Pasta, "imerg"));
        Directory.CreateDirectory(Path.Combine(Pasta, "smap"));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Farm", $"Data Source={Banco}");
        builder.UseSetting("Jwt:Chave", ChaveJwt);
        builder.ConfigureLogging(l => l.ClearProviders()); // sem o SQL do EF na saída
        builder.ConfigureServices(s =>
        {
            s.AddSingleton<IHttpClientFactory>(new FabricaHttp(Power));
            s.AddSingleton<IStartupFilter, IpDoTeste>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder); // aqui o Program.cs já rodou e apontou as pastas para data/
        Estatico("Imerg", "Pasta", Path.Combine(Pasta, "imerg"));
        Estatico("Smap", "Pasta", Path.Combine(Pasta, "smap"));
        LimparCachePower();
        return host;
    }

    static Type Tipo(string nome) => typeof(FarmDb).Assembly.GetType(nome) ?? throw new InvalidOperationException($"tipo {nome} sumiu");

    static void Estatico(string tipo, string campo, object valor) =>
        Tipo(tipo).GetField(campo, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, valor);

    // Power.cs guarda um cache estático por (lat, lon) que vive o processo inteiro: zera antes de começar
    public static void LimparCachePower() =>
        ((IDictionary)Tipo("Power").GetField("cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Clear();

    // ---- identidade ----

    static int ultimoIp;
    // cada chamador ganha um IP próprio, então só o teste de rate limit esbarra no limite de 10/min
    public static string IpNovo() { var n = Interlocked.Increment(ref ultimoIp); return $"10.{n >> 16 & 255}.{n >> 8 & 255}.{n & 255}"; }

    // cliente anônimo (sem token), com IP próprio
    public HttpClient Anonimo(string? ip = null)
    {
        var c = CreateClient();
        c.DefaultRequestHeaders.Add(HeaderIp, ip ?? IpNovo());
        return c;
    }

    // cria uma conta nova e devolve um cliente logado nela (Bearer)
    public async Task<HttpClient> Cliente()
    {
        var c = Anonimo();
        var tokens = await c.PostOk("/auth/registrar", new { email = $"u{Guid.NewGuid():N}@teste.dev", senha = "senha-boa-123" });
        return ComoDono(c, tokens.GetProperty("accessToken").GetString()!);
    }

    // ÚNICO ponto que sabe como a API identifica o dono
    public static HttpClient ComoDono(HttpClient c, string accessToken)
    {
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return c;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Pasta, true); } catch (IOException) { }
    }

    sealed class FabricaHttp(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    // roda antes de todo o pipeline do Program.cs: troca RemoteIpAddress pelo header do teste
    sealed class IpDoTeste : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, prox) =>
            {
                if (IPAddress.TryParse(ctx.Request.Headers[HeaderIp].FirstOrDefault(), out var ip)) ctx.Connection.RemoteIpAddress = ip;
                return prox(ctx);
            });
            next(app);
        };
    }
}

// uma fábrica só para todos os testes: Imerg.Pasta/Smap.Pasta são estáticos e o Program.cs os sobrescreve a cada host.
// Cada teste usa uma conta nova, então o banco compartilhado não mistura dados.
[CollectionDefinition("api")]
public class ApiColecao : ICollectionFixture<ApiFabrica>;

public static class Http
{
    public static async Task<JsonElement> Json(this HttpResponseMessage r) =>
        await r.Content.ReadFromJsonAsync<JsonElement>();

    public static async Task<JsonElement> PostOk(this HttpClient c, string url, object corpo)
    {
        var r = await c.PostAsJsonAsync(url, corpo);
        Assert.True(r.IsSuccessStatusCode, $"POST {url} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Json();
    }

    public static async Task<JsonElement> GetOk(this HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.True(r.IsSuccessStatusCode, $"GET {url} -> {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Json();
    }

    public static int Id(this JsonElement e) => e.GetProperty("id").GetInt32();

    // número na query string sempre com ponto (o PC está em pt-BR)
    public static string Q(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

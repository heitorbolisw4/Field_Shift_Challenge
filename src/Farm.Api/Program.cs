using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using static Fontes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient(); // registra a fábrica de HttpClient

// documento em /openapi/v1.json; tela de teste em /docs (botão de token Bearer)
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "JWT de /auth/login" };
    doc.Security ??= [];
    doc.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
    return Task.CompletedTask;
}));

// login: JWT HS256. A chave vem de Jwt:Chave (dev: appsettings.Development.json; produção: variável Jwt__Chave)
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOpcoes>() ?? new JwtOpcoes();
var chave = jwt.ChaveAssinatura(); // falha já na subida se a chave não existir ou for curta
builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false; // mantém "sub" como "sub"
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Emissor, ValidAudience = jwt.Publico, IssuerSigningKey = chave,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromMinutes(1),
    };
    // conta apagada: o JWT ainda é válido até expirar, então confere se o usuário existe a cada requisição
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            var sub = ctx.Principal?.FindFirst("sub")?.Value;
            var db = ctx.HttpContext.RequestServices.GetRequiredService<FarmDb>();
            if (sub is null || !await db.Usuarios.AnyAsync(u => u.Id == sub)) ctx.Fail("usuário não existe mais");
        }
    };
});
builder.Services.AddAuthorization();

// /auth: 10 tentativas por minuto por IP (atrás de proxy na VPS, precisa de ForwardedHeaders para ver o IP real)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// banco da fazenda: SQLite em data/farm.db (ConnectionStrings:Farm no appsettings troca o caminho)
var raiz = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."));
builder.Services.AddDbContext<FarmDb>(o => o.UseSqlite(
    builder.Configuration.GetConnectionString("Farm") ?? $"Data Source={Path.Combine(raiz, "data", "farm.db")}"));

var app = builder.Build();

using (var escopo = app.Services.CreateScope())
    escopo.ServiceProvider.GetRequiredService<FarmDb>().Database.Migrate(); // cria/atualiza as tabelas

app.MapOpenApi();
app.MapScalarApiReference("/docs");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// o front fica em <raiz>/web; a API serve esses arquivos na mesma porta (sem CORS)
var web = new PhysicalFileProvider(Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "web")));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = web });  // "/" abre web/index.html
app.UseStaticFiles(new StaticFileOptions { FileProvider = web });

Imerg.Pasta = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "imerg"));
Smap.Pasta = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "smap"));

// passo 4: média por mês, para checar se os dados fazem sentido
app.MapGet("/clima", async (double lat, double lon, IHttpClientFactory http, string fonteChuva = "auto") =>
{
    (var validos, fonteChuva) = await Dados(http, lat, lon, fonteChuva);
    if (validos is null) return SemImerg(lat, lon, fonteChuva);

    var porMes = validos
        .GroupBy(d => d.Data.Month)
        .OrderBy(g => g.Key)
        .Select(g => new {
            mes = g.Key,
            et0Hs = Math.Round(g.Average(d => Clima.Et0(d, lat)), 2),    // Hargreaves, mm/dia
            et0Pm = Math.Round(g.Average(d => Clima.Et0Pm(d, lat)), 2),  // Penman-Monteith, mm/dia
            chuvaMes = Math.Round(g.Sum(d => d.Chuva) / 10, 0)          // mm/mês, média dos anos
        });

    return Results.Ok(new { fonteChuva, validos = validos.Count, porMes });
}).ComCoordenadas();

// cad padrão 30: calibrado contra o ZARC com chuva do IMERG
// passo 5/6: déficit de UMA cultura plantada numa data, ano a ano (com balde de solo)
// ex.: /deficit?lat=-19.03&lon=-47.92&plantio=02-15&cultura=milho&cad=30
app.MapGet("/deficit", async (double lat, double lon, string plantio, string cultura, IHttpClientFactory http, double cad = 30, double fatorChuva = 1, string et0 = "pm", string fonteChuva = "auto", string aguaInicial = "auto") =>
{
    if (Agua(aguaInicial, lat, lon) is not { } agua) return Results.BadRequest(AguaErro);
    aguaInicial = agua;
    if (!Culturas.Todas.TryGetValue(cultura, out var c))
        return Results.BadRequest($"cultura desconhecida: {cultura}");

    if (MesDia(plantio) is not { } md) return Results.BadRequest("plantio no formato MM-dd (ex.: 02-15)"); // "02-15" vira mês/dia
    (var lista, fonteChuva) = await Dados(http, lat, lon, fonteChuva);
    if (lista is null) return SemImerg(lat, lon, fonteChuva);
    var dias = lista.ToDictionary(d => d.Data);

    var anos = new List<object>();
    foreach (var ano in dias.Keys.Select(d => d.Year).Distinct().Order())
    {
        var ini = new DateOnly(ano, md.Month, Math.Min(md.Day, DateTime.DaysInMonth(ano, md.Month))); // 29/02 em ano normal
        var s = Modelo.Simular(c, dias, ini, lat, cad, fatorChuva, et0 == "pm", AguaIni(aguaInicial, lat, lon, ini));
        if (s is null) continue; // ciclo passou do fim dos dados

        anos.Add(new { ano, etc = Math.Round(s.Etc), chuva = Math.Round(s.Chuva),
                       deficit = Math.Round(s.Deficit), s.DiasSede,
                       perda = Math.Round(Modelo.Perda(c, s), 2),
                       isnaI = Math.Round(s.IsnaI, 2), isnaIII = Math.Round(s.IsnaIII, 2),
                       passaZarc = Modelo.Passa(c, s, "isna") });
    }

    return Results.Ok(new { cultura = c.Nome, plantio, ciclo = c.Ciclo, cad, fatorChuva, et0, fonteChuva, aguaInicial, anos });
}).ComCoordenadas();

// passo 10: calendário por DECÊNDIO (blocos de 10 dias, como o ZARC), com nível de risco
// ex.: /recomendacao?lat=-19.03&lon=-47.92&inicio=01-20&fim=03-20
app.MapGet("/recomendacao", async (double lat, double lon, string inicio, string fim, IHttpClientFactory http,
                                   double cad = 30, double fatorChuva = 1, string et0 = "pm",
                                   string criterio = "isna", double riscoMax = 0.2, string fonteChuva = "auto",
                                   string aguaInicial = "auto") =>
{
    if ((MesDia(inicio), MesDia(fim)) is not (DateOnly di, DateOnly df)) return Results.BadRequest("inicio e fim no formato MM-dd (ex.: 02-15)");

    var (cal, erro) = await CalendarioAsync(http, lat, lon, di, df, cad, riscoMax, fonteChuva, aguaInicial, criterio, fatorChuva, et0);
    if (cal is null) return erro!;

    var decendios = cal.Decendios.Select(r => new {
        decendio = r.Decendio.ToString(),
        risco = r.Risco.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)),
        nivel = r.Risco.ToDictionary(kv => kv.Key, kv => Modelo.Nivel(kv.Value)),
        recomendacao = r.Recomendacao
    });

    return Results.Ok(new { cad, et0, criterio, riscoMax, fonteChuva = cal.FonteChuva, aguaInicial = cal.AguaInicial, decendios });
}).ComCoordenadas();

app.MapColheita();
app.MapAuth();
app.MapFazenda();

app.Run();

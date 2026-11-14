using System.Globalization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient(); // registra a fábrica de HttpClient
var app = builder.Build();

// passo 4: média por mês, para checar se os dados fazem sentido
app.MapGet("/clima", async (double lat, double lon, IHttpClientFactory http) =>
{
    var validos = await Clima.BaixarAsync(http.CreateClient(), lat, lon);

    var porMes = validos
        .GroupBy(d => d.Data.Month)
        .OrderBy(g => g.Key)
        .Select(g => new {
            mes = g.Key,
            et0Hs = Math.Round(g.Average(d => Clima.Et0(d, lat)), 2),    // Hargreaves, mm/dia
            et0Pm = Math.Round(g.Average(d => Clima.Et0Pm(d, lat)), 2),  // Penman-Monteith, mm/dia
            chuvaMes = Math.Round(g.Sum(d => d.Chuva) / 10, 0)          // mm/mês, média dos anos
        });

    return Results.Ok(new { validos = validos.Count, porMes });
});

// passo 5/6: déficit de UMA cultura plantada numa data, ano a ano (com balde de solo)
// ex.: /deficit?lat=-19.03&lon=-47.92&plantio=02-15&cultura=milho&cad=70
app.MapGet("/deficit", async (double lat, double lon, string plantio, string cultura, IHttpClientFactory http, double cad = 70, double fatorChuva = 1, string et0 = "pm") =>
{
    if (!Culturas.Todas.TryGetValue(cultura, out var c))
        return Results.BadRequest($"cultura desconhecida: {cultura}");

    var md = DateOnly.ParseExact($"2000-{plantio}", "yyyy-MM-dd", CultureInfo.InvariantCulture); // "02-15" vira mês/dia
    var dias = (await Clima.BaixarAsync(http.CreateClient(), lat, lon)).ToDictionary(d => d.Data);

    var anos = new List<object>();
    foreach (var ano in dias.Keys.Select(d => d.Year).Distinct().Order())
    {
        var ini = new DateOnly(ano, md.Month, Math.Min(md.Day, DateTime.DaysInMonth(ano, md.Month))); // 29/02 em ano normal
        var s = Modelo.Simular(c, dias, ini, lat, cad, fatorChuva, et0 == "pm");
        if (s is null) continue; // ciclo passou do fim dos dados

        anos.Add(new { ano, etc = Math.Round(s.Etc), chuva = Math.Round(s.Chuva),
                       deficit = Math.Round(s.Deficit), s.DiasSede,
                       perda = Math.Round(Modelo.Perda(c, s), 2),
                       isnaI = Math.Round(s.IsnaI, 2), isnaIII = Math.Round(s.IsnaIII, 2),
                       passaZarc = Modelo.Passa(c, s, "isna") });
    }

    return Results.Ok(new { cultura = c.Nome, plantio, ciclo = c.Ciclo, cad, fatorChuva, et0, anos });
});

// passo 10: calendário por DECÊNDIO (blocos de 10 dias, como o ZARC), com nível de risco
// ex.: /recomendacao?lat=-19.03&lon=-47.92&inicio=01-20&fim=03-20
app.MapGet("/recomendacao", async (double lat, double lon, string inicio, string fim, IHttpClientFactory http,
                                   double cad = 70, double fatorChuva = 1, string et0 = "pm",
                                   string criterio = "isna", double riscoMax = 0.2) =>
{
    var inv = CultureInfo.InvariantCulture;
    var di = DateOnly.ParseExact($"2001-{inicio}", "yyyy-MM-dd", inv); // 2001: ano qualquer, não bissexto
    var df = DateOnly.ParseExact($"2001-{fim}", "yyyy-MM-dd", inv);
    if (df < di) return Results.BadRequest("fim antes do início (janela cruzando o ano não é suportada)");

    var dias = (await Clima.BaixarAsync(http.CreateClient(), lat, lon)).ToDictionary(d => d.Data);
    var anos = dias.Keys.Select(d => d.Year).Distinct().Order().ToList();

    var decendios = new List<object>();
    for (var d = di; d <= df; )
    {
        // decêndio: dias 1–10, 11–20 ou 21–fim do mês
        var ini = new DateOnly(2001, d.Month, d.Day <= 10 ? 1 : d.Day <= 20 ? 11 : 21);
        var fimDec = d.Day <= 20 ? ini.AddDays(9) : new DateOnly(2001, d.Month, DateTime.DaysInMonth(2001, d.Month));

        // risco = fração de (dia de plantio × ano) em que a cultura falha no critério
        var risco = new Dictionary<string, double>();
        foreach (var c in Culturas.Graos)
        {
            int total = 0, falhas = 0;
            for (var p = ini; p <= fimDec; p = p.AddDays(1))
                foreach (var a in anos)
                {
                    var s = Modelo.Simular(c, dias, new DateOnly(a, p.Month, p.Day), lat, cad, fatorChuva, et0 == "pm");
                    if (s is null) continue;                 // ciclo passou do fim dos dados
                    total++;
                    if (!Modelo.Passa(c, s, criterio)) falhas++;
                }
            risco[c.Nome] = total == 0 ? 1 : (double)falhas / total;
        }

        // a cultura mais exigente cujo risco cabe no limite; senão, braquiária
        var escolha = Culturas.Graos.FirstOrDefault(c => risco[c.Nome] <= riscoMax)?.Nome ?? "braquiária";

        decendios.Add(new {
            decendio = $"{ini.ToString("dd/MM", inv)} a {fimDec.ToString("dd/MM", inv)}",
            risco = risco.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)),
            nivel = risco.ToDictionary(kv => kv.Key, kv => Modelo.Nivel(kv.Value)),
            recomendacao = escolha
        });

        d = fimDec.AddDays(1);
    }

    return Results.Ok(new { cad, et0, criterio, riscoMax, decendios });
});

app.Run();

// só os campos que interessam do JSON do POWER
record PowerResponse(PowerProps Properties);
record PowerProps(Dictionary<string, Dictionary<string, double>> Parameter);

// Rad: radiação solar (MJ/m²/dia), UR: umidade relativa (%), Vento: m/s a 2 m, Pressao: kPa
record Dia(DateOnly Data, double TMax, double TMin, double Chuva,
           double Rad, double UR, double Vento, double Pressao);

// curva de Kc da FAO-56: 4 fases (inicial, desenvolvimento, média, final), em dias
// Ky: quanto a produção cai por unidade de falta de água (FAO-33). Ky > 1 = sensível.
record Cultura(string Nome, int Ini, int Des, int Med, int Fim, double KcIni, double KcMed, double KcFim, double Ky = 0)
{
    public int Ciclo => Ini + Des + Med + Fim;

    // fase do dia n: 1 inicial, 2 desenvolvimento, 3 média (floração/enchimento), 4 final
    public int Fase(int n) => n < Ini ? 1 : n < Ini + Des ? 2 : n < Ini + Des + Med ? 3 : 4;

    public double Kc(int n)
    {
        if (n < Ini) return KcIni;                                    // fase inicial: Kc fixo baixo
        n -= Ini;
        if (n < Des) return KcIni + (KcMed - KcIni) * n / Des;        // desenvolvimento: sobe em rampa
        n -= Des;
        if (n < Med) return KcMed;                                    // fase média: Kc máximo
        n -= Med;
        return KcMed + (KcFim - KcMed) * n / Fim;                     // final: desce em rampa
    }
}

static class Culturas
{
    // CHUTE INICIAL baseado na FAO-56 (tabelas 11 e 12). Conferir e ajustar para safrinha no Cerrado.
    public static readonly Dictionary<string, Cultura> Todas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["milho"]      = new("milho",      20, 35, 40, 30, 0.30, 1.20, 0.35, Ky: 1.25),
        ["sorgo"]      = new("sorgo",      15, 30, 40, 25, 0.30, 1.00, 0.55, Ky: 0.90),
        ["braquiaria"] = new("braquiária", 10, 20, 60, 30, 0.40, 0.85, 0.85),
    };

    // culturas de grão, da mais exigente para a menos; braquiária é o plano B
    public static readonly Cultura[] Graos = [Todas["milho"], Todas["sorgo"]];
}

static class Clima
{
    // baixa os últimos 10 anos completos do POWER e descarta os dias com -999
    public static async Task<List<Dia>> BaixarAsync(HttpClient http, double lat, double lon)
    {
        var inv = CultureInfo.InvariantCulture;           // força ponto decimal
        var ultimoAno = DateTime.Today.Year - 1;          // só anos completos
        var url = "https://power.larc.nasa.gov/api/temporal/daily/point" +
                  "?parameters=T2M_MAX,T2M_MIN,PRECTOTCORR,ALLSKY_SFC_SW_DWN,RH2M,WS2M,PS&community=AG" +
                  $"&latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                  $"&start={ultimoAno - 9}0101&end={ultimoAno}1231&format=JSON";

        var resp = await http.GetFromJsonAsync<PowerResponse>(url);
        var p = resp!.Properties.Parameter;

        return p["T2M_MAX"].Keys
            .Select(k => new Dia(
                DateOnly.ParseExact(k, "yyyyMMdd", inv),
                p["T2M_MAX"][k], p["T2M_MIN"][k], p["PRECTOTCORR"][k],
                p["ALLSKY_SFC_SW_DWN"][k], p["RH2M"][k], p["WS2M"][k], p["PS"][k]))
            .Where(d => d.TMax != -999 && d.TMin != -999 && d.Chuva != -999 &&
                        d.Rad != -999 && d.UR != -999 && d.Vento != -999 && d.Pressao != -999) // fill_value do POWER
            .OrderBy(d => d.Data)
            .ToList();
    }

    // Ra: radiação no topo da atmosfera (MJ/m²/dia). FAO-56, equações 21 a 25.
    public static double Ra(DateOnly data, double latGraus)
    {
        double J = data.DayOfYear;
        double phi = latGraus * Math.PI / 180;                         // latitude em radianos
        double dr = 1 + 0.033 * Math.Cos(2 * Math.PI * J / 365);       // distância Terra-Sol
        double delta = 0.409 * Math.Sin(2 * Math.PI * J / 365 - 1.39); // declinação solar
        double ws = Math.Acos(-Math.Tan(phi) * Math.Tan(delta));       // ângulo do pôr do sol
        return 24 * 60 / Math.PI * 0.0820 * dr *
               (ws * Math.Sin(phi) * Math.Sin(delta) + Math.Cos(phi) * Math.Cos(delta) * Math.Sin(ws));
    }

    // e°(T): pressão de vapor de saturação (kPa). FAO-56, equação 11.
    static double Esat(double t) => 0.6108 * Math.Exp(17.27 * t / (t + 237.3));

    // ET0 por Penman-Monteith (mm/dia). FAO-56, equação 6. É o método padrão da FAO.
    public static double Et0Pm(Dia d, double latGraus)
    {
        double t = (d.TMax + d.TMin) / 2;
        double delta = 4098 * Esat(t) / Math.Pow(t + 237.3, 2);       // inclinação da curva de vapor (eq. 13)
        double gama = 0.000665 * d.Pressao;                           // constante psicrométrica (eq. 8)
        double es = (Esat(d.TMax) + Esat(d.TMin)) / 2;                // vapor de saturação (eq. 12)
        double ea = es * d.UR / 100;                                  // vapor real, pela UR média (eq. 19)

        // saldo de radiação (eq. 37 a 40)
        double z = (293 - 293 * Math.Pow(d.Pressao / 101.3, 1 / 5.26)) / 0.0065; // altitude a partir da pressão
        double rso = (0.75 + 2e-5 * z) * Ra(d.Data, latGraus);       // radiação de céu limpo
        double rsRso = Math.Min(d.Rad / rso, 1.0);
        double rns = 0.77 * d.Rad;                                    // onda curta líquida (albedo 0,23)
        double rnl = 4.903e-9 * (Math.Pow(d.TMax + 273.16, 4) + Math.Pow(d.TMin + 273.16, 4)) / 2
                     * (0.34 - 0.14 * Math.Sqrt(ea)) * (1.35 * rsRso - 0.35); // onda longa perdida
        double rn = rns - rnl;                                        // G (fluxo do solo) ~ 0 no diário

        return (0.408 * delta * rn + gama * 900 / (t + 273) * d.Vento * (es - ea))
               / (delta + gama * (1 + 0.34 * d.Vento));
    }

    // ET0 por Hargreaves (mm/dia). FAO-56, equação 52. 0.408 converte MJ/m² em mm.
    public static double Et0(Dia d, double latGraus)
    {
        double tMedia = (d.TMax + d.TMin) / 2;
        return 0.0023 * (tMedia + 17.8) * Math.Sqrt(d.TMax - d.TMin) * 0.408 * Ra(d.Data, latGraus);
    }
}

// IsnaI / IsnaIII: água consumida ÷ água desejada nas fases I e III (índice do ZARC)
record Safra(double Etc, double Chuva, double Deficit, int DiasSede, double IsnaI, double IsnaIII);

static class Modelo
{
    public const double PerdaMax = 0.20; // CHUTE: aceita perder até 20% da produção...
    public const double AnosOk = 0.80;   // ...em pelo menos 8 de 10 anos (lógica do ZARC)

    // ZARC milho 2ª safra MG 2025/26 (Portaria SPA/MAPA 388/2025): ISNA >= 0,6 na fase I e >= 0,5 na fase III
    public const double IsnaMinI = 0.6;
    public const double IsnaMinIII = 0.5;

    // p: fração da CAD que a planta usa sem sofrer. FAO-56 tabela 22: milho e sorgo ~0,55
    public const double P = 0.55;

    // balanço hídrico dia a dia com balde de solo; null se faltar dado no ciclo
    public static Safra? Simular(Cultura c, Dictionary<DateOnly, Dia> dias, DateOnly ini, double lat, double cad, double fatorChuva = 1, bool usarPm = false)
    {
        double etc = 0, chuva = 0, deficit = 0;
        double agua = cad;   // SUPOSIÇÃO: solo cheio no plantio (fim das águas). v2: GLDAS/SMAP
        int diasSede = 0;
        var etm = new double[5];   // água desejada por fase (índice 1 a 4)
        var etr = new double[5];   // água consumida por fase

        for (int n = 0; n < c.Ciclo; n++)
        {
            if (!dias.TryGetValue(ini.AddDays(n), out var d)) return null;
            var et0Dia = usarPm ? Clima.Et0Pm(d, lat) : Clima.Et0(d, lat);
            var etcDia = et0Dia * c.Kc(n);             // ETc = ET0 * Kc do dia n do ciclo
            var chuvaDia = d.Chuva * fatorChuva;        // CALIBRAÇÃO: corrige o POWER se ele subestima a chuva
            etc += etcDia;
            chuva += chuvaDia;

            agua += chuvaDia;                          // balde: entra chuva
            if (agua > cad) agua = cad;                // transbordou: excesso escorre

            // Ks (FAO-56, eq. 84): com o balde abaixo de (1-p)*CAD, a planta já não consegue puxar toda a água
            double limite = (1 - P) * cad;
            double ks = agua >= limite ? 1 : agua / limite;
            double etrDia = Math.Min(ks * etcDia, agua); // água que a planta consegue consumir (nunca mais do que há)
            agua -= etrDia;                            // sai o que foi consumido

            double falta = etcDia - etrDia;
            deficit += falta;
            if (ks < 1) diasSede++;                    // dia com estresse hídrico

            int f = c.Fase(n);
            etm[f] += etcDia;
            etr[f] += etcDia - falta;
        }
        return new Safra(etc, chuva, deficit, diasSede, etr[1] / etm[1], etr[3] / etm[3]);
    }

    // perda relativa de produção: Ky * (falta de água / água que a planta queria). FAO-33.
    public static double Perda(Cultura c, Safra s) => c.Ky * s.Deficit / s.Etc;

    // faixas de risco do ZARC: 20%, 30%, 40%; acima disso, não indicado
    public static string Nivel(double risco) =>
        risco <= 0.20 ? "20%" : risco <= 0.30 ? "30%" : risco <= 0.40 ? "40%" : "inapto";

    // a safra "deu certo" nesse ano? criterio = "isna" (ZARC) ou "perda" (FAO-33)
    public static bool Passa(Cultura c, Safra s, string criterio) => criterio == "perda"
        ? Perda(c, s) <= PerdaMax
        : s.IsnaI >= IsnaMinI && s.IsnaIII >= IsnaMinIII;
}

// de onde vêm os dados (POWER, IMERG, SMAP) e o calendário de risco de um ponto.
// Usado por /recomendacao, /plano-colheita e pelos endpoints da fazenda.
static class Fontes
{
    public const string AguaErro = "aguaInicial deve ser auto, cheio ou smap";

    // água no balde no dia do plantio: "cheio" (CAD toda) ou "smap" (umidade observada; dia sem dado -> cheio)
    // "auto": smap se a cidade tem CSV do SMAP, senão cheio. Devolve o modo usado de fato (null = valor inválido).
    public static string? Agua(string aguaInicial, double lat, double lon) => aguaInicial switch
    {
        "auto" => Smap.Fc(lat, lon) is null ? "cheio" : "smap",
        "cheio" or "smap" => aguaInicial,
        _ => null
    };

    public static double AguaIni(string aguaInicial, double lat, double lon, DateOnly plantio) =>
        aguaInicial == "smap" ? Smap.Fracao(lat, lon, plantio) ?? 1 : 1;

    // clima diário do POWER; com fonteChuva=imerg, a chuva vem do CSV do IMERG. null = não tem IMERG para esse ponto.
    // "auto": IMERG se tiver CSV do pixel, senão POWER. Devolve também a fonte usada de fato.
    public static async Task<(List<Dia>? Dias, string Fonte)> Dados(IHttpClientFactory http, double lat, double lon, string fonte)
    {
        var power = await Power.BaixarAsync(http.CreateClient(), lat, lon);
        if (fonte == "power") return (power, "power");
        if (fonte is not ("auto" or "imerg")) return (null, fonte);
        var imerg = Imerg.TrocarChuva(power, lat, lon);
        return fonte == "auto" && imerg is null ? (power, "power") : (imerg, "imerg");
    }

    public static IResult SemImerg(double lat, double lon, string fonte) => fonte != "imerg"
        ? Results.BadRequest("fonteChuva deve ser auto, imerg ou power")
        : Results.BadRequest($"sem dados do IMERG para este ponto: falta {Path.GetFileName(Imerg.Arquivo(lat, lon))} em data/imerg");

    // calendário por decêndio de um ponto, na janela [inicio, fim] (só mês/dia contam; cruza o ano se fim < inicio)
    public static async Task<(CalendarioPonto? Cal, IResult? Erro)> CalendarioAsync(
        IHttpClientFactory http, double lat, double lon, DateOnly inicio, DateOnly fim, double cad, double riscoMax,
        string fonteChuva = "auto", string aguaInicial = "auto", string criterio = "isna", double fatorChuva = 1, string et0 = "pm")
    {
        if (Agua(aguaInicial, lat, lon) is not { } agua) return (null, Results.BadRequest(AguaErro));
        if (criterio is not ("isna" or "perda")) return (null, Results.BadRequest("criterio deve ser isna ou perda"));
        var (lista, fonte) = await Dados(http, lat, lon, fonteChuva);
        if (lista is null) return (null, SemImerg(lat, lon, fonte));

        var dias = lista.ToDictionary(d => d.Data);
        var p = new Parametros(lat, cad, fatorChuva, et0 == "pm", criterio, riscoMax,
                               agua == "smap" ? d => AguaIni(agua, lat, lon, d) : null);
        var (di, df) = Calendario.Janela(inicio, fim);
        return (new CalendarioPonto(Calendario.Calcular(di, df, dias, p), fonte, agua), null);
    }
}

// calendário pronto de um ponto + a fonte de chuva e o modo de água inicial usados de fato
record CalendarioPonto(List<RiscoDecendio> Decendios, string FonteChuva, string AguaInicial)
{
    // decêndio que contém a data real d (comparando só mês/dia)
    public RiscoDecendio? NoDia(DateOnly d)
    {
        var ini = Decendio.De(Calendario.NoAnoModelo(d)).Inicio;
        return Decendios.FirstOrDefault(x => x.Decendio.Inicio.Month == ini.Month && x.Decendio.Inicio.Day == ini.Day);
    }
}

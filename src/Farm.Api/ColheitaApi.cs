using static Fontes;

// plano de colheita sem cadastro: o mesmo cálculo que o front fazia em JS, agora no servidor
static class ColheitaApi
{
    public record TrechoOut(string Cultura, DateOnly De, DateOnly Ate, int Dias, double? Ha);
    public record DecendioOut(string Decendio, Dictionary<string, double> Risco, Dictionary<string, string> Nivel, string Recomendacao);
    public record PlanoOut(DateOnly Inicio, DateOnly Fim, int Dias, double CapacidadeHaDia, double? AreaHa, double Cad, double RiscoMax,
                           string FonteChuva, string AguaInicial, List<TrechoOut> Trechos, List<AjusteGrao> Graos, List<DecendioOut> Decendios);

    public static DecendioOut Saida(RiscoDecendio r) => new(r.Decendio.ToString(),
        r.Risco.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2)),
        r.Risco.ToDictionary(kv => kv.Key, kv => Modelo.Nivel(kv.Value)),
        r.Recomendacao);

    public static void MapColheita(this WebApplication app)
    {
        // ex.: /plano-colheita?lat=-19.03&lon=-47.92&inicio=2026-02-01&area=1000&colhedoras=2&haDia=60&uteis=70
        app.MapGet("/plano-colheita", async (double lat, double lon, DateOnly inicio, IHttpClientFactory http,
            double? area = null, DateOnly? fim = null, double haDia = 60, int colhedoras = 1, double uteis = 70,
            double cad = 30, double riscoMax = 0.2, string fonteChuva = "auto", string aguaInicial = "auto") =>
        {
            if (area is null && fim is null) return Results.BadRequest("informe area (fim calculado pela capacidade) ou fim");
            var capacidade = Colheita.Capacidade(haDia * colhedoras, uteis);
            if (fim is null && capacidade <= 0) return Results.BadRequest("capacidade de colheita precisa ser maior que zero");
            var f = fim ?? Colheita.Fim(inicio, Colheita.Dias(area!.Value, capacidade));
            if (f < inicio || f.DayNumber - inicio.DayNumber >= 365) return Results.BadRequest("a colheita precisa durar de 1 a 365 dias");

            var (cal, erro) = await CalendarioAsync(http, lat, lon, inicio, f, cad, riscoMax, fonteChuva, aguaInicial);
            if (cal is null) return erro!;

            var trechos = Colheita.Trechos(inicio, f, d => cal.NoDia(d)!.Recomendacao, area ?? 0, capacidade)
                .Select(t => new TrechoOut(t.Cultura, t.De, t.Ate, t.Dias, area is null ? null : t.Ha)).ToList();
            var graos = Colheita.AjusteDosGraos(inicio, f, d => cal.NoDia(d)?.Risco, riscoMax);

            return Results.Ok(new PlanoOut(inicio, f, f.DayNumber - inicio.DayNumber + 1, Math.Round(capacidade, 2), area, cad, riscoMax,
                cal.FonteChuva, cal.AguaInicial, trechos, graos, cal.Decendios.Select(Saida).ToList()));
        })
        .ComCoordenadas()
        .WithTags("Colheita")
        .WithSummary("Quanto tempo leva a colheita da soja e quantos ha vão para cada cultura da safrinha")
        .WithDescription("Sem cadastro. Com `area`, o fim sai da capacidade (haDia × colhedoras × uteis%). Com `fim`, usa a data informada. " +
                         "Cada dia de colheita recebe a cultura indicada no decêndio daquele dia (critério ZARC).")
        .Produces<PlanoOut>();

        // classes de solo aceitas no talhão
        app.MapGet("/solos", () => Solos.Cad.Select(kv => new { classe = kv.Key, cadMm = kv.Value }))
           .WithTags("Fazenda").WithSummary("Classes de água disponível do ZARC (AD1 arenoso … AD6 argiloso) e a CAD usada");
    }
}

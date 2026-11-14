using System.Globalization;

// leitor único dos números da query, para todas as rotas.
// O binding do ASP.NET lê "60,5" como 605 (vírgula = separador de milhar) e aceita NaN/Infinity.
// Aqui cada parâmetro numérico conhecido é relido do texto: ponto decimal, finito e dentro da faixa; senão 400.
static class Numeros
{
    // Min/Max inclusivos; MinAberto = o mínimo não vale (ex.: cad > 0); Inteiro = sem casas decimais
    public record Faixa(double Min, double Max, bool MinAberto = false, bool Inteiro = false)
    {
        public bool Contem(double v) => (MinAberto ? v > Min : v >= Min) && v <= Max && (!Inteiro || v == Math.Floor(v));
        public string Texto => Inteiro ? $"inteiro de {Min} a {Max}"
            : MinAberto ? $"maior que {Fmt(Min)} e até {Fmt(Max)}" : $"entre {Fmt(Min)} e {Fmt(Max)}";
        static string Fmt(double x) => x.ToString(CultureInfo.InvariantCulture);
    }

    // todo parâmetro numérico de query das rotas. Nome novo numa rota = linha nova aqui.
    public static readonly IReadOnlyDictionary<string, Faixa> Faixas = new Dictionary<string, Faixa>(StringComparer.OrdinalIgnoreCase)
    {
        ["lat"] = new(-90, 90),
        ["lon"] = new(-180, 180),
        ["cad"] = new(0, 500, MinAberto: true),            // mm de água no balde; 0 zera o balde e quebra o Ks
        ["riscoMax"] = new(0, 1),                           // 0,2 = 20%
        ["fatorChuva"] = new(0, 5),                         // calibração da chuva; negativo = chuva negativa
        ["area"] = new(0, 1_000_000, MinAberto: true),      // ha
        ["haDia"] = new(0, Colheita.HaDiaMax, MinAberto: true),
        ["colhedoras"] = new(1, Colheita.ColhedorasMax, Inteiro: true),
        ["uteis"] = new(0, 100, MinAberto: true),           // % de dias úteis
    };

    // null = ok; senão a mensagem de erro
    public static string? Validar(string nome, string? texto)
    {
        if (!Faixas.TryGetValue(nome, out var f) || texto is null) return null;
        if (texto.Contains(','))
            return $"{nome} inválido: use ponto como separador decimal (ex.: 0.2), não vírgula";
        if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !double.IsFinite(v))
            return $"{nome} inválido: \"{texto}\" não é um número finito";
        if (!f.Contem(v))
            return $"{nome} inválido: precisa ser {f.Texto}";
        return null;
    }

    // middleware: confere a query antes de qualquer rota
    public static IApplicationBuilder UseNumerosDaQuery(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        foreach (var (nome, valores) in ctx.Request.Query)
        {
            // parâmetro repetido: o binding junta com vírgula (cad=30&cad=5 vira "30,5" e depois 305). Qualquer nome, número ou não.
            if (valores.Count > 1)
            {
                await Results.BadRequest($"{nome} repetido na query: mande uma vez só").ExecuteAsync(ctx);
                return;
            }
            foreach (var texto in valores)
                if (Validar(nome, texto) is { } erro)
                {
                    await Results.BadRequest(erro).ExecuteAsync(ctx);
                    return;
                }
        }
        await next(ctx);
    });
}

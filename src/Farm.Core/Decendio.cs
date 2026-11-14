using System.Globalization;

// decêndio: blocos de 10 dias do mês (1–10, 11–20, 21–fim), a unidade de tempo do ZARC
public readonly record struct Decendio(DateOnly Inicio, DateOnly Fim)
{
    public static Decendio De(DateOnly d)
    {
        var ini = new DateOnly(d.Year, d.Month, d.Day <= 10 ? 1 : d.Day <= 20 ? 11 : 21);
        var fim = d.Day <= 20 ? ini.AddDays(9) : new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month));
        return new(ini, fim);
    }

    // todos os decêndios que tocam a janela [di, df], em ordem
    public static IEnumerable<Decendio> Entre(DateOnly di, DateOnly df)
    {
        for (var d = di; d <= df; )
        {
            var x = De(d);
            yield return x;
            d = x.Fim.AddDays(1);
        }
    }

    public override string ToString() =>
        $"{Inicio.ToString("dd/MM", CultureInfo.InvariantCulture)} a {Fim.ToString("dd/MM", CultureInfo.InvariantCulture)}";
}

using System.Collections.Concurrent;
using System.Globalization;

// chuva do IMERG (GPM), lida de CSV baixado antes: data/imerg/<lat>_<lon>.csv com linhas "2016-01-01,12.3"
// O resto do clima (temperatura, radiação, vento...) continua vindo do POWER.
static class Imerg
{
    public static string Pasta = "";  // definida no Program.cs: <raiz>/data/imerg
    static readonly ConcurrentDictionary<string, Dictionary<DateOnly, double>> cache = new();

    // centro do pixel de 0,1° que contém o ponto (grade do IMERG começa em -89,95 / -179,95)
    public static (double Lat, double Lon) Pixel(double lat, double lon) =>
        (Math.Round(Math.Floor((lat + 90) / 0.1) * 0.1 - 89.95, 2),
         Math.Round(Math.Floor((lon + 180) / 0.1) * 0.1 - 179.95, 2));

    public static string Arquivo(double lat, double lon)
    {
        var inv = CultureInfo.InvariantCulture;
        var (pla, plo) = Pixel(lat, lon);
        return Path.Combine(Pasta, $"{pla.ToString("F2", inv)}_{plo.ToString("F2", inv)}.csv");
    }

    // troca a chuva do POWER pela do IMERG. null = não tem CSV para esse pixel.
    public static List<Dia>? TrocarChuva(List<Dia> dias, double lat, double lon)
    {
        var arq = Arquivo(lat, lon);
        if (!File.Exists(arq)) return null;

        var chuva = cache.GetOrAdd(arq, a => File.ReadLines(a)
            .Select(l => l.Split(','))
            .Where(c => c.Length == 2 && DateOnly.TryParse(c[0], CultureInfo.InvariantCulture, out _))
            .ToDictionary(c => DateOnly.Parse(c[0], CultureInfo.InvariantCulture),
                          c => double.Parse(c[1], CultureInfo.InvariantCulture)));

        // dia sem IMERG: mantém a chuva do POWER
        return dias.Select(d => chuva.TryGetValue(d.Data, out var mm) ? d with { Chuva = mm } : d)   // record: copia mudando só a chuva
                   .ToList();
    }
}

using System.Collections.Concurrent;
using System.Globalization;

// umidade da zona de raízes do SMAP L4 (0–100 cm), lida de CSV baixado antes pelo tools/Farm.Smap:
// data/smap/<mesmo nome do CSV do IMERG>.csv ("2016-01-01,0.2129") e data/smap/constantes.csv ("arquivo,wp,poros")
static class Smap
{
    public static string Pasta = "";  // definida no Program.cs: <raiz>/data/smap
    static readonly ConcurrentDictionary<string, (Dictionary<DateOnly, double> Theta, double Wp, double Fc)?> cache = new();

    static (Dictionary<DateOnly, double> Theta, double Wp, double Fc)? Carregar(string nome) => cache.GetOrAdd(nome, n =>
    {
        var inv = CultureInfo.InvariantCulture;
        var arq = Path.Combine(Pasta, n);
        var cte = Path.Combine(Pasta, "constantes.csv");
        if (!File.Exists(arq) || !File.Exists(cte)) return null;

        var theta = File.ReadLines(arq).Skip(1).Select(l => l.Split(',')).Where(c => c.Length == 2)
            .ToDictionary(c => DateOnly.Parse(c[0], inv), c => double.Parse(c[1], inv));
        var linha = File.ReadLines(cte).Skip(1).Select(l => l.Split(',')).FirstOrDefault(c => c[0] == n);
        if (linha is null || theta.Count == 0) return null;

        // capacidade de campo empírica: umidade típica do solo drenado depois das chuvas; o SMAP não publica CC
        var ord = theta.Values.Order().ToArray();
        var fc = ord[(int)Math.Round(0.95 * (ord.Length - 1))];
        return (theta, double.Parse(linha[1], inv), fc);
    });

    // CC usada para o arquivo do ponto (null = sem SMAP)
    public static double? Fc(double lat, double lon) => Carregar(Path.GetFileName(Imerg.Arquivo(lat, lon)))?.Fc;

    // fração da CAD cheia no dia: (theta - wp) / (cc - wp), entre 0 e 1. null = sem arquivo ou sem dado no dia
    public static double? Fracao(double lat, double lon, DateOnly dia)
    {
        var s = Carregar(Path.GetFileName(Imerg.Arquivo(lat, lon)));
        if (s is null || !s.Value.Theta.TryGetValue(dia, out var t)) return null;
        return Math.Clamp((t - s.Value.Wp) / (s.Value.Fc - s.Value.Wp), 0, 1);
    }
}

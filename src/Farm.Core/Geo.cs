// contas de polígono de talhão (anel de pontos lon/lat em graus, como no GeoJSON)
// projeção plana local (equiretangular na latitude média): erro desprezível no tamanho de um talhão
public static class Geo
{
    const double R = 6371008.8; // raio médio da Terra (m)

    static (double X, double Y)[] Projetar(IReadOnlyList<(double Lon, double Lat)> anel, double lat0)
    {
        var k = Math.PI / 180;
        return anel.Select(p => (R * p.Lon * k * Math.Cos(lat0 * k), R * p.Lat * k)).ToArray();
    }

    // anel aberto (sem repetir o 1º ponto no fim)
    static IReadOnlyList<(double Lon, double Lat)> Abrir(IReadOnlyList<(double Lon, double Lat)> anel) =>
        anel.Count > 1 && anel[0] == anel[^1] ? anel.Take(anel.Count - 1).ToList() : anel;

    // área em hectares (fórmula do laço)
    public static double AreaHa(IReadOnlyList<(double Lon, double Lat)> anel)
    {
        anel = Abrir(anel);
        if (anel.Count < 3) return 0;
        var p = Projetar(anel, anel.Average(q => q.Lat));
        double a = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Length];
            a += x1 * y2 - x2 * y1;
        }
        return Math.Abs(a) / 2 / 10_000;
    }

    // centroide da área (não a média dos vértices); polígono degenerado cai na média
    public static (double Lon, double Lat) Centroide(IReadOnlyList<(double Lon, double Lat)> anel)
    {
        anel = Abrir(anel);
        if (anel.Count == 0) throw new ArgumentException("polígono vazio");
        double lat0 = anel.Average(q => q.Lat), lon0 = anel.Average(q => q.Lon);
        var p = Projetar(anel.Select(q => (q.Lon - lon0, q.Lat - lat0)).ToList(), lat0); // perto da origem: menos erro numérico
        double a = 0, cx = 0, cy = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Length];
            var f = x1 * y2 - x2 * y1;
            a += f; cx += (x1 + x2) * f; cy += (y1 + y2) * f;
        }
        if (Math.Abs(a) < 1e-9) return (lon0, lat0);
        cx /= 3 * a; cy /= 3 * a;
        var k = Math.PI / 180;
        return (lon0 + cx / (R * k * Math.Cos(lat0 * k)), lat0 + cy / (R * k));
    }
}

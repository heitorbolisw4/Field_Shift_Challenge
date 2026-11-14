using Microsoft.EntityFrameworkCore;

// banco da "Minha fazenda". SQLite agora; para Postgres basta trocar UseSqlite por UseNpgsql e gerar migração nova.
public class FarmDb(DbContextOptions<FarmDb> options) : DbContext(options)
{
    public DbSet<Fazenda> Fazendas => Set<Fazenda>();
    public DbSet<Talhao> Talhoes => Set<Talhao>();
    public DbSet<Colhedora> Colhedoras => Set<Colhedora>();
    public DbSet<ColheitaEvento> Colheitas => Set<ColheitaEvento>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Usuario>(e =>
        {
            e.Property(u => u.Id).HasMaxLength(32);
            e.Property(u => u.Email).HasMaxLength(254);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Nome).HasMaxLength(200);
        });
        m.Entity<RefreshToken>(e =>
        {
            e.Property(r => r.Hash).HasMaxLength(64);
            e.HasIndex(r => r.Hash).IsUnique();
            e.HasIndex(r => r.UsuarioId);
            e.HasOne<Usuario>().WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Fazenda>(e =>
        {
            e.HasIndex(f => f.DonoId);                 // toda consulta filtra pelo dono
            e.Property(f => f.DonoId).HasMaxLength(128);
            e.Property(f => f.Nome).HasMaxLength(200);
            e.HasMany(f => f.Talhoes).WithOne(t => t.Fazenda).HasForeignKey(t => t.FazendaId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(f => f.Colhedoras).WithOne(c => c.Fazenda).HasForeignKey(c => c.FazendaId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Talhao>(e =>
        {
            e.Property(t => t.Nome).HasMaxLength(200);
            e.Property(t => t.ClasseSolo).HasMaxLength(3);
            e.HasMany(t => t.Colheitas).WithOne(c => c.Talhao).HasForeignKey(c => c.TalhaoId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<Colhedora>(e => e.Property(c => c.Nome).HasMaxLength(200));
        m.Entity<ColheitaEvento>(e =>
        {
            e.HasIndex(c => new { c.TalhaoId, c.Data });
            e.Property(c => c.Cultura).HasMaxLength(40);
        });
    }
}

public class Fazenda
{
    public int Id { get; set; }
    public required string DonoId { get; set; }         // id do usuário dono (multiusuário)
    public required string Nome { get; set; }
    public double? Lat { get; set; }                     // sede: usada quando o talhão não tem coordenada
    public double? Lon { get; set; }
    public double DiasUteisPct { get; set; } = 70;      // ESTIMATIVA: % de dias com colheita (o resto é chuva/quebra)
    public double RiscoMax { get; set; } = 0.2;         // risco aceito na recomendação (0,2 = 20%)
    public DateTimeOffset CriadaEm { get; set; } = DateTimeOffset.UtcNow;
    public List<Talhao> Talhoes { get; set; } = [];
    public List<Colhedora> Colhedoras { get; set; } = [];
}

public class Talhao
{
    public int Id { get; set; }
    public int FazendaId { get; set; }
    public Fazenda Fazenda { get; set; } = null!;
    public required string Nome { get; set; }
    public double AreaHa { get; set; }
    public string ClasseSolo { get; set; } = Solos.Padrao; // AD1 … AD6 (ZARC)
    public double? Lat { get; set; }                       // ponto do talhão ou centroide do polígono
    public double? Lon { get; set; }
    public string? PoligonoGeoJson { get; set; }           // geometria GeoJSON (Polygon), lon/lat
    public List<ColheitaEvento> Colheitas { get; set; } = [];
}

public class Colhedora
{
    public int Id { get; set; }
    public int FazendaId { get; set; }
    public Fazenda Fazenda { get; set; } = null!;
    public required string Nome { get; set; }
    public double HaDia { get; set; } = 60;              // ESTIMATIVA: ha colhidos por dia de trabalho
}

// "colhi a soja do talhão X na data D": guarda a recomendação calculada naquele momento
public class ColheitaEvento
{
    public int Id { get; set; }
    public int TalhaoId { get; set; }
    public Talhao Talhao { get; set; } = null!;
    public DateOnly Data { get; set; }
    public required string Cultura { get; set; }         // cultura indicada para plantar
    public double? RiscoMilho { get; set; }
    public double? RiscoSorgo { get; set; }
    public required string Decendio { get; set; }        // "11/02 a 20/02"
    public double Cad { get; set; }
    public double RiscoMax { get; set; }
    public required string FonteChuva { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

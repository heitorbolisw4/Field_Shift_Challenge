// conta de quem usa o app. O id vira o claim "sub" do JWT e o DonoId das fazendas.
public class Usuario
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Email { get; set; }            // sempre minúsculo e sem espaços
    public required string SenhaHash { get; set; }        // PBKDF2 do ASP.NET Identity (nunca a senha)
    public string? Nome { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}

// refresh token: o app troca por um JWT novo sem pedir a senha. Guardado só o hash (SHA-256).
// Cada uso gera um novo e revoga o antigo; usar um já revogado derruba todos do usuário (token vazado).
public class RefreshToken
{
    public int Id { get; set; }
    public required string UsuarioId { get; set; }
    public required string Hash { get; set; }
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevogadoEm { get; set; }
}

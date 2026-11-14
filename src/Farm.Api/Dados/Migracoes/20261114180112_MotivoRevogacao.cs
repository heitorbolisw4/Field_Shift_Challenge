using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Farm.Api.Dados.Migracoes
{
    /// <inheritdoc />
    public partial class MotivoRevogacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Motivo",
                table: "RefreshTokens",
                type: "TEXT",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "RefreshTokens");
        }
    }
}

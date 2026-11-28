using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoffeeReader.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParserVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParserVersion",
                table: "Books",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParserVersion",
                table: "Books");
        }
    }
}

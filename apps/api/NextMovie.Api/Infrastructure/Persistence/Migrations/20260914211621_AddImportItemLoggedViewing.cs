using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportItemLoggedViewing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_logged_viewing",
                table: "import_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_logged_viewing",
                table: "import_items");
        }
    }
}

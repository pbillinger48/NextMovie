using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionalProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "region_catalogs",
                columns: table => new
                {
                    region = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_region_catalogs", x => x.region);
                });

            migrationBuilder.CreateTable(
                name: "regional_providers",
                columns: table => new
                {
                    region = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    streaming_provider_id = table.Column<int>(type: "integer", nullable: false),
                    display_priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regional_providers", x => new { x.region, x.streaming_provider_id });
                    table.ForeignKey(
                        name: "fk_regional_providers_streaming_providers_streaming_provider_id",
                        column: x => x.streaming_provider_id,
                        principalTable: "streaming_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_regional_providers_region_display_priority",
                table: "regional_providers",
                columns: new[] { "region", "display_priority" });

            migrationBuilder.CreateIndex(
                name: "ix_regional_providers_streaming_provider_id",
                table: "regional_providers",
                column: "streaming_provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "region_catalogs");

            migrationBuilder.DropTable(
                name: "regional_providers");
        }
    }
}

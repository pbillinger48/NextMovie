using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStreamingAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "users",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "movie_availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    region = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    link = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_availability", x => x.id);
                    table.ForeignKey(
                        name: "fk_movie_availability_movies_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "streaming_providers",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    logo_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streaming_providers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "availability_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movie_availability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    streaming_provider_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_availability_offers_movie_availability_movie_availability_id",
                        column: x => x.movie_availability_id,
                        principalTable: "movie_availability",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_availability_offers_streaming_providers_streaming_provider_",
                        column: x => x.streaming_provider_id,
                        principalTable: "streaming_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_streaming_providers",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    streaming_provider_id = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_streaming_providers", x => new { x.user_id, x.streaming_provider_id });
                    table.ForeignKey(
                        name: "fk_user_streaming_providers_streaming_providers_streaming_prov",
                        column: x => x.streaming_provider_id,
                        principalTable: "streaming_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_streaming_providers_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_availability_offers_movie_availability_id_streaming_provide",
                table: "availability_offers",
                columns: new[] { "movie_availability_id", "streaming_provider_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_availability_offers_streaming_provider_id",
                table: "availability_offers",
                column: "streaming_provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_movie_availability_movie_id_region",
                table: "movie_availability",
                columns: new[] { "movie_id", "region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_streaming_providers_streaming_provider_id",
                table: "user_streaming_providers",
                column: "streaming_provider_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "availability_offers");

            migrationBuilder.DropTable(
                name: "user_streaming_providers");

            migrationBuilder.DropTable(
                name: "movie_availability");

            migrationBuilder.DropTable(
                name: "streaming_providers");

            migrationBuilder.DropColumn(
                name: "region",
                table: "users");
        }
    }
}

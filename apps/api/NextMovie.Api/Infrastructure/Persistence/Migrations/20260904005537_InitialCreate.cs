using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "movies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tmdb_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overview = table.Column<string>(type: "text", nullable: true),
                    poster_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    backdrop_path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    runtime = table.Column<int>(type: "integer", nullable: true),
                    average_rating = table.Column<double>(type: "double precision", nullable: true),
                    popularity = table.Column<double>(type: "double precision", nullable: true),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "movie_genre",
                columns: table => new
                {
                    genre_id = table.Column<int>(type: "integer", nullable: false),
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_genre", x => new { x.genre_id, x.movie_id });
                    table.ForeignKey(
                        name: "fk_movie_genre_genres_genres_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_movie_genre_movies_movies_id",
                        column: x => x.movie_id,
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_genres_name",
                table: "genres",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movie_genre_movies_id",
                table: "movie_genre",
                column: "movie_id");

            migrationBuilder.CreateIndex(
                name: "ix_movies_title",
                table: "movies",
                column: "title");

            migrationBuilder.CreateIndex(
                name: "ix_movies_tmdb_id",
                table: "movies",
                column: "tmdb_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movie_genre");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "movies");
        }
    }
}

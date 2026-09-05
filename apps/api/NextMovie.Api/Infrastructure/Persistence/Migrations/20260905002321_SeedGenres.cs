using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedGenres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "genres",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { 12, "Adventure" },
                    { 14, "Fantasy" },
                    { 16, "Animation" },
                    { 18, "Drama" },
                    { 27, "Horror" },
                    { 28, "Action" },
                    { 35, "Comedy" },
                    { 36, "History" },
                    { 37, "Western" },
                    { 53, "Thriller" },
                    { 80, "Crime" },
                    { 99, "Documentary" },
                    { 878, "Science Fiction" },
                    { 9648, "Mystery" },
                    { 10402, "Music" },
                    { 10749, "Romance" },
                    { 10751, "Family" },
                    { 10752, "War" },
                    { 10770, "TV Movie" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 99);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 878);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 9648);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 10402);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 10749);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 10751);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 10752);

            migrationBuilder.DeleteData(
                table: "genres",
                keyColumn: "id",
                keyValue: 10770);
        }
    }
}

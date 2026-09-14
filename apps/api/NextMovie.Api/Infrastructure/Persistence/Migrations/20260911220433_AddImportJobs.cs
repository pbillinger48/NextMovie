using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_items = table.Column<int>(type: "integer", nullable: false),
                    skipped_rows = table.Column<int>(type: "integer", nullable: false),
                    matched_items = table.Column<int>(type: "integer", nullable: false),
                    ambiguous_items = table.Column<int>(type: "integer", nullable: false),
                    unresolved_items = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "import_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    film_uri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rating = table.Column<decimal>(type: "numeric(2,1)", nullable: true),
                    watched_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    matched_movie_id = table.Column<Guid>(type: "uuid", nullable: true),
                    match_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    candidate_tmdb_ids = table.Column<int[]>(type: "integer[]", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_items_import_jobs_import_job_id",
                        column: x => x.import_job_id,
                        principalTable: "import_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_import_items_movies_matched_movie_id",
                        column: x => x.matched_movie_id,
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_items_import_job_id_status",
                table: "import_items",
                columns: new[] { "import_job_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_import_items_matched_movie_id",
                table: "import_items",
                column: "matched_movie_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_status_created_at",
                table: "import_jobs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_import_jobs_user_id",
                table: "import_jobs",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_items");

            migrationBuilder.DropTable(
                name: "import_jobs");
        }
    }
}

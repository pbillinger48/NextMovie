using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NextMovie.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendation_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    recommendation_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_responses_movies_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_recommendation_responses_recommendation_events_recommendati",
                        column: x => x.recommendation_event_id,
                        principalTable: "recommendation_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_recommendation_responses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_responses_movie_id",
                table: "recommendation_responses",
                column: "movie_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_responses_recommendation_event_id",
                table: "recommendation_responses",
                column: "recommendation_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_responses_user_id_kind_responded_at",
                table: "recommendation_responses",
                columns: new[] { "user_id", "kind", "responded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_responses_user_id_movie_id",
                table: "recommendation_responses",
                columns: new[] { "user_id", "movie_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_responses");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AiGeneratedRoadmapFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Roadmaps",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Resources",
                table: "RoadmapNodes",
                type: "jsonb",
                nullable: true);

            // Existing nodes get an empty list instead of NULL.
            migrationBuilder.Sql("UPDATE \"RoadmapNodes\" SET \"Resources\" = '[]'::jsonb WHERE \"Resources\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Roadmaps");

            migrationBuilder.DropColumn(
                name: "Resources",
                table: "RoadmapNodes");
        }
    }
}

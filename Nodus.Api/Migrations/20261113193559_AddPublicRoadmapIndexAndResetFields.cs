using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodus.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicRoadmapIndexAndResetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_IsPublic",
                table: "Roadmaps",
                column: "IsPublic");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Roadmaps_IsPublic",
                table: "Roadmaps");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nodus.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReviewDbConstraintsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Nickname",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Roadmaps_IsPublic",
                table: "Roadmaps");

            migrationBuilder.DropIndex(
                name: "IX_Roadmaps_OwnerId",
                table: "Roadmaps");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordResetCode",
                table: "Users",
                type: "character(6)",
                fixedLength: true,
                maxLength: 6,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Nickname",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Roadmaps",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TimeCommitment",
                table: "Roadmaps",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");



            migrationBuilder.AlterColumn<string>(
                name: "GoalPrompt",
                table: "Roadmaps",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "RoadmapNodes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");



            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "NodeChecklistItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            // Clean-up: history row of a removed duplicate migration (exists only in some local databases).
            migrationBuilder.Sql("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261110122105_AddPasswordResetFields';");

            // Enums: integer -> readable strings. A plain ALTER would turn 0 into '0', so values are mapped explicitly.
            foreach (var column in EnumColumns)
                migrationBuilder.Sql(IntToString(column));

            // Case-insensitive unique nickname (login and the uniqueness check compare lower-cased nicknames).
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_Users_Nickname_Lower\" ON \"Users\" (lower(\"Nickname\"));");

            // Tags are stored lower-cased since the catalog refactoring; normalize rows published before it.
            migrationBuilder.Sql("UPDATE \"Roadmaps\" SET \"Tags\" = ARRAY(SELECT lower(t) FROM unnest(\"Tags\") AS t);");

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_OwnerId_CreatedAt",
                table: "Roadmaps",
                columns: new[] { "OwnerId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_Public_ClonesCount_CreatedAt",
                table: "Roadmaps",
                columns: new[] { "ClonesCount", "CreatedAt" },
                descending: new bool[0],
                filter: "\"IsPublic\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX \"IX_Users_Nickname_Lower\";");

            foreach (var column in EnumColumns)
                migrationBuilder.Sql(StringToInt(column));

            migrationBuilder.DropIndex(
                name: "IX_Roadmaps_OwnerId_CreatedAt",
                table: "Roadmaps");

            migrationBuilder.DropIndex(
                name: "IX_Roadmaps_Public_ClonesCount_CreatedAt",
                table: "Roadmaps");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordResetCode",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character(6)",
                oldFixedLength: true,
                oldMaxLength: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Nickname",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Users",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Roadmaps",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "TimeCommitment",
                table: "Roadmaps",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);



            migrationBuilder.AlterColumn<string>(
                name: "GoalPrompt",
                table: "Roadmaps",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "RoadmapNodes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);



            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "NodeChecklistItems",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Nickname",
                table: "Users",
                column: "Nickname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_IsPublic",
                table: "Roadmaps",
                column: "IsPublic");

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_OwnerId",
                table: "Roadmaps",
                column: "OwnerId");
        }

        private sealed record EnumColumn(string Table, string Column, string[] Names);

        // Names are listed in the numeric order of the C# enums (index = old integer value).
        private static readonly EnumColumn[] EnumColumns =
        [
            new("Roadmaps", "KnowledgeLevel", ["Beginner", "Intermediate", "Advanced"]),
            new("Roadmaps", "Status", ["Active", "Completed", "Archived"]),
            new("RoadmapNodes", "Status", ["Locked", "Available", "Completed"]),
            new("RoadmapNodes", "Category", ["Theory", "Practice", "Project"]),
        ];

        private static string IntToString(EnumColumn c) =>
            $"ALTER TABLE \"{c.Table}\" ALTER COLUMN \"{c.Column}\" TYPE character varying(20) " +
            $"USING (CASE \"{c.Column}\" {string.Join(" ", c.Names.Select((n, i) => $"WHEN {i} THEN '{n}'"))} END);";

        private static string StringToInt(EnumColumn c) =>
            $"ALTER TABLE \"{c.Table}\" ALTER COLUMN \"{c.Column}\" TYPE integer " +
            $"USING (CASE \"{c.Column}\" {string.Join(" ", c.Names.Select((n, i) => $"WHEN '{n}' THEN {i}"))} END);";
    }
}

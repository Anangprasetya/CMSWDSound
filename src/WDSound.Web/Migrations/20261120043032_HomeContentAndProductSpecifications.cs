using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WDSound.Web.Migrations
{
    /// <inheritdoc />
    public partial class HomeContentAndProductSpecifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SpecsEn",
                table: "Entries",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SpecsId",
                table: "Entries",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Target",
                table: "Entries",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpecsEn",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "SpecsId",
                table: "Entries");

            migrationBuilder.DropColumn(
                name: "Target",
                table: "Entries");
        }
    }
}

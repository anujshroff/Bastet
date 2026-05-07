using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bastet.Migrations.BastetDb
{
    /// <inheritdoc />
    public partial class AddAzureResourceIdToSubnet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureResourceId",
                table: "Subnets",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureResourceId",
                table: "Subnets");
        }
    }
}

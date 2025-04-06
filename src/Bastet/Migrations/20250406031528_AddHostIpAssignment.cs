using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bastet.Migrations
{
    /// <inheritdoc />
    public partial class AddHostIpAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFullyAllocated",
                table: "Subnets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DeletedHostIpAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OriginalIP = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginalSubnetId = table.Column<int>(type: "int", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_DeletedHostIpAssignments", x => x.Id));

            migrationBuilder.CreateTable(
                name: "HostIpAssignments",
                columns: table => new
                {
                    IP = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SubnetId = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostIpAssignments", x => x.IP);
                    table.ForeignKey(
                        name: "FK_HostIpAssignments_Subnets_SubnetId",
                        column: x => x.SubnetId,
                        principalTable: "Subnets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeletedHostIpAssignments_OriginalSubnetId",
                table: "DeletedHostIpAssignments",
                column: "OriginalSubnetId");

            migrationBuilder.CreateIndex(
                name: "IX_HostIpAssignments_IP",
                table: "HostIpAssignments",
                column: "IP",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostIpAssignments_SubnetId",
                table: "HostIpAssignments",
                column: "SubnetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeletedHostIpAssignments");

            migrationBuilder.DropTable(
                name: "HostIpAssignments");

            migrationBuilder.DropColumn(
                name: "IsFullyAllocated",
                table: "Subnets");
        }
    }
}

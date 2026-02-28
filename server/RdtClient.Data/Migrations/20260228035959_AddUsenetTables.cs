using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdtClient.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsenetTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsenetJobs",
                columns: table => new
                {
                    UsenetJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    JobName = table.Column<string>(type: "TEXT", nullable: false),
                    NzbFileName = table.Column<string>(type: "TEXT", nullable: false),
                    NzbContents = table.Column<string>(type: "TEXT", nullable: false),
                    TotalSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Added = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Completed = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetJobs", x => x.UsenetJobId);
                });

            migrationBuilder.CreateTable(
                name: "UsenetProviders",
                columns: table => new
                {
                    UsenetProviderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    UseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Password = table.Column<string>(type: "TEXT", nullable: true),
                    MaxConnections = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetProviders", x => x.UsenetProviderId);
                });

            migrationBuilder.CreateTable(
                name: "UsenetFiles",
                columns: table => new
                {
                    UsenetFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsenetJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    SegmentIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetFiles", x => x.UsenetFileId);
                    table.ForeignKey(
                        name: "FK_UsenetFiles_UsenetJobs_UsenetJobId",
                        column: x => x.UsenetJobId,
                        principalTable: "UsenetJobs",
                        principalColumn: "UsenetJobId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsenetFiles_UsenetJobId",
                table: "UsenetFiles",
                column: "UsenetJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsenetFiles");

            migrationBuilder.DropTable(
                name: "UsenetProviders");

            migrationBuilder.DropTable(
                name: "UsenetJobs");
        }
    }
}

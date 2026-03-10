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
                name: "UsenetDavItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastHealthCheck = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    NextHealthCheck = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetDavItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsenetDavItems_UsenetDavItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "UsenetDavItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsenetFiles",
                columns: table => new
                {
                    UsenetFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsenetJobId = table.Column<Guid>(type: "TEXT", nullable: true),
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
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsenetHealthCheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DavItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    RepairStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetHealthCheckResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsenetHealthCheckStats",
                columns: table => new
                {
                    DateStartInclusive = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DateEndExclusive = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    RepairStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetHealthCheckStats", x => new { x.DateStartInclusive, x.DateEndExclusive, x.Result, x.RepairStatus });
                });

            migrationBuilder.CreateTable(
                name: "UsenetMultipartFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetMultipartFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsenetMultipartFiles_UsenetDavItems_Id",
                        column: x => x.Id,
                        principalTable: "UsenetDavItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsenetNzbFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SegmentIds = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetNzbFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsenetNzbFiles_UsenetDavItems_Id",
                        column: x => x.Id,
                        principalTable: "UsenetDavItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsenetRarFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RarParts = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsenetRarFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsenetRarFiles_UsenetDavItems_Id",
                        column: x => x.Id,
                        principalTable: "UsenetDavItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsenetDavItems_ParentId_Name",
                table: "UsenetDavItems",
                columns: new[] { "ParentId", "Name" },
                unique: true);

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
                name: "UsenetHealthCheckResults");

            migrationBuilder.DropTable(
                name: "UsenetHealthCheckStats");

            migrationBuilder.DropTable(
                name: "UsenetMultipartFiles");

            migrationBuilder.DropTable(
                name: "UsenetNzbFiles");

            migrationBuilder.DropTable(
                name: "UsenetRarFiles");

            migrationBuilder.DropTable(
                name: "UsenetProviders");

            migrationBuilder.DropTable(
                name: "UsenetJobs");

            migrationBuilder.DropTable(
                name: "UsenetDavItems");
        }
    }
}

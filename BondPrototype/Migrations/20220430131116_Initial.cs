using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BondPrototype.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rating = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DirectedById = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movies_Persons_DirectedById",
                        column: x => x.DirectedById,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MovieCast",
                columns: table => new
                {
                    ActedInId = table.Column<int>(type: "int", nullable: false),
                    ActorsId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieCast", x => new { x.ActedInId, x.ActorsId });
                    table.ForeignKey(
                        name: "FK_MovieCast_Movies_ActedInId",
                        column: x => x.ActedInId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MovieCast_Persons_ActorsId",
                        column: x => x.ActorsId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Persons",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Denis Villeneuve" },
                    { 2, "Ethan Coen" },
                    { 3, "Josh Brolin" },
                    { 4, "Javier Bardem" },
                    { 5, "Timothée Chalamet" },
                    { 6, "Woody Harrelson" },
                    { 7, "Benicio Del Toro" }
                });

            migrationBuilder.InsertData(
                table: "Movies",
                columns: new[] { "Id", "DirectedById", "Rating", "ReleaseDate", "Title" },
                values: new object[] { 1, 1, (byte)8, new DateTime(2021, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), "Dune" });

            migrationBuilder.InsertData(
                table: "Movies",
                columns: new[] { "Id", "DirectedById", "Rating", "ReleaseDate", "Title" },
                values: new object[] { 2, 2, (byte)8, new DateTime(2007, 11, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), "No Country for Old Men" });

            migrationBuilder.InsertData(
                table: "Movies",
                columns: new[] { "Id", "DirectedById", "Rating", "ReleaseDate", "Title" },
                values: new object[] { 3, 1, (byte)7, new DateTime(2015, 10, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "Sicario" });

            migrationBuilder.InsertData(
                table: "MovieCast",
                columns: new[] { "ActedInId", "ActorsId" },
                values: new object[,]
                {
                    { 1, 3 },
                    { 1, 4 },
                    { 1, 5 },
                    { 2, 3 },
                    { 2, 4 },
                    { 2, 6 },
                    { 3, 3 },
                    { 3, 7 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovieCast_ActorsId",
                table: "MovieCast",
                column: "ActorsId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_DirectedById",
                table: "Movies",
                column: "DirectedById");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieCast");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "Persons");
        }
    }
}

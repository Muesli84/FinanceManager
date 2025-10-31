using System;
using Microsoft.EntityFrameworkCore.Migrations;
using System.IO;

#nullable disable

namespace FinanceManager.Infrastructure.Data.Migrations.Identity
{
    /// <inheritdoc />
    public partial class AddPostingsValutaDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ValutaDate",
                table: "Postings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValutaDate",
                table: "Postings");
        }
    }
}

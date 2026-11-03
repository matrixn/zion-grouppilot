using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FacebookWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PostingAccess",
                table: "Groups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "Groups",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostingAccess",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Groups");
        }
    }
}

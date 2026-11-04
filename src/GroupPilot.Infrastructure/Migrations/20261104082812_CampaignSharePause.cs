using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CampaignSharePause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextShareAt",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PauseSeconds",
                table: "Campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 120);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextShareAt",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "PauseSeconds",
                table: "Campaigns");
        }
    }
}

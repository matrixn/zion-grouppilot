using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RecurringCampaignRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastShareGroupUrl",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextRunAt",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Recurrence",
                table: "Campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RunNumber",
                table: "Campaigns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Campaigns",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RunNumber",
                table: "Attempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastShareGroupUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NextRunAt",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Recurrence",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "RunNumber",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "RunNumber",
                table: "Attempts");
        }
    }
}

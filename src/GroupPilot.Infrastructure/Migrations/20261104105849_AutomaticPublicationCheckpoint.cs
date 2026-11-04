using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticPublicationCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutomationMessage",
                table: "Targets",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SubmissionStartedAt",
                table: "Targets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "Attempts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomatic",
                table: "Attempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutomationMessage",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "SubmissionStartedAt",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "IsAutomatic",
                table: "Attempts");
        }
    }
}

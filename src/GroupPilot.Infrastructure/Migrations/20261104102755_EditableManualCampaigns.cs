using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GroupPilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditableManualCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsExcluded",
                table: "Targets",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecurrenceAnchorAt",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsExcluded",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "RecurrenceAnchorAt",
                table: "Campaigns");
        }
    }
}

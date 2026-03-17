using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalysis.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "TradingWindowEnd",
                table: "StrategyRules",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TradingWindowStart",
                table: "StrategyRules",
                type: "interval",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TradingWindowEnd",
                table: "StrategyRules");

            migrationBuilder.DropColumn(
                name: "TradingWindowStart",
                table: "StrategyRules");
        }
    }
}

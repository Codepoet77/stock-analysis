using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalysis.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategiesAndLiveSignals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Strategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Strategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategyRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    SignalTimeframe = table.Column<string>(type: "text", nullable: false),
                    BiasTimeframe = table.Column<string>(type: "text", nullable: true),
                    ConfirmTimeframe = table.Column<string>(type: "text", nullable: true),
                    SignalTypesJson = table.Column<string>(type: "text", nullable: false),
                    ExitStrategy = table.Column<string>(type: "text", nullable: false),
                    FixedRRatio = table.Column<decimal>(type: "numeric", nullable: false),
                    KillZoneOnly = table.Column<bool>(type: "boolean", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyRules_Strategies_StrategyId",
                        column: x => x.StrategyId,
                        principalTable: "Strategies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LiveSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StrategyRuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<string>(type: "text", nullable: false),
                    SignalType = table.Column<string>(type: "text", nullable: false),
                    TimeframeLabel = table.Column<string>(type: "text", nullable: false),
                    EntryTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Target = table.Column<decimal>(type: "numeric", nullable: false),
                    Stop = table.Column<decimal>(type: "numeric", nullable: false),
                    ExitStrategy = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OutcomeTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActualExitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    ActualRR = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentStop = table.Column<decimal>(type: "numeric", nullable: false),
                    BreakevenActivated = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveSignals_StrategyRules_StrategyRuleId",
                        column: x => x.StrategyRuleId,
                        principalTable: "StrategyRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveSignals_StrategyRuleId",
                table: "LiveSignals",
                column: "StrategyRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveSignals_Symbol_Status",
                table: "LiveSignals",
                columns: new[] { "Symbol", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Strategies_UserId",
                table: "Strategies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyRules_StrategyId",
                table: "StrategyRules",
                column: "StrategyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveSignals");

            migrationBuilder.DropTable(
                name: "StrategyRules");

            migrationBuilder.DropTable(
                name: "Strategies");
        }
    }
}

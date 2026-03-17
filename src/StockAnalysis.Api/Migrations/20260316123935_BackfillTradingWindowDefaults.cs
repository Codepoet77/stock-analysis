using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockAnalysis.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillTradingWindowDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing rows to standard US market hours (9:30–16:00 ET)
            migrationBuilder.Sql(@"
                UPDATE ""StrategyRules""
                SET ""TradingWindowStart"" = INTERVAL '9 hours 30 minutes',
                    ""TradingWindowEnd""   = INTERVAL '16 hours'
                WHERE ""TradingWindowStart"" IS NULL OR ""TradingWindowEnd"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""StrategyRules""
                SET ""TradingWindowStart"" = NULL,
                    ""TradingWindowEnd""   = NULL;
            ");
        }
    }
}

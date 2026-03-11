using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TradingIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tickers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CompanyName = table.Column<string>(type: "text", nullable: true),
                    Exchange = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    TelegramChatId = table.Column<string>(type: "text", nullable: true),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MomentumScores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TickerSymbol = table.Column<string>(type: "text", nullable: false),
                    TotalScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    RedditScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    NewsScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    VolumeScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    OptionsScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    SentimentScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    TradeBias = table.Column<int>(type: "integer", nullable: false),
                    SignalSummary = table.Column<string>(type: "text", nullable: true),
                    AiAnalysis = table.Column<string>(type: "text", nullable: true),
                    TradeSetup = table.Column<string>(type: "text", nullable: true),
                    RiskFactors = table.Column<string>(type: "text", nullable: true),
                    Session = table.Column<int>(type: "integer", nullable: false),
                    ScoredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TickerId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MomentumScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MomentumScores_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalTable: "Tickers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SignalEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TickerSymbol = table.Column<string>(type: "text", nullable: false),
                    SignalType = table.Column<int>(type: "integer", nullable: false),
                    SignalScore = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    RawText = table.Column<string>(type: "text", nullable: true),
                    SentimentScore = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TickerId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignalEvents_Tickers_TickerId",
                        column: x => x.TickerId,
                        principalTable: "Tickers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Watchlists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TickerSymbol = table.Column<string>(type: "text", nullable: false),
                    AlertThreshold = table.Column<decimal>(type: "numeric", nullable: true),
                    AlertEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Watchlists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Watchlists_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Tickers",
                columns: new[] { "Id", "CompanyName", "CreatedAt", "Exchange", "IsActive", "Symbol" },
                values: new object[,]
                {
                    { 1, "Apple Inc", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(3533), "NASDAQ", true, "AAPL" },
                    { 2, "NVIDIA Corporation", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5633), "NASDAQ", true, "NVDA" },
                    { 3, "Microsoft Corporation", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5641), "NASDAQ", true, "MSFT" },
                    { 4, "Tesla Inc", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5642), "NASDAQ", true, "TSLA" },
                    { 5, "Amazon.com Inc", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5643), "NASDAQ", true, "AMZN" },
                    { 6, "Meta Platforms Inc", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5644), "NASDAQ", true, "META" },
                    { 7, "Alphabet Inc", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5646), "NASDAQ", true, "GOOGL" },
                    { 8, "Advanced Micro Devices", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5648), "NASDAQ", true, "AMD" },
                    { 9, "SPDR S&P 500 ETF", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5649), "NYSE", true, "SPY" },
                    { 10, "Invesco QQQ Trust", new DateTime(2026, 3, 11, 21, 3, 27, 876, DateTimeKind.Utc).AddTicks(5650), "NASDAQ", true, "QQQ" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_MomentumScores_TickerId",
                table: "MomentumScores",
                column: "TickerId");

            migrationBuilder.CreateIndex(
                name: "IX_MomentumScores_TickerSymbol_ScoredAt",
                table: "MomentumScores",
                columns: new[] { "TickerSymbol", "ScoredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SignalEvents_TickerId",
                table: "SignalEvents",
                column: "TickerId");

            migrationBuilder.CreateIndex(
                name: "IX_SignalEvents_TickerSymbol_DetectedAt",
                table: "SignalEvents",
                columns: new[] { "TickerSymbol", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickers_Symbol",
                table: "Tickers",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Watchlists_UserId_TickerSymbol",
                table: "Watchlists",
                columns: new[] { "UserId", "TickerSymbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MomentumScores");

            migrationBuilder.DropTable(
                name: "SignalEvents");

            migrationBuilder.DropTable(
                name: "Watchlists");

            migrationBuilder.DropTable(
                name: "Tickers");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}

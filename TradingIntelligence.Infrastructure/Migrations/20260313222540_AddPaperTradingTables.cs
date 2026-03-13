using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTradingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OtpCode",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCode", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OtpCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: false),
                    CodeHash = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperTrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    TickerSymbol = table.Column<string>(type: "text", nullable: false),
                    MomentumScoreId = table.Column<int>(type: "integer", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    TradeBias = table.Column<int>(type: "integer", nullable: false),
                    TotalScoreAtEntry = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExitPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    PnlPoints = table.Column<decimal>(type: "numeric", nullable: true),
                    PnlPercent = table.Column<decimal>(type: "numeric", nullable: true),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperTrades_MomentumScores_MomentumScoreId",
                        column: x => x.MomentumScoreId,
                        principalTable: "MomentumScores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SignalAccuracies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TickerSymbol = table.Column<string>(type: "text", nullable: false),
                    TotalTrades = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    Breakevens = table.Column<int>(type: "integer", nullable: false),
                    WinRate = table.Column<decimal>(type: "numeric", nullable: false),
                    AvgPnlPercent = table.Column<decimal>(type: "numeric", nullable: false),
                    AvgScoreAtEntry = table.Column<decimal>(type: "numeric", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignalAccuracies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrokerTrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PaperTradeId = table.Column<int>(type: "integer", nullable: false),
                    Mt5Ticket = table.Column<long>(type: "bigint", nullable: false),
                    Mt5Symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LotSize = table.Column<decimal>(type: "numeric", nullable: false),
                    FilledPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentPrice = table.Column<decimal>(type: "numeric", nullable: true),
                    BrokerStatus = table.Column<int>(type: "integer", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrokerTrades_PaperTrades_PaperTradeId",
                        column: x => x.PaperTradeId,
                        principalTable: "PaperTrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerTrades_BrokerStatus",
                table: "BrokerTrades",
                column: "BrokerStatus");

            migrationBuilder.CreateIndex(
                name: "IX_BrokerTrades_PaperTradeId",
                table: "BrokerTrades",
                column: "PaperTradeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpCode_Email",
                table: "OtpCode",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_OtpCode_ExpiresAt",
                table: "OtpCode",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaperTrades_MomentumScoreId",
                table: "PaperTrades",
                column: "MomentumScoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignalAccuracies_TickerSymbol",
                table: "SignalAccuracies",
                column: "TickerSymbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokerTrades");

            migrationBuilder.DropTable(
                name: "OtpCode");

            migrationBuilder.DropTable(
                name: "OtpCodes");

            migrationBuilder.DropTable(
                name: "SignalAccuracies");

            migrationBuilder.DropTable(
                name: "PaperTrades");
        }
    }
}

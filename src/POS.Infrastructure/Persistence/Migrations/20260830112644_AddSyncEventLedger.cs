using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncEventLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sync_events_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sync_events_terminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "terminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_Sequence",
                table: "sync_events",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_StoreId_AggregateType_AggregateId",
                table: "sync_events",
                columns: new[] { "StoreId", "AggregateType", "AggregateId" });

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_StoreId_EventId",
                table: "sync_events",
                columns: new[] { "StoreId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_StoreId_Sequence",
                table: "sync_events",
                columns: new[] { "StoreId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_TerminalId",
                table: "sync_events",
                column: "TerminalId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_events");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncEventSequencePerStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sync_events_Sequence",
                table: "sync_events");

            migrationBuilder.DropIndex(
                name: "IX_sync_events_StoreId_Sequence",
                table: "sync_events");

            migrationBuilder.AlterColumn<long>(
                name: "Sequence",
                table: "sync_events",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_StoreId_Sequence",
                table: "sync_events",
                columns: new[] { "StoreId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sync_events_StoreId_Sequence",
                table: "sync_events");

            migrationBuilder.AlterColumn<long>(
                name: "Sequence",
                table: "sync_events",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_Sequence",
                table: "sync_events",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_events_StoreId_Sequence",
                table: "sync_events",
                columns: new[] { "StoreId", "Sequence" });
        }
    }
}

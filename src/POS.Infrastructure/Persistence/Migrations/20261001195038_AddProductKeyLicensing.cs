using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductKeyLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaxTerminals = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssignedStoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_product_keys_stores_AssignedStoreId",
                        column: x => x.AssignedStoreId,
                        principalTable: "stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "product_keys",
                columns: new[] { "Id", "ActivatedAtUtc", "AssignedStoreId", "CreatedAtUtc", "CreatedBy", "ExpiresAtUtc", "KeyCode", "MaxTerminals", "PlanTier", "Status", "UpdatedAtUtc", "UpdatedBy" },
                values: new object[] { new Guid("a0000000-0000-0000-0000-000000000001"), null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "MIQ-PRO-7K4D-92LX-AB83", 3, "PRO", "Available", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_product_keys_AssignedStoreId",
                table: "product_keys",
                column: "AssignedStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_product_keys_KeyCode",
                table: "product_keys",
                column: "KeyCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_keys");
        }
    }
}

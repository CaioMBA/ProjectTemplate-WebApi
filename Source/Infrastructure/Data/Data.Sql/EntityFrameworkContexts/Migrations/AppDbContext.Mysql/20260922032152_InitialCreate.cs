using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Sql.EntityFrameworkContexts.Migrations.AppDbContext.Mysql
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    event_type = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    content_type = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    occurred_on_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    processed_on_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    next_attempt_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    trace_parent = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    sku = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    price_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    price_currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_by = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_by = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    is_deleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    deleted_by = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                table: "outbox_messages",
                columns: new[] { "processed_on_utc", "next_attempt_utc" },
                filter: "processed_on_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_created_at_utc",
                table: "products",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_products_sku",
                table: "products",
                column: "sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}

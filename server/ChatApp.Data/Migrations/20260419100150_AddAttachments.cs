using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddAttachments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "attachments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: true),
                uploader_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<int>(type: "integer", nullable: false),
                original_filename = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                stored_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                mime = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                size_bytes = table.Column<long>(type: "bigint", nullable: false),
                thumb_path = table.Column<string>(type: "text", nullable: true),
                comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_attachments", x => x.id);
                table.ForeignKey(
                    name: "fk_attachments_messages_message_id",
                    column: x => x.message_id,
                    principalTable: "messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_attachments_message_id",
            table: "attachments",
            column: "message_id");

        migrationBuilder.CreateIndex(
            name: "ix_attachments_unlinked_created",
            table: "attachments",
            column: "created_at",
            filter: "message_id IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "attachments");
    }
}

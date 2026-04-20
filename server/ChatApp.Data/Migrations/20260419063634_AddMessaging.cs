using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddMessaging : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "messages",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                scope = table.Column<int>(type: "integer", nullable: false),
                personal_chat_id = table.Column<Guid>(type: "uuid", nullable: true),
                room_id = table.Column<Guid>(type: "uuid", nullable: true),
                author_id = table.Column<Guid>(type: "uuid", nullable: false),
                body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                reply_to_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "unread_markers",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                scope = table.Column<int>(type: "integer", nullable: false),
                scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                unread_count = table.Column<int>(type: "integer", nullable: false),
                last_read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_unread_markers", x => new { x.user_id, x.scope, x.scope_id });
            });

        migrationBuilder.CreateIndex(
            name: "ix_messages_personal_chat_created",
            table: "messages",
            columns: new[] { "personal_chat_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_messages_room_created",
            table: "messages",
            columns: new[] { "room_id", "created_at", "id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "messages");

        migrationBuilder.DropTable(
            name: "unread_markers");
    }
}

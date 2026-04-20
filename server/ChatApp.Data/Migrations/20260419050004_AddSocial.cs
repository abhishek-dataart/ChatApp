using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddSocial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "friendships",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id_low = table.Column<Guid>(type: "uuid", nullable: false),
                user_id_high = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                request_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_friendships", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "personal_chats",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_personal_chats", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_friendships_requester_id",
            table: "friendships",
            column: "requester_id");

        migrationBuilder.CreateIndex(
            name: "ux_friendships_pair",
            table: "friendships",
            columns: new[] { "user_id_low", "user_id_high" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_personal_chats_pair",
            table: "personal_chats",
            columns: new[] { "user_a_id", "user_b_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "friendships");

        migrationBuilder.DropTable(
            name: "personal_chats");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddRoomModeration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "moderation_audit",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                room_id = table.Column<Guid>(type: "uuid", nullable: false),
                actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                target_id = table.Column<Guid>(type: "uuid", nullable: true),
                action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                detail = table.Column<string>(type: "jsonb", nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_moderation_audit", x => x.id);
                table.ForeignKey(
                    name: "fk_moderation_audit_rooms_room_id",
                    column: x => x.room_id,
                    principalTable: "rooms",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_moderation_audit_users_actor_id",
                    column: x => x.actor_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_moderation_audit_users_target_id",
                    column: x => x.target_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "room_bans",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                room_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                banned_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lifted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_room_bans", x => x.id);
                table.ForeignKey(
                    name: "fk_room_bans_rooms_room_id",
                    column: x => x.room_id,
                    principalTable: "rooms",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_room_bans_users_banned_by_id",
                    column: x => x.banned_by_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_room_bans_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_moderation_audit_actor_id",
            table: "moderation_audit",
            column: "actor_id");

        migrationBuilder.CreateIndex(
            name: "ix_moderation_audit_room_created",
            table: "moderation_audit",
            columns: new[] { "room_id", "created_at", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_moderation_audit_target_id",
            table: "moderation_audit",
            column: "target_id");

        migrationBuilder.CreateIndex(
            name: "ix_room_bans_banned_by_id",
            table: "room_bans",
            column: "banned_by_id");

        migrationBuilder.CreateIndex(
            name: "ix_room_bans_room_id",
            table: "room_bans",
            column: "room_id");

        migrationBuilder.CreateIndex(
            name: "ix_room_bans_user_id",
            table: "room_bans",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ux_room_bans_room_user_active",
            table: "room_bans",
            columns: new[] { "room_id", "user_id" },
            unique: true,
            filter: "\"lifted_at\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "moderation_audit");

        migrationBuilder.DropTable(
            name: "room_bans");
    }
}

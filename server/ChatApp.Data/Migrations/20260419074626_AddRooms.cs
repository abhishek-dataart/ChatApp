using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddRooms : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "rooms",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                name_normalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                visibility = table.Column<int>(type: "integer", nullable: false),
                owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                capacity = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_rooms", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "room_members",
            columns: table => new
            {
                room_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<int>(type: "integer", nullable: false),
                joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_room_members", x => new { x.room_id, x.user_id });
                table.ForeignKey(
                    name: "fk_room_members_rooms_room_id",
                    column: x => x.room_id,
                    principalTable: "rooms",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_room_members_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_room_members_user_id",
            table: "room_members",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name: "ix_rooms_owner_id",
            table: "rooms",
            column: "owner_id");

        migrationBuilder.CreateIndex(
            name: "ix_rooms_visibility",
            table: "rooms",
            column: "visibility");

        migrationBuilder.CreateIndex(
            name: "ux_rooms_name_normalized",
            table: "rooms",
            column: "name_normalized",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "room_members");

        migrationBuilder.DropTable(
            name: "rooms");
    }
}

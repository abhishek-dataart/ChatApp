using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddRoomInvitations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "room_invitations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                room_id = table.Column<Guid>(type: "uuid", nullable: false),
                inviter_id = table.Column<Guid>(type: "uuid", nullable: false),
                invitee_id = table.Column<Guid>(type: "uuid", nullable: false),
                note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_room_invitations", x => x.id);
                table.ForeignKey(
                    name: "fk_room_invitations_rooms_room_id",
                    column: x => x.room_id,
                    principalTable: "rooms",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "fk_room_invitations_users_invitee_id",
                    column: x => x.invitee_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_room_invitations_users_inviter_id",
                    column: x => x.inviter_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_room_invitations_invitee_id",
            table: "room_invitations",
            column: "invitee_id");

        migrationBuilder.CreateIndex(
            name: "ix_room_invitations_inviter_id",
            table: "room_invitations",
            column: "inviter_id");

        migrationBuilder.CreateIndex(
            name: "ux_room_invitations_room_invitee",
            table: "room_invitations",
            columns: new[] { "room_id", "invitee_id" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "room_invitations");
    }
}

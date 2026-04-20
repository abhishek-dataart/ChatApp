using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class DeleteAccountCascade : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_moderation_audit_users_actor_id",
            table: "moderation_audit");

        migrationBuilder.DropForeignKey(
            name: "fk_moderation_audit_users_target_id",
            table: "moderation_audit");

        migrationBuilder.DropForeignKey(
            name: "fk_room_bans_users_banned_by_id",
            table: "room_bans");

        migrationBuilder.DropForeignKey(
            name: "fk_room_bans_users_user_id",
            table: "room_bans");

        migrationBuilder.DropForeignKey(
            name: "fk_room_invitations_users_invitee_id",
            table: "room_invitations");

        migrationBuilder.DropForeignKey(
            name: "fk_room_invitations_users_inviter_id",
            table: "room_invitations");

        migrationBuilder.DropForeignKey(
            name: "fk_room_members_users_user_id",
            table: "room_members");

        migrationBuilder.DropForeignKey(
            name: "fk_user_bans_users_banned_id",
            table: "user_bans");

        migrationBuilder.DropForeignKey(
            name: "fk_user_bans_users_banner_id",
            table: "user_bans");

        migrationBuilder.AlterColumn<Guid>(
            name: "actor_id",
            table: "moderation_audit",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "author_id",
            table: "messages",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.AlterColumn<Guid>(
            name: "uploader_id",
            table: "attachments",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateIndex(
            name: "ix_messages_author_id",
            table: "messages",
            column: "author_id");

        migrationBuilder.CreateIndex(
            name: "ix_friendships_user_id_high",
            table: "friendships",
            column: "user_id_high");

        migrationBuilder.CreateIndex(
            name: "ix_attachments_uploader_id",
            table: "attachments",
            column: "uploader_id");

        migrationBuilder.AddForeignKey(
            name: "fk_attachments_users_uploader_id",
            table: "attachments",
            column: "uploader_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_friendships_users_user_id_high",
            table: "friendships",
            column: "user_id_high",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_friendships_users_user_id_low",
            table: "friendships",
            column: "user_id_low",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_messages_users_author_id",
            table: "messages",
            column: "author_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_moderation_audit_users_actor_id",
            table: "moderation_audit",
            column: "actor_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_moderation_audit_users_target_id",
            table: "moderation_audit",
            column: "target_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "fk_room_bans_users_banned_by_id",
            table: "room_bans",
            column: "banned_by_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_room_bans_users_user_id",
            table: "room_bans",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_room_invitations_users_invitee_id",
            table: "room_invitations",
            column: "invitee_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_room_invitations_users_inviter_id",
            table: "room_invitations",
            column: "inviter_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_room_members_users_user_id",
            table: "room_members",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_rooms_users_owner_id",
            table: "rooms",
            column: "owner_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_unread_markers_users_user_id",
            table: "unread_markers",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_user_bans_users_banned_id",
            table: "user_bans",
            column: "banned_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "fk_user_bans_users_banner_id",
            table: "user_bans",
            column: "banner_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_attachments_users_uploader_id",
            table: "attachments");

        migrationBuilder.DropForeignKey(
            name: "fk_friendships_users_user_id_high",
            table: "friendships");

        migrationBuilder.DropForeignKey(
            name: "fk_friendships_users_user_id_low",
            table: "friendships");

        migrationBuilder.DropForeignKey(
            name: "fk_messages_users_author_id",
            table: "messages");

        migrationBuilder.DropForeignKey(
            name: "fk_moderation_audit_users_actor_id",
            table: "moderation_audit");

        migrationBuilder.DropForeignKey(
            name: "fk_moderation_audit_users_target_id",
            table: "moderation_audit");

        migrationBuilder.DropForeignKey(
            name: "fk_room_bans_users_banned_by_id",
            table: "room_bans");

        migrationBuilder.DropForeignKey(
            name: "fk_room_bans_users_user_id",
            table: "room_bans");

        migrationBuilder.DropForeignKey(
            name: "fk_room_invitations_users_invitee_id",
            table: "room_invitations");

        migrationBuilder.DropForeignKey(
            name: "fk_room_invitations_users_inviter_id",
            table: "room_invitations");

        migrationBuilder.DropForeignKey(
            name: "fk_room_members_users_user_id",
            table: "room_members");

        migrationBuilder.DropForeignKey(
            name: "fk_rooms_users_owner_id",
            table: "rooms");

        migrationBuilder.DropForeignKey(
            name: "fk_unread_markers_users_user_id",
            table: "unread_markers");

        migrationBuilder.DropForeignKey(
            name: "fk_user_bans_users_banned_id",
            table: "user_bans");

        migrationBuilder.DropForeignKey(
            name: "fk_user_bans_users_banner_id",
            table: "user_bans");

        migrationBuilder.DropIndex(
            name: "ix_messages_author_id",
            table: "messages");

        migrationBuilder.DropIndex(
            name: "ix_friendships_user_id_high",
            table: "friendships");

        migrationBuilder.DropIndex(
            name: "ix_attachments_uploader_id",
            table: "attachments");

        migrationBuilder.AlterColumn<Guid>(
            name: "actor_id",
            table: "moderation_audit",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "author_id",
            table: "messages",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "uploader_id",
            table: "attachments",
            type: "uuid",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AddForeignKey(
            name: "fk_moderation_audit_users_actor_id",
            table: "moderation_audit",
            column: "actor_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_moderation_audit_users_target_id",
            table: "moderation_audit",
            column: "target_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_room_bans_users_banned_by_id",
            table: "room_bans",
            column: "banned_by_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_room_bans_users_user_id",
            table: "room_bans",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_room_invitations_users_invitee_id",
            table: "room_invitations",
            column: "invitee_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_room_invitations_users_inviter_id",
            table: "room_invitations",
            column: "inviter_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_room_members_users_user_id",
            table: "room_members",
            column: "user_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_user_bans_users_banned_id",
            table: "user_bans",
            column: "banned_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "fk_user_bans_users_banner_id",
            table: "user_bans",
            column: "banner_id",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }
}

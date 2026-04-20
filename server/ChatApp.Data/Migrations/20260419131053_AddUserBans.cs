using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddUserBans : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_bans",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                banner_id = table.Column<Guid>(type: "uuid", nullable: false),
                banned_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                lifted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_bans", x => x.id);
                table.ForeignKey(
                    name: "fk_user_bans_users_banned_id",
                    column: x => x.banned_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_user_bans_users_banner_id",
                    column: x => x.banner_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_user_bans_banned_id",
            table: "user_bans",
            column: "banned_id");

        migrationBuilder.CreateIndex(
            name: "ix_user_bans_banner_id",
            table: "user_bans",
            column: "banner_id");

        migrationBuilder.CreateIndex(
            name: "ux_user_bans_banner_banned_active",
            table: "user_bans",
            columns: new[] { "banner_id", "banned_id" },
            unique: true,
            filter: "\"lifted_at\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_bans");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddPasswordResetAndSessionExpiry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "expires_at",
            table: "sessions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "password_reset_tokens",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                request_ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                table.ForeignKey(
                    name: "fk_password_reset_tokens_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_password_reset_tokens_token_hash",
            table: "password_reset_tokens",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_password_reset_tokens_user_id_used_at",
            table: "password_reset_tokens",
            columns: new[] { "user_id", "used_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "password_reset_tokens");

        migrationBuilder.DropColumn(
            name: "expires_at",
            table: "sessions");
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatApp.Data.Migrations;

/// <inheritdoc />
public partial class AddIdentity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                email_normalized = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                username_normalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                display_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                avatar_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_users", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "sessions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                cookie_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sessions", x => x.id);
                table.ForeignKey(
                    name: "fk_sessions_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_sessions_cookie_hash",
            table: "sessions",
            column: "cookie_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_sessions_user_id_revoked_at",
            table: "sessions",
            columns: new[] { "user_id", "revoked_at" });

        migrationBuilder.CreateIndex(
            name: "ix_users_email_normalized",
            table: "users",
            column: "email_normalized",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_users_username_normalized",
            table: "users",
            column: "username_normalized",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sessions");

        migrationBuilder.DropTable(
            name: "users");
    }
}

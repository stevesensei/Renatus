using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renatus.Host.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSomeApi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxPlayerCount",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "Height",
                table: "Textures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Textures",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SkinModel",
                table: "Textures",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Textures",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Width",
                table: "Textures",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Textures_UserId",
                table: "Textures",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Textures_Users_UserId",
                table: "Textures",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Textures_Users_UserId",
                table: "Textures");

            migrationBuilder.DropIndex(
                name: "IX_Textures_UserId",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "MaxPlayerCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Height",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "SkinModel",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "Textures");
        }
    }
}

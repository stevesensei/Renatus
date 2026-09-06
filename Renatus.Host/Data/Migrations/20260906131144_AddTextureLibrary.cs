using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Renatus.Host.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTextureLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionMarkdown",
                table: "Textures",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Textures",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "未命名材质");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Textures",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Original");

            migrationBuilder.CreateIndex(
                name: "IX_Textures_IsPublic_Type_CreatedAt",
                table: "Textures",
                columns: new[] { "IsPublic", "Type", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Textures_IsPublic_Type_CreatedAt",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "DescriptionMarkdown",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Textures");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Textures");

        }
    }
}

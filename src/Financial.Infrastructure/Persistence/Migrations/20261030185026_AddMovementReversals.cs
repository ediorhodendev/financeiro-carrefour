using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Financial.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMovementReversals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReversalOfMovementId",
                table: "movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "movements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_movements_ReversalOfMovementId",
                table: "movements",
                column: "ReversalOfMovementId",
                unique: true,
                filter: "\"ReversalOfMovementId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reversal_metadata",
                table: "movements",
                sql: "(\"ReversalOfMovementId\" IS NULL AND \"ReversalReason\" IS NULL) OR (\"ReversalOfMovementId\" IS NOT NULL AND \"ReversalOfMovementId\" <> \"Id\" AND \"ReversalReason\" IS NOT NULL AND length(btrim(\"ReversalReason\")) BETWEEN 1 AND 500)");

            migrationBuilder.AddForeignKey(
                name: "FK_movements_movements_ReversalOfMovementId",
                table: "movements",
                column: "ReversalOfMovementId",
                principalTable: "movements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE FUNCTION validate_movement_reversal() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE original movements%ROWTYPE;
                BEGIN
                    IF NEW."ReversalOfMovementId" IS NULL THEN RETURN NEW; END IF;
                    SELECT * INTO original FROM movements WHERE "Id" = NEW."ReversalOfMovementId";
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Original movement not found' USING ERRCODE = '23503';
                    END IF;
                    IF original."AccountId" <> NEW."AccountId" OR original."ReversalOfMovementId" IS NOT NULL
                        OR original."Amount" <> NEW."Amount" OR original."Type" = NEW."Type" THEN
                        RAISE EXCEPTION 'Invalid full movement reversal' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER movements_validate_reversal BEFORE INSERT ON movements
                FOR EACH ROW EXECUTE FUNCTION validate_movement_reversal();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER movements_validate_reversal ON movements;
                DROP FUNCTION validate_movement_reversal();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_movements_movements_ReversalOfMovementId",
                table: "movements");

            migrationBuilder.DropIndex(
                name: "IX_movements_ReversalOfMovementId",
                table: "movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reversal_metadata",
                table: "movements");

            migrationBuilder.DropColumn(
                name: "ReversalOfMovementId",
                table: "movements");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "movements");
        }
    }
}

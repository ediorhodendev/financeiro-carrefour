using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace Financial.Infrastructure.Persistence.Migrations;
[DbContext(typeof(FinancialDbContext))]
[Migration("20261028000100_ProtectLedger")]
public sealed class ProtectLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION reject_movement_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'Confirmed financial movements are immutable' USING ERRCODE = '23514';
        END;
        $$;
        CREATE TRIGGER movements_immutable BEFORE UPDATE OR DELETE ON movements
        FOR EACH ROW EXECUTE FUNCTION reject_movement_mutation();
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER movements_immutable ON movements;
        DROP FUNCTION reject_movement_mutation();
        """);
}

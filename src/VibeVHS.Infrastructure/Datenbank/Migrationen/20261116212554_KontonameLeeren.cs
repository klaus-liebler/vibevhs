using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeVHS.Infrastructure.Datenbank.Migrationen;

/// <inheritdoc />
public partial class _20261116212554_KontonameLeeren : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Konten mit Person: der Name kommt aus der Person, die gespeicherte Kopie entfällt.
        migrationBuilder.Sql("""UPDATE "Benutzerkonten" SET "Name" = NULL WHERE "PersonId" IS NOT NULL;""");

    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {

    }
}

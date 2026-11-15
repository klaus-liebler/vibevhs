using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Domain.Personen;

namespace VibeVHS.Infrastructure.Datenbank;

public sealed class VibeVhsDbContext(DbContextOptions<VibeVhsDbContext> optionen) : DbContext(optionen)
{
    public DbSet<Person> Personen => Set<Person>();

    public DbSet<PersonName> PersonNamen => Set<PersonName>();

    public DbSet<AuditEintrag> Protokoll => Set<AuditEintrag>();

    public DbSet<AusgehendeNachricht> Postausgang => Set<AusgehendeNachricht>();

    protected override void ConfigureConventions(ModelConfigurationBuilder konfiguration)
    {
        // SQLite kennt keinen Zeitstempel mit Offset; als sortierbare Zahl (UTC-Ticks) speichern.
        konfiguration.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        konfiguration.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(b =>
        {
            b.ToTable("Personen");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.EMail).HasMaxLength(320);
            b.Property(p => p.Sprache).HasMaxLength(10);
            b.HasMany(p => p.Namen).WithOne().HasForeignKey("PersonId").IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Navigation(p => p.Namen).HasField("_namen").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });

        modelBuilder.Entity<PersonName>(b =>
        {
            b.ToTable("PersonNamen");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.Vorname).HasMaxLength(200);
            b.Property(n => n.Nachname).HasMaxLength(200);
            b.Property(n => n.Grund).HasMaxLength(500);
            b.HasIndex("PersonId", nameof(PersonName.GueltigVon));
            b.HasIndex(n => n.Nachname);
        });

        modelBuilder.Entity<AuditEintrag>(b =>
        {
            b.ToTable("Protokoll");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Aktion).HasMaxLength(100);
            b.Property(e => e.ObjektTyp).HasMaxLength(100);
            b.Property(e => e.Benutzer).HasMaxLength(200);
            b.Property(e => e.BenutzerKennung).HasMaxLength(100);
            b.Property(e => e.Rolle).HasMaxLength(50);
            b.HasIndex(e => e.Zeitpunkt);
            b.HasIndex(e => new { e.ObjektTyp, e.ObjektId });
        });

        modelBuilder.Entity<AusgehendeNachricht>(b =>
        {
            b.ToTable("Postausgang");
            b.HasKey(n => n.Id);
            b.Property(n => n.Id).ValueGeneratedNever();
            b.Property(n => n.Empfaenger).HasMaxLength(320);
            b.Property(n => n.EmpfaengerAnzeige).HasMaxLength(200);
            b.Property(n => n.Betreff).HasMaxLength(500);
            b.Property(n => n.Sprache).HasMaxLength(10);
            b.Property(n => n.AusgeloestVon).HasMaxLength(200);
            b.HasIndex(n => new { n.Status, n.ErstelltAm });
            b.HasIndex(n => n.PersonId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NurAnfuegenPruefen();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NurAnfuegenPruefen();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Protokolleinträge und Namensversionen dürfen nur angefügt, nie gelöscht werden.</summary>
    private void NurAnfuegenPruefen()
    {
        var verboten = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted && e.Entity is AuditEintrag or PersonName
                        || e.State == EntityState.Modified && e.Entity is AuditEintrag)
            .Select(e => e.Entity.GetType().Name)
            .ToList();
        if (verboten.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unzulässige Änderung an nur anfügbaren Daten: {string.Join(", ", verboten.Distinct())}.");
        }
    }
}

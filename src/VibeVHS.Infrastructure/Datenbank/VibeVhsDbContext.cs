using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeVHS.Domain.Gemeinsam;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VibeVHS.Application.Personen;
using VibeVHS.Application.Postausgang;
using VibeVHS.Application.Protokoll;
using VibeVHS.Application.Sicherheit;
using VibeVHS.Domain.Personen;
using VibeVHS.Domain.Stammdaten;
using VibeVHS.Infrastructure.Personen;

namespace VibeVHS.Infrastructure.Datenbank;

public sealed class VibeVhsDbContext(DbContextOptions<VibeVhsDbContext> optionen) : DbContext(optionen)
{
    public DbSet<Person> Personen => Set<Person>();

    public DbSet<PersonName> PersonNamen => Set<PersonName>();

    public DbSet<AuditEintrag> Protokoll => Set<AuditEintrag>();

    public DbSet<AusgehendeNachricht> Postausgang => Set<AusgehendeNachricht>();

    public DbSet<Einrichtung> Einrichtungen => Set<Einrichtung>();

    public DbSet<Fachbereich> Fachbereiche => Set<Fachbereich>();

    public DbSet<Benutzerkonto> Benutzerkonten => Set<Benutzerkonto>();

    public DbSet<Organisation> Organisationen => Set<Organisation>();

    public DbSet<DublettenAusschluss> DublettenAusschluesse => Set<DublettenAusschluss>();

    public DbSet<Suchindexeintrag> Suchindex => Set<Suchindexeintrag>();

    public DbSet<VibeVHS.Domain.Programm.Semester> Semester => Set<VibeVHS.Domain.Programm.Semester>();

    public DbSet<VibeVHS.Domain.Programm.Kursart> Kursarten => Set<VibeVHS.Domain.Programm.Kursart>();

    public DbSet<VibeVHS.Domain.Programm.Kurs> Kurse => Set<VibeVHS.Domain.Programm.Kurs>();

    public DbSet<VibeVHS.Domain.Programm.Veranstaltung> Veranstaltungen => Set<VibeVHS.Domain.Programm.Veranstaltung>();

    public DbSet<VibeVHS.Domain.Programm.Ort> Orte => Set<VibeVHS.Domain.Programm.Ort>();

    public DbSet<VibeVHS.Domain.Programm.Merkmal> Merkmale => Set<VibeVHS.Domain.Programm.Merkmal>();

    public DbSet<VibeVHS.Domain.Programm.Ferienzeitraum> Ferien => Set<VibeVHS.Domain.Programm.Ferienzeitraum>();

    public DbSet<VibeVHS.Domain.Aufgaben.Aufgabe> Aufgaben => Set<VibeVHS.Domain.Aufgaben.Aufgabe>();

    public DbSet<VibeVHS.Domain.Anmeldung.Belegung> Belegungen => Set<VibeVHS.Domain.Anmeldung.Belegung>();

    public DbSet<VibeVHS.Domain.Anmeldung.KontingentEreignis> KontingentEreignisse => Set<VibeVHS.Domain.Anmeldung.KontingentEreignis>();

    public DbSet<VibeVHS.Domain.Anmeldung.Ermaessigung> Ermaessigungen => Set<VibeVHS.Domain.Anmeldung.Ermaessigung>();

    public DbSet<VibeVHS.Domain.Vorlagen.Textvorlage> Vorlagen => Set<VibeVHS.Domain.Vorlagen.Textvorlage>();

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
            b.Property(p => p.Titel).HasMaxLength(50);
            b.Property(p => p.Rollen).HasConversion<int>();
            b.Property(p => p.Benutzerkennung).HasMaxLength(100);
            b.HasIndex(p => p.Benutzerkennung).IsUnique();
            b.Property(p => p.Status).HasMaxLength(30);
            b.Property(p => p.Anzeigename).HasMaxLength(300);
            b.Property(p => p.Sortiername).HasMaxLength(300);
            b.Property(p => p.Sortierschluessel).HasMaxLength(340);
            b.Property(p => p.Wohnort).HasMaxLength(200);
            b.PrimitiveCollection(p => p.Fachbereiche);
            b.HasIndex(p => p.Sortierschluessel).IsUnique();
            b.HasIndex(p => p.Geburtsdatum);
            b.HasIndex(p => p.Status);
            b.Property(p => p.Datenschutz).HasMaxLength(20);
            b.HasIndex(p => p.Datenschutz);
            b.Property(p => p.Sperrgrund).HasMaxLength(2000);
            b.Property(p => p.StatistikAltersgruppe).HasMaxLength(30);
            b.Property(p => p.StatistikGebiet).HasMaxLength(100);
            b.Property(p => p.StatistikGeschlecht).HasMaxLength(30);
            Unterliste(b, p => p.Namen, "_namen");
            Unterliste(b, p => p.Merkmale, "_merkmale");
            Unterliste(b, p => p.Adressen, "_adressen");
            Unterliste(b, p => p.AdressKennzeichen, "_kennzeichen");
            Unterliste(b, p => p.Rufnummern, "_rufnummern");
            Unterliste(b, p => p.EMailAdressen, "_emails");
            Unterliste(b, p => p.Organisationen, "_organisationen");
            Unterliste(b, p => p.Vollmachten, "_vollmachten");
            Unterliste(b, p => p.Einwilligungen, "_einwilligungen");
            Unterliste(b, p => p.Qualifikationen, "_qualifikationen");
        });

        Version<PersonName>(modelBuilder, "PersonNamen", b =>
        {
            b.Property(n => n.Vorname).HasMaxLength(200);
            b.Property(n => n.Nachname).HasMaxLength(200);
            b.HasIndex(n => n.Nachname);
        });
        Version<PersonMerkmal>(modelBuilder, "PersonMerkmale", b => b.Property(m => m.Inhalt).HasMaxLength(200));
        Version<Postadresse>(modelBuilder, "PersonAdressen", b =>
        {
            b.Property(a => a.Zusatz).HasMaxLength(200);
            b.Property(a => a.Strasse).HasMaxLength(200);
            b.Property(a => a.Postleitzahl).HasMaxLength(20);
            b.Property(a => a.Ort).HasMaxLength(200);
            b.Property(a => a.Land).HasMaxLength(2);
            b.HasIndex(a => a.AdressId);
        });
        Version<AdressKennzeichen>(modelBuilder, "PersonAdressKennzeichen", b => b.Property(k => k.AdressAnzeige).HasMaxLength(500));
        Version<Rufnummer>(modelBuilder, "PersonRufnummern", b =>
        {
            b.Property(r => r.Nummer).HasMaxLength(50);
            b.Property(r => r.Dienste).HasConversion<int>();
            b.HasIndex(r => r.NummerId);
        });
        Version<Organisationszugehoerigkeit>(modelBuilder, "PersonOrganisationen", b =>
        {
            b.Property(o => o.OrganisationName).HasMaxLength(300);
            b.Property(o => o.Funktion).HasMaxLength(200);
            b.HasIndex(o => o.OrganisationId);
        });
        Version<Buchungsvollmacht>(modelBuilder, "PersonVollmachten", b =>
        {
            b.Property(v => v.BevollmaechtigterName).HasMaxLength(300);
            b.Property(v => v.Nachweis).HasMaxLength(500);
            b.HasIndex(v => v.BevollmaechtigterId);
        });
        Version<Einwilligung>(modelBuilder, "PersonEinwilligungen", _ => { });
        Version<PersonQualifikation>(modelBuilder, "PersonQualifikationen", b =>
        {
            b.Property(q => q.Schluessel).HasMaxLength(40);
            b.Property(q => q.Bezeichnung).HasMaxLength(200);
            b.Property(q => q.WertText).HasMaxLength(200);
            b.Property(q => q.Nachweis).HasMaxLength(500);
            b.Property(q => q.AusstellendeStelle).HasMaxLength(200);
        });

        modelBuilder.Entity<Organisation>(b =>
        {
            b.ToTable("Organisationen");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Name).HasMaxLength(300);
            b.Property(o => o.Ort).HasMaxLength(200);
        });

        modelBuilder.Entity<DublettenAusschluss>(b =>
        {
            b.ToTable("DublettenAusschluesse");
            b.HasKey(d => new { d.PersonA, d.PersonB });
            b.Property(d => d.Von).HasMaxLength(200);
        });

        ProgrammMapping.Anwenden(modelBuilder);
        AnmeldungMapping.Anwenden(modelBuilder);
        Durchfuehrung.DurchfuehrungMapping.Anwenden(modelBuilder);
        Finanzen.FinanzMapping.Anwenden(modelBuilder);

        modelBuilder.Entity<VibeVHS.Domain.Datenschutz.Aufbewahrungsfrist>(b =>
        {
            b.ToTable("Aufbewahrungsfristen");
            b.HasKey(f => f.Schluessel);
            b.Property(f => f.Schluessel).HasMaxLength(40);
            b.Property(f => f.Bezeichnung).HasMaxLength(200);
            b.Property(f => f.Rechtsgrundlage).HasMaxLength(300);
            b.Property(f => f.Hinweis).HasMaxLength(500);
        });

        modelBuilder.Entity<VibeVHS.Domain.Datenschutz.Loeschlauf>(b =>
        {
            b.ToTable("Loeschlaeufe");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
            b.Property(l => l.Regel).HasMaxLength(300);
            b.Property(l => l.ErstelltVon).HasMaxLength(200);
            b.Property(l => l.Freigabe1Name).HasMaxLength(200);
            b.Property(l => l.Freigabe2Name).HasMaxLength(200);
            b.Property(l => l.Freigabe1Kennung).HasMaxLength(100);
            b.Property(l => l.Freigabe2Kennung).HasMaxLength(100);
            b.PrimitiveCollection(l => l.PersonIds);
        });
        Version<VibeVHS.Domain.Programm.KursEntgelt>(modelBuilder, "KursEntgelte", _ => { }, "KursId");
        Version<VibeVHS.Domain.Programm.OrtMerkmal>(modelBuilder, "OrtMerkmale", b => b.Property(m => m.Schluessel).HasMaxLength(40), "OrtId");

        // FTS5-Volltextindex; wird per SQL in der Migration angelegt und von SaveChanges gepflegt.
        modelBuilder.Entity<Suchindexeintrag>(b =>
        {
            b.ToTable("PersonSuche", t => t.ExcludeFromMigrations());
            b.HasKey(s => s.PersonId);
        });

        Version<EMailAdresse>(modelBuilder, "PersonEMails", b =>
        {
            b.Property(e => e.Adresse).HasMaxLength(320);
            b.HasIndex(e => e.AdressId);
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
            b.Property(n => n.AnhangName).HasMaxLength(200);
            b.Property(n => n.AnhangTyp).HasMaxLength(100);
            b.Property(n => n.WeitereAnhaenge).HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    s => System.Text.Json.JsonSerializer.Deserialize<List<Mailanhang>>(s, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<Mailanhang>(),
                    new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<Mailanhang>>(
                        (a, c) => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null) == System.Text.Json.JsonSerializer.Serialize(c, (System.Text.Json.JsonSerializerOptions?)null),
                        v => v.Count,
                        v => v.ToList()));
            b.HasIndex(n => new { n.Status, n.ErstelltAm });
            b.HasIndex(n => n.PersonId);
        });

        modelBuilder.Entity<Einrichtung>(b =>
        {
            b.ToTable("Einrichtung");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.Name).HasMaxLength(200);
            b.Property(e => e.Kurzname).HasMaxLength(50);
            b.Property(e => e.Bundesland).HasMaxLength(50);
            b.PrimitiveCollection(e => e.Orte);
        });

        modelBuilder.Entity<Fachbereich>(b =>
        {
            b.ToTable("Fachbereiche");
            b.HasKey(f => f.Nr);
            b.Property(f => f.Nr).ValueGeneratedNever();
            b.Property(f => f.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Benutzerkonto>(b =>
        {
            b.ToTable("Benutzerkonten");
            b.HasKey(k => k.Id);
            b.Property(k => k.Id).ValueGeneratedNever();
            b.Property(k => k.Login).HasMaxLength(100);
            b.HasIndex(k => k.Login).IsUnique();
            b.Property(k => k.Name).HasMaxLength(200);
            b.Property(k => k.Funktion).HasMaxLength(300);
            b.Property(k => k.Hinweis).HasMaxLength(500);
            b.PrimitiveCollection(k => k.Rollen);
            b.PrimitiveCollection(k => k.Fachbereiche);
        });
    }

    private static void Unterliste<T>(EntityTypeBuilder<Person> b, Expression<Func<Person, IEnumerable<T>?>> navigation, string feld)
        where T : class
    {
        b.HasMany(navigation).WithOne().HasForeignKey("PersonId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        b.Navigation(navigation).HasField(feld).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }

    /// <summary>Gemeinsame Abbildung aller bitemporalen Versionen.</summary>
    private static void Version<T>(ModelBuilder modelBuilder, string tabelle, Action<EntityTypeBuilder<T>> weiter, string fremdschluessel = "PersonId")
        where T : BitemporaleVersion<T>
    {
        modelBuilder.Entity<T>(b =>
        {
            b.ToTable(tabelle);
            b.HasKey(v => v.Id);
            b.Property(v => v.Id).ValueGeneratedNever();
            b.Property(v => v.Grund).HasMaxLength(500);
            b.Property(v => v.ErfasstVon).HasMaxLength(200);
            b.HasIndex(fremdschluessel, nameof(BitemporaleVersion<T>.GueltigVon));
            weiter(b);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NurAnfuegenPruefen();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NurAnfuegenPruefen();
        var geaendert = GeaendertePersonen();
        if (geaendert.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        var eigeneTransaktion = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var anzahl = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            await PersonenSuchindex.AktualisierenAsync(this, geaendert, cancellationToken);
            if (eigeneTransaktion is not null)
            {
                await eigeneTransaktion.CommitAsync(cancellationToken);
            }

            return anzahl;
        }
        finally
        {
            if (eigeneTransaktion is not null)
            {
                await eigeneTransaktion.DisposeAsync();
            }
        }
    }

    /// <summary>Personen, deren Daten (oder Unterlisten) sich in diesem Speichervorgang ändern.</summary>
    private List<Person> GeaendertePersonen()
    {
        var ids = new HashSet<Guid>();
        foreach (var e in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (e.Entity is Person p)
            {
                ids.Add(p.Id);
            }
            else if (e.Entity is IHistorienwert && e.Metadata.FindProperty("PersonId") is not null
                     && e.Property("PersonId").CurrentValue is Guid personId)
            {
                ids.Add(personId);
            }
        }

        return ChangeTracker.Entries<Person>().Select(e => e.Entity).Where(p => ids.Contains(p.Id)).ToList();
    }

    /// <summary>Protokolleinträge und Namensversionen dürfen nur angefügt, nie gelöscht werden.</summary>
    private void NurAnfuegenPruefen()
    {
        var verboten = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted && e.Entity is AuditEintrag or IHistorienwert or VibeVHS.Domain.Anmeldung.KontingentEreignis
                        || e.State == EntityState.Modified && e.Entity is AuditEintrag or VibeVHS.Domain.Anmeldung.KontingentEreignis)
            .Select(e => e.Entity.GetType().Name)
            .ToList();
        if (verboten.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unzulässige Änderung an nur anfügbaren Daten: {string.Join(", ", verboten.Distinct())}.");
        }
    }
}

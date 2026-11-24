using VibeVHS.Domain.Gemeinsam;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VibeVHS.Domain.Aufgaben;
using VibeVHS.Domain.Programm;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Abbildung der Programmplanung (Semester, Kursarten, Kurse, Veranstaltungen, Orte, Ferien, Aufgaben).</summary>
internal static class ProgrammMapping
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Wertobjekte (Serienregel, Richtlinie, Listen) als JSON-Spalte; Änderungen werden über den JSON-Text erkannt.</summary>
    internal static PropertyBuilder<T> AlsJson<T>(this PropertyBuilder<T> p)
    {
        p.HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            s => JsonSerializer.Deserialize<T>(s, Json)!,
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                v => JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!));
        return p;
    }

    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Semester>(b =>
        {
            b.ToTable("Semester");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Name).HasMaxLength(100);
            b.Property(s => s.Kuerzel).HasMaxLength(10);
        });

        mb.Entity<Kursart>(b =>
        {
            b.ToTable("Kursarten");
            b.HasKey(k => k.Id);
            b.Property(k => k.Id).ValueGeneratedNever();
            b.Property(k => k.Name).HasMaxLength(200);
            b.FeldAlsTabelle<Kursart, Listenwert<string>>("_benoetigteMerkmale", nameof(Kursart.BenoetigteMerkmale), "KursartRaumanforderungen", "KursartId", 100);
            b.FeldAlsTabelle<Kursart, Listenwert<string>>("_benoetigteDozierendenmerkmale", nameof(Kursart.BenoetigteDozierendenmerkmale), "KursartLehranforderungen", "KursartId", 100);
            b.OwnsOne(k => k.Richtlinie, r =>
            {
                r.AlsTabelle<Kursart, Buchungsrichtlinie, Buchungsrecht>(x => x.Rechte, "KursartBuchungsrechte", "KursartId");
            });
            b.Navigation(k => k.Richtlinie).IsRequired();
        });

        mb.Entity<Qualifikationsausnahme>(b =>
        {
            b.ToTable("Qualifikationsausnahmen");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Schluessel).HasMaxLength(40);
            b.Property(a => a.Begruendung).HasMaxLength(1000);
            b.Property(a => a.ErteiltVon).HasMaxLength(200);
            b.HasIndex(a => a.KursId);
        });

        mb.Entity<Kurs>(b =>
        {
            b.ToTable("Kurse");
            b.HasKey(k => k.Id);
            b.Property(k => k.Id).ValueGeneratedNever();
            b.Property(k => k.Kursnummer).HasMaxLength(30);
            b.HasIndex(k => k.Kursnummer).IsUnique();
            b.Property(k => k.Titel).HasMaxLength(300);
            b.Property(k => k.Bildungsurlaub).HasMaxLength(100);
            b.HasIndex(k => k.SemesterId);
            b.FeldAlsTabelle<Kurs, Listenwert<string>>("_benoetigteMerkmale", nameof(Kurs.BenoetigteMerkmale), "KursRaumanforderungen", "KursId", 100);
            b.FeldAlsTabelle<Kurs, Listenwert<string>>("_benoetigteDozierendenmerkmale", nameof(Kurs.BenoetigteDozierendenmerkmale), "KursLehranforderungen", "KursId", 100);
            b.OwnsOne(k => k.Richtlinie, r =>
            {
                r.AlsTabelle<Kurs, Buchungsrichtlinie, Buchungsrecht>(x => x.Rechte, "KursBuchungsrechte", "KursId");
            });
            b.Navigation(k => k.Richtlinie).IsRequired();
            // Serienregel als Spalten des Kurses (Serie_…), Wochentage und Orte als eigene Tabellen.
            b.OwnsOne(k => k.Serie, r =>
            {
                r.Ignore(x => x.Unbefristet);
                r.FeldAlsTabelle<Kurs, Serienregel, Listenwert<DayOfWeek>>("_wochentage", nameof(Serienregel.Wochentage), "KursSerieWochentage", "KursId");
                r.FeldAlsTabelle<Kurs, Serienregel, Listenwert<Guid>>("_ortIds", nameof(Serienregel.OrtIds), "KursSerieOrte", "KursId");
            });
            // Kursteam als eigene Tabelle (statt JSON mit Namenskopie): KursId + PersonId, Leitung.
            b.OwnsMany(k => k.Team, t =>
            {
                t.ToTable("KursTeam");
                t.WithOwner().HasForeignKey("KursId");
                // Eigener Schlüssel: Einträge sind unveränderliche Werte und werden beim Ändern ersetzt (löschen + einfügen).
                t.Property<Guid>("Id").HasValueGenerator<V7Erzeuger>();
                t.HasKey("Id");
                t.HasIndex("KursId");
                t.HasIndex(d => d.PersonId);
            });
            b.Navigation(k => k.Team).UsePropertyAccessMode(PropertyAccessMode.Property);
            b.HasMany(k => k.Entgelte).WithOne().HasForeignKey("KursId").IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Navigation(k => k.Entgelte).HasField("_entgelte").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            b.HasMany(k => k.Veranstaltungen).WithOne().HasForeignKey(v => v.KursId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(k => k.Veranstaltungen).HasField("_veranstaltungen").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        mb.Entity<Veranstaltung>(b =>
        {
            b.ToTable("Veranstaltungen");
            b.HasKey(v => v.Id);
            b.Property(v => v.Id).ValueGeneratedNever();
            b.HasIndex(v => v.DozentId);
            b.Property(v => v.Bemerkung).HasMaxLength(500);
            // Orte eines Termins als eigene Tabelle (statt JSON mit Ortsnamen-Kopie).
            b.OwnsMany(v => v.Orte, o =>
            {
                o.ToTable("VeranstaltungOrte");
                o.WithOwner().HasForeignKey("VeranstaltungId");
                o.Property<Guid>("Id").HasValueGenerator<V7Erzeuger>();
                o.HasKey("Id");
                o.HasIndex("VeranstaltungId");
                o.HasIndex(x => x.OrtId);
            });
            b.HasIndex(v => v.Beginn);
            b.HasIndex(v => v.KursId);
        });

        mb.Entity<Merkmal>(b =>
        {
            b.ToTable("Merkmale");
            b.HasKey(m => m.Schluessel);
            b.Property(m => m.Schluessel).HasMaxLength(40);
            b.Property(m => m.Name).HasMaxLength(200);
            b.Property(m => m.Gruppe).HasMaxLength(60);
            b.Property(m => m.Ausschlussgruppe).HasMaxLength(60);
            b.Property(m => m.Bezug).HasMaxLength(20);
            b.FeldAlsTabelle<Merkmal, Listenwert<string>>("_werte", nameof(Merkmal.Werte), "MerkmalWerte", "MerkmalSchluessel", 200);
            b.Property(m => m.Typ).HasMaxLength(20);
            b.Property(m => m.Einheit).HasMaxLength(30);
            b.FeldAlsTabelle<Merkmal, Listenwert<string>>("_inaktiveWerte", nameof(Merkmal.InaktiveWerte), "MerkmalInaktiveWerte", "MerkmalSchluessel", 200);
        });

        mb.Entity<Ort>(b =>
        {
            b.ToTable("Orte");
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.Property(o => o.Name).HasMaxLength(200);
            b.Property(o => o.Kurzname).HasMaxLength(40);
            b.Property(o => o.Strasse).HasMaxLength(200);
            b.Property(o => o.Postleitzahl).HasMaxLength(10);
            b.Property(o => o.Stadt).HasMaxLength(100);
            b.Property(o => o.Lage).HasMaxLength(1000);
            b.Property(o => o.Zugang).HasMaxLength(500);
            b.AlsTabelle(o => o.Kapazitaeten, "OrtKapazitaeten", "OrtId");
            b.HasIndex(o => o.ElternId);
            b.HasMany(o => o.Merkmale).WithOne().HasForeignKey("OrtId").IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Navigation(o => o.Merkmale).HasField("_merkmale").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });

        mb.Entity<Schluessel>(b =>
        {
            b.ToTable("Schluessel");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.Property(s => s.Nummer).HasMaxLength(50);
            b.Property(s => s.Bezeichnung).HasMaxLength(200);
            b.Property(s => s.AusserBetriebGrund).HasMaxLength(300);
            b.HasIndex(s => s.Nummer).IsUnique();
            b.HasOne<Ort>().WithMany().HasForeignKey(s => s.OrtId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Schluesselausgabe>(b =>
        {
            b.ToTable("Schluesselausgaben");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.AusgegebenVon).HasMaxLength(200);
            b.Property(a => a.ZurueckVon).HasMaxLength(200);
            b.Property(a => a.Bemerkung).HasMaxLength(500);
            b.HasOne<Schluessel>().WithMany().HasForeignKey(a => a.SchluesselId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Domain.Personen.Person>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
            // Höchstens eine offene Ausgabe je Schlüssel (Teilindex, zweite Linie hinter der Prüfung im Dienst).
            b.HasIndex(a => a.SchluesselId).IsUnique().HasFilter("\"ZurueckAm\" IS NULL").HasDatabaseName("IX_Schluesselausgaben_offen");
            b.HasIndex(a => a.PersonId);
        });

        mb.Entity<Infomonitor>(b =>
        {
            b.ToTable("Infomonitore");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Name).HasMaxLength(200);
            b.Property(m => m.Ueberschrift).HasMaxLength(200);
            b.Property(m => m.Pruefwert).HasMaxLength(64);
            b.Property(m => m.VorschauPruefwert).HasMaxLength(64);
            b.HasIndex(m => m.Pruefwert).IsUnique();
            b.HasIndex(m => m.VorschauPruefwert);
            b.HasOne<Ort>().WithMany().HasForeignKey(m => m.OrtId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Ferienzeitraum>(b =>
        {
            b.ToTable("Ferien");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).ValueGeneratedNever();
            b.Property(f => f.Name).HasMaxLength(100);
            b.Property(f => f.Bundesland).HasMaxLength(10);
        });

        mb.Entity<Abwesenheit>(b =>
        {
            b.Ignore(a => a.Name); // Namen aus den Benutzerkonten, keine Kopie
            b.Ignore(a => a.VertretungName);
            b.ToTable("Abwesenheiten");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => x.Login);
        });
        mb.Entity<Aufgabe>(b =>
        {
            b.ToTable("Aufgaben");
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.Property(a => a.Typ).HasMaxLength(60);
            b.Property(a => a.Schluessel).HasMaxLength(200);
            b.HasIndex(a => a.Schluessel).IsUnique();
            b.HasIndex(a => new { a.Status, a.Gruppe });
            b.Property(a => a.Titel).HasMaxLength(300);
            b.Property(a => a.Gruppe).HasMaxLength(60);
            b.Property(a => a.Ziel).HasMaxLength(300);
            b.Property(a => a.Status).HasConversion<string>().HasMaxLength(40);
            b.Property(a => a.Dringlichkeit).HasConversion<string>().HasMaxLength(40);
            b.Property(a => a.BearbeiterKennung).HasMaxLength(100);
            b.Ignore(a => a.BearbeiterName); // Name aus dem Benutzerkonto, keine Kopie
            b.Property(a => a.ErledigtVon).HasMaxLength(200);
            b.Property(a => a.Vermerk).HasMaxLength(1000);
        });

        mb.Entity<Aufgabentyp>(b =>
        {
            b.ToTable("Aufgabentypen");
            b.HasKey(t => t.Schluessel);
            b.Property(t => t.Schluessel).HasMaxLength(60);
            b.Property(t => t.Name).HasMaxLength(200);
            b.Property(t => t.Rolle).HasMaxLength(60);
            b.Property(t => t.FestePerson).HasMaxLength(100);
        });
    }
}

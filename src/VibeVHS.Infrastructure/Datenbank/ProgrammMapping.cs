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
            b.PrimitiveCollection(k => k.BenoetigteMerkmale);
            b.PrimitiveCollection(k => k.BenoetigteDozierendenmerkmale);
            b.Property(k => k.Richtlinie).AlsJson();
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
            b.Property(k => k.AuftraggeberName).HasMaxLength(300);
            b.Property(k => k.Bildungsurlaub).HasMaxLength(100);
            b.Property(k => k.Suchtext).HasMaxLength(400);
            b.HasIndex(k => k.SemesterId);
            b.PrimitiveCollection(k => k.BenoetigteMerkmale);
            b.PrimitiveCollection(k => k.BenoetigteDozierendenmerkmale);
            b.Property(k => k.Richtlinie).AlsJson();
            b.Property(k => k.Serie).AlsJson();
            b.Property(k => k.Team).AlsJson();
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
            b.Property(v => v.DozentName).HasMaxLength(300);
            b.Property(v => v.Bemerkung).HasMaxLength(500);
            b.Property(v => v.Orte).AlsJson();
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
            b.PrimitiveCollection(m => m.Werte);
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
            b.Property(o => o.Suchtext).HasMaxLength(500);
            b.Property(o => o.Kapazitaeten).AlsJson();
            b.HasIndex(o => o.ElternId);
            b.HasMany(o => o.Merkmale).WithOne().HasForeignKey("OrtId").IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.Navigation(o => o.Merkmale).HasField("_merkmale").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
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
            b.Property(a => a.BearbeiterName).HasMaxLength(200);
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

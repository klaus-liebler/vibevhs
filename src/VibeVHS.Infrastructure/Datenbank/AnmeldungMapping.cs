using Microsoft.EntityFrameworkCore;
using VibeVHS.Domain.Anmeldung;
using VibeVHS.Domain.Vorlagen;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>Abbildung von Belegungen, Kontingentereignissen, Ermäßigungen und Textvorlagen.</summary>
internal static class AnmeldungMapping
{
    public static void Anwenden(ModelBuilder mb)
    {
        mb.Entity<Belegung>(b =>
        {
            b.ToTable("Belegungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Kursnummer).HasMaxLength(30);
            b.Property(x => x.GebuchtVon).HasMaxLength(200);
            b.Property(x => x.GebuchtVonKennung).HasMaxLength(100);
            b.Property(x => x.GebuchtInRolle).HasMaxLength(50);
            b.Property(x => x.BevollmaechtigterName).HasMaxLength(300);
            b.Property(x => x.ErmaessigungNachweis).HasMaxLength(500);
            b.Property(x => x.StorniertVon).HasMaxLength(200);
            b.Property(x => x.StornoGrund).HasMaxLength(500);
            b.HasIndex(x => x.KursId);
            b.HasIndex(x => x.PersonId);
            b.HasIndex(x => new { x.Status, x.AngebotBis });
            b.HasIndex(x => x.BevollmaechtigterId);
            b.HasMany(x => x.Ereignisse).WithOne().HasForeignKey(e => e.BelegungId).OnDelete(DeleteBehavior.Restrict);
            b.Navigation(x => x.Ereignisse).HasField("_ereignisse").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });

        mb.Entity<KontingentEreignis>(b =>
        {
            b.ToTable("KontingentEreignisse");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Von).HasMaxLength(200);
            b.Property(x => x.Bemerkung).HasMaxLength(300);
            b.HasIndex(x => new { x.BelegungId, x.Laufnummer }).IsUnique();
            b.HasIndex(x => x.VeranstaltungId);
        });

        mb.Entity<Ermaessigung>(b =>
        {
            b.ToTable("Ermaessigungen");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Art).HasMaxLength(100);
            b.Property(x => x.Nachweis).HasMaxLength(500);
            b.Property(x => x.ErfasstVon).HasMaxLength(200);
            b.HasIndex(x => x.PersonId);
        });

        mb.Entity<Textvorlage>(b =>
        {
            b.ToTable("Vorlagen");
            b.HasKey(x => x.Schluessel);
            b.Property(x => x.Schluessel).HasMaxLength(60);
            b.Property(x => x.Bezeichnung).HasMaxLength(200);
            b.Property(x => x.Betreff).HasMaxLength(300);
            b.Property(x => x.Text).HasMaxLength(8000);
            b.Property(x => x.GeaendertVon).HasMaxLength(200);
            b.Property(x => x.Anredestil).HasMaxLength(20);
        });
    }
}

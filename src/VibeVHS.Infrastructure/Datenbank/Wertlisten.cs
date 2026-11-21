using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;
using VibeVHS.Domain.Gemeinsam;

namespace VibeVHS.Infrastructure.Datenbank;

/// <summary>
/// Listen unveränderlicher Werte (Records) als eigene Tabelle statt JSON-Spalte (1. Normalform): je Eintrag eine Zeile mit Verweis auf
/// den Besitzer und eigener Id. Die Id ist eine monotone UUIDv7 – damit bleibt die Reihenfolge der Liste beim Laden erhalten (EF sortiert
/// eigene Sammlungen nach dem Schlüssel). Beim Ändern werden Einträge ersetzt (löschen + einfügen), nie überschrieben.
/// </summary>
internal static class Wertlisten
{
    public static EntityTypeBuilder<TBesitzer> AlsTabelle<TBesitzer, TWert>(this EntityTypeBuilder<TBesitzer> b,
        Expression<Func<TBesitzer, IEnumerable<TWert>?>> liste, string tabelle, string fremdschluessel,
        Action<OwnedNavigationBuilder<TBesitzer, TWert>>? weiter = null)
        where TBesitzer : class
        where TWert : class
    {
        b.OwnsMany(liste, o =>
        {
            o.ToTable(tabelle);
            o.WithOwner().HasForeignKey(fremdschluessel);
            o.Property<Guid>("Id").HasValueGenerator<V7Erzeuger>();
            o.HasKey("Id");
            o.HasIndex(fremdschluessel);
            weiter?.Invoke(o);
        });
        return b;
    }

    /// <summary>Wie <see cref="AlsTabelle{TBesitzer,TWert}"/> für eine Liste innerhalb eines eigenen Werts (z. B. Rechte der Buchungsrichtlinie).</summary>
    public static OwnedNavigationBuilder<TBesitzer, TTeil> AlsTabelle<TBesitzer, TTeil, TWert>(this OwnedNavigationBuilder<TBesitzer, TTeil> b,
        Expression<Func<TTeil, IEnumerable<TWert>?>> liste, string tabelle, string fremdschluessel)
        where TBesitzer : class
        where TTeil : class
        where TWert : class
    {
        b.OwnsMany(liste, o =>
        {
            o.ToTable(tabelle);
            o.WithOwner().HasForeignKey(fremdschluessel);
            o.Property<Guid>("Id").HasValueGenerator<V7Erzeuger>();
            o.HasKey("Id");
            o.HasIndex(fremdschluessel);
        });
        return b;
    }
}

internal static class Feldlisten
{
    /// <summary>Liste in einem privaten Feld (z. B. <c>_fachbereiche</c>) als Tabelle; die öffentliche Liste der Fachklasse wird ignoriert.</summary>
    public static EntityTypeBuilder<TBesitzer> FeldAlsTabelle<TBesitzer, TWert>(this EntityTypeBuilder<TBesitzer> b, string feld, string oeffentlich,
        string tabelle, string fremdschluessel, int? laenge = null)
        where TBesitzer : class
        where TWert : class
    {
        b.Ignore(oeffentlich);
        b.OwnsMany<TWert>(feld, o =>
        {
            o.ToTable(tabelle);
            o.WithOwner().HasForeignKey(fremdschluessel);
            o.Property<Guid>("Id").HasValueGenerator<V7Erzeuger>();
            o.HasKey("Id");
            o.HasIndex(fremdschluessel);
            if (laenge is { } l)
            {
                foreach (var p in o.OwnedEntityType.GetDeclaredProperties().Where(p => p.ClrType == typeof(string)))
                {
                    p.SetMaxLength(l);
                }
            }
        });
        b.Navigation(feld).UsePropertyAccessMode(PropertyAccessMode.Field);
        return b;
    }

    public static OwnedNavigationBuilder<TBesitzer, TTeil> FeldAlsTabelle<TBesitzer, TTeil, TWert>(this OwnedNavigationBuilder<TBesitzer, TTeil> b,
        string feld, string oeffentlich, string tabelle, string fremdschluessel)
        where TBesitzer : class
        where TTeil : class
        where TWert : class
    {
        b.Ignore(oeffentlich);
        b.OwnsMany<TWert>(feld, o =>
        {
            o.ToTable(tabelle);
            o.WithOwner().HasForeignKey(fremdschluessel);
            o.Property<Guid>("Id").HasValueGenerator<V7Erzeuger>();
            o.HasKey("Id");
            o.HasIndex(fremdschluessel);
        });
        b.Navigation(feld).UsePropertyAccessMode(PropertyAccessMode.Field);
        return b;
    }
}

/// <summary>Erzeugt monotone UUIDv7 (Reihenfolge = Einfügereihenfolge).</summary>
internal sealed class V7Erzeuger : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Ids.Neu();
}

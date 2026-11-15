using VibeVHS.Web.Sicherheit;

namespace VibeVHS.Web.Tests;

/// <summary>Content-Security-Policy: Skripte nur aus eigenen Dateien plus Hash der Blazor-Import-Map, kein 'unsafe-inline' für Skripte oder Style-Elemente.</summary>
public sealed class CspTests
{
    private static string Direktive(string csp, string name)
        => csp.Split(';', StringSplitOptions.TrimEntries).Single(d => d.StartsWith(name + " ", StringComparison.Ordinal));

    [Fact]
    public void Skripte_ohne_unsafe_inline_aber_mit_Hash_der_Import_Map()
    {
        var csp = Sicherheitsheader.Csp("abc=");
        Direktive(csp, "script-src").ShouldBe("script-src 'self' 'sha256-abc='");
        Direktive(csp, "style-src-elem").ShouldBe("style-src-elem 'self'");
        csp.ShouldNotContain("unsafe-eval");
        Direktive(Sicherheitsheader.Csp(null), "script-src").ShouldBe("script-src 'self'");
    }

    [Fact]
    public void Hash_normalisiert_Zeilenenden_wie_der_Browser()
    {
        Sicherheitsheader.HashVon("{\r\n  \"imports\": {}\r\n}").ShouldBe(Sicherheitsheader.HashVon("{\n  \"imports\": {}\n}"));
        // SHA-256 von "" (Base64)
        Sicherheitsheader.HashVon("").ShouldBe("47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=");
    }
}

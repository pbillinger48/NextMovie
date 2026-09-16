using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace NextMovie.Api.Domain.Streaming;

/// <summary>A country someone can say they watch from.</summary>
/// <param name="Code">ISO 3166-1 alpha-2 code.</param>
/// <param name="Name">English name.</param>
internal sealed record Country(string Code, string Name);

/// <summary>
/// The countries a person can say they watch from.
/// </summary>
/// <remarks>
/// Built from the framework's ISO 3166-1 data rather than a list written here.
/// A hardcoded list is wrong the moment a country is added or renamed, and this
/// codebase has already refused to seed one for streaming services on exactly
/// that reasoning.
/// <para>
/// This is every country, not only the ones our availability source covers.
/// Narrowing it would mean another upstream call and another cache, and the
/// failure it prevents is already visible without one: pick a country nothing is
/// known about and the settings page lists no services and says so, rather than
/// quietly claiming everything is unavailable.
/// </para>
/// </remarks>
internal static class Countries
{
    /// <summary>Every country, ordered by name.</summary>
    /// <remarks>
    /// An ordered list rather than a dictionary because this is rendered as a
    /// dropdown. A hash-based collection would enumerate in whatever order its
    /// buckets happen to fall in, which is stable enough to look intentional and
    /// arbitrary enough to be useless.
    /// </remarks>
    public static readonly ImmutableArray<Country> All = Build();

    private static readonly FrozenSet<string> Codes =
        All.Select(country => country.Code).ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether a string is a country code we accept, ignoring case.</summary>
    public static bool IsKnown(string code) => Codes.Contains(code.ToUpperInvariant());

    private static ImmutableArray<Country> Build() =>
        [.. CultureInfo
            .GetCultures(CultureTypes.SpecificCultures)
            .Select(TryResolve)
            .OfType<RegionInfo>()

            // Two letters only. RegionInfo also yields three-letter codes for
            // some cultures, and availability sources speak alpha-2.
            .Where(region => region.TwoLetterISORegionName.Length == 2)
            .DistinctBy(region => region.TwoLetterISORegionName)
            .Select(region => new Country(region.TwoLetterISORegionName, region.EnglishName))

            // Ordinal, so the order does not depend on the server's locale. Two
            // people picking from the same dropdown should see the same one.
            .OrderBy(country => country.Name, StringComparer.Ordinal)];

    private static RegionInfo? TryResolve(CultureInfo culture)
    {
        try
        {
            return new RegionInfo(culture.Name);
        }
        catch (ArgumentException)
        {
            // A specific culture whose region the runtime cannot resolve. Rare,
            // and there is nothing to offer the user for it.
            return null;
        }
    }
}

using System.Globalization;
using System.Text;

namespace NextMovie.Api.Domain.Import;

/// <summary>
/// Resolves a Letterboxd row to a TMDb film, or declines to.
/// </summary>
/// <remarks>
/// Letterboxd has no public API, so an export identifies films by title and year
/// only. Every rating imported has to survive this. The rules here are the ones
/// the [spike](../../../../docs/spikes/letterboxd-tmdb-matching.md) measured
/// against a real 796-film library, where they resolved 99.1% of actual films.
/// <para>
/// Pure, and takes its candidates rather than fetching them, so every rule can be
/// tested against the spike's real examples without a network call.
/// </para>
/// <para>
/// The governing principle: <b>a wrong match is worse than a visible failure.</b>
/// A rating attached to the wrong film is invisible, permanent and poisons the
/// recommendation engine; an unresolved row is a line on a reconciliation screen.
/// Every threshold here is set with that asymmetry in mind.
/// </para>
/// </remarks>
public static class LetterboxdMatcher
{
    /// <summary>
    /// Letterboxd records the festival premiere where TMDb records the wide
    /// release, and vice versa. The spike found 16 films in 796 off by exactly
    /// one year — systematic enough that this tolerance is required, not optional.
    /// </summary>
    public const int YearTolerance = 1;

    /// <summary>
    /// Below this, a vote count is not evidence of anything — a film with 9 votes
    /// beating one with 0 says nothing about which the user meant.
    /// </summary>
    public const int MinimumVotesToTiebreak = 50;

    /// <summary>
    /// How far ahead the leader must be to win on votes alone.
    /// </summary>
    /// <remarks>
    /// The tightest genuine margins the spike observed were better than 10:1
    /// (Justice League 13,905 vs 1,161). Cases below that bar — <c>How to Make a
    /// Killing</c> at 720 vs 399 — were genuinely too close to call, and go to a
    /// person instead.
    /// </remarks>
    public const int TiebreakRatio = 10;

    /// <summary>Matches one export row against the films TMDb offered for it.</summary>
    public static MatchOutcome Match(LetterboxdEntry entry, IReadOnlyList<MatchCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return new MatchOutcome.Unresolved();
        }

        var name = Normalize(entry.Name);

        // Exact title first, and only then the looser subtitle rule. Trying them
        // together would let "Alien" match "Alien: Resurrection" while a film
        // actually called "Alien" sat in the same list.
        var exact = candidates.Where(candidate => Normalize(candidate.Title) == name).ToList();

        if (exact.Count > 0)
        {
            return Decide(exact, entry.Year, MatchMethod.Exact);
        }

        var subtitled = candidates
            .Where(candidate => Normalize(candidate.Title).StartsWith(name + ":", StringComparison.Ordinal))
            .ToList();

        return subtitled.Count > 0
            ? Decide(subtitled, entry.Year, MatchMethod.Subtitle)
            : new MatchOutcome.Unresolved();
    }

    /// <summary>
    /// Narrows title matches by year, then by votes, then gives up.
    /// </summary>
    private static MatchOutcome Decide(
        List<MatchCandidate> titleMatches,
        int? year,
        MatchMethod method)
    {
        var plausible = year is null
            ? titleMatches
            : titleMatches
                .Where(candidate => candidate.ReleaseYear is { } released
                    && Math.Abs(released - year.Value) <= YearTolerance)
                .ToList();

        if (plausible.Count == 0)
        {
            // The title matched but no year is close. Rather than reaching past
            // the tolerance, this goes to a person: TMDb release dates are not
            // immutable — the spike found Hamilton's move from 2020 to 2025 after
            // a re-release — so a wide year window would quietly mis-key films.
            return new MatchOutcome.Ambiguous(titleMatches);
        }

        if (plausible.Count == 1)
        {
            return new MatchOutcome.Matched(plausible[0].TmdbId, method);
        }

        // Several films share this title and year. TMDb genuinely carries obscure
        // shorts and student films alongside the one the user means.
        var ranked = plausible.OrderByDescending(candidate => candidate.VoteCount).ToList();
        var leader = ranked[0];
        var runnerUp = ranked[1];

        var decisive = leader.VoteCount >= MinimumVotesToTiebreak
            && leader.VoteCount >= runnerUp.VoteCount * TiebreakRatio;

        return decisive
            ? new MatchOutcome.Matched(leader.TmdbId, MatchMethod.VoteCount)
            : new MatchOutcome.Ambiguous(ranked);
    }

    /// <summary>
    /// Reduces a title to a form two sources can be compared on.
    /// </summary>
    /// <remarks>
    /// Case, surrounding whitespace, accents, punctuation style and a leading
    /// article are all places where two catalogues legitimately disagree about
    /// the same film while meaning the same film. Everything else is left alone:
    /// the more this normalises, the more different films it can collide, and a
    /// collision here is a wrong match.
    /// <para>
    /// Every rule here was put in by a real failure against a real 796-film
    /// library, not anticipated. Adding one on a hunch is how collisions get in.
    /// </para>
    /// </remarks>
    internal static string Normalize(string title)
    {
        var folded = FoldLeadingArticle(FoldTrailingArticle(title.Trim()));

        // Strip diacritics so "Amelie" matches "Amélie", then compare in lower
        // case and with runs of whitespace collapsed.
        var decomposed = folded.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(char.ToLowerInvariant(FoldPunctuation(character)));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Collapses typographic punctuation onto its plain ASCII equivalent.
    /// </summary>
    /// <remarks>
    /// Letterboxd writes <c>Mission: Impossible – Fallout</c> with an en dash;
    /// TMDb writes the same film with a hyphen. Eleven films in one real library
    /// failed on that single character — Mission: Impossible, Star Wars, John
    /// Wick and The Hunger Games all use it.
    /// <para>
    /// Purely orthographic, so it cannot collide two genuinely different films:
    /// no pair of films differs only in which dash or apostrophe someone typed.
    /// </para>
    /// </remarks>
    private static char FoldPunctuation(char character) => character switch
    {
        // En dash, em dash, figure dash, minus sign, non-breaking hyphen.
        '\u2013' or '\u2014' or '\u2012' or '\u2212' or '\u2011' => '-',

        // Curly quotes, which Letterboxd and TMDb also disagree about.
        '\u2018' or '\u2019' => '\'',
        '\u201C' or '\u201D' => '"',

        _ => character,
    };

    /// <summary>
    /// Drops a leading "The", "A" or "An".
    /// </summary>
    /// <remarks>
    /// The spike's third heuristic, and one this originally got wrong by folding
    /// trailing articles instead. Letterboxd has <c>School of Rock</c> where TMDb
    /// has <c>The School of Rock</c>; neither is wrong, and both mean the film
    /// everyone calls School of Rock.
    /// <para>
    /// Applied to both sides, so the comparison never privileges one catalogue's
    /// house style. It could in principle collide two films differing only by a
    /// leading article — the year and vote-count rules still stand behind it, and
    /// no such pair turned up in 796 real films.
    /// </para>
    /// </remarks>
    private static string FoldLeadingArticle(string title)
    {
        foreach (var article in (string[])["The ", "A ", "An "])
        {
            if (title.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                return title[article.Length..];
            }
        }

        return title;
    }

    /// <summary>
    /// Turns "Matrix, The" into "The Matrix".
    /// </summary>
    /// <remarks>
    /// Catalogues that sort by title often store the article at the end. Folding
    /// it back recovers a handful of films per library — and folding it into
    /// exactly one form means "The Matrix" and "Matrix, The" compare equal
    /// without either spelling being privileged.
    /// </remarks>
    private static string FoldTrailingArticle(string title)
    {
        foreach (var article in (string[])["The", "A", "An"])
        {
            var suffix = ", " + article;

            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return $"{article} {title[..^suffix.Length]}";
            }
        }

        return title;
    }
}

using System.Globalization;
using System.Text;
using NextMovie.Api.Features.Recommendations;

namespace NextMovie.Eval;

/// <summary>Prints an evaluation so a person can act on it.</summary>
/// <remarks>
/// The caveats are printed with the numbers rather than left in the ADR. A recall
/// figure read without them looks like accuracy, and the whole point of this tool
/// is that it is a lower bound on quality (ADR-0012).
/// </remarks>
internal static class Report
{
    public static string Render(EvaluationOptions options, EvaluationReport report)
    {
        var text = new StringBuilder();

        text.AppendLine();
        text.AppendLine("  RECOMMENDATION EVALUATION");
        text.AppendLine("  ─────────────────────────────────────────────────────────────");
        text.AppendLine(Line("library", $"{report.Library} rated, {report.Loved} loved (≥ {options.LovedAtLeast:0.0})"));
        text.AppendLine(Line("method", $"{report.HiddenPerTrial} hidden per trial × {options.Trials} trials, seed {options.Seed}"));
        text.AppendLine(Line("asked for", $"{options.Count} films"));
        text.AppendLine(Line("took", $"{report.Elapsed.TotalSeconds:0.0}s"));
        text.AppendLine();

        text.AppendLine("  FOUND AGAIN");
        text.AppendLine(Line("recall", Percent(report.Aggregate.Recall)));
        text.AppendLine(Line("MRR", report.Aggregate.MeanReciprocalRank.ToString("0.000", CultureInfo.InvariantCulture)));
        text.AppendLine(Line("hit rate", Percent(report.Aggregate.HitRate)));
        text.AppendLine();

        text.AppendLine("  LIST QUALITY (final trial)");
        text.AppendLine(Line("returned", report.Quality.Count.ToString(CultureInfo.InvariantCulture)));
        text.AppendLine(Line("median TMDb", Rating(report.Quality.MedianRating)));
        text.AppendLine(Line("lowest", Rating(report.Quality.LowestRating)));
        text.AppendLine(Line("below floor", report.Quality.BelowFloor == 0
            ? "0"
            : $"{report.Quality.BelowFloor}  ← the floor is not holding"));
        text.AppendLine(Line("distinct genres", report.Quality.DistinctGenres.ToString(CultureInfo.InvariantCulture)));
        text.AppendLine(Line("streamable now", $"{report.Quality.Streamable} of {report.Quality.Count}"));
        text.AppendLine();

        text.AppendLine("  FINAL TRIAL");

        foreach (var film in report.Sample)
        {
            var marks = new StringBuilder();

            // The hidden films are the point of the exercise, so they are the one
            // thing marked rather than one column among several.
            marks.Append(film.WasHidden ? " ← HELD OUT" : string.Empty);
            marks.Append(film.CanStreamNow ? " ▸" : string.Empty);

            text.AppendLine(
                $"   {film.Rank,2}. {Rating(film.Rating),-5}  {Truncate(film.Title, 44),-44}{marks}");
        }

        if (report.Sample.Count == 0)
        {
            text.AppendLine("   (nothing returned)");
        }

        text.AppendLine();
        text.AppendLine("  ─────────────────────────────────────────────────────────────");
        text.AppendLine("  Recall is a LOWER BOUND, not accuracy: most good recommendations");
        text.AppendLine("  are films never rated and are invisible to it. The held-out set is");
        text.AppendLine("  biased toward discoverable films — they are the ones that got");
        text.AppendLine("  watched. n=1. Compare runs at the same seed; a single number on");
        text.AppendLine("  its own means little. (ADR-0012)");
        text.AppendLine();

        return text.ToString();
    }

    /// <summary>Prints a personalisation check.</summary>
    /// <remarks>
    /// The verdict bands are judgement, not science, and are labelled as such
    /// where they print. A coefficient with no reading attached gets read as
    /// whatever the reader already believed.
    /// </remarks>
    public static string Render(EvaluationOptions options, PersonalisationReport report)
    {
        var text = new StringBuilder();
        var overlap = report.Overlap;

        text.AppendLine();
        text.AppendLine("  PERSONALISATION CHECK");
        text.AppendLine("  ─────────────────────────────────────────────────────────────");
        text.AppendLine(Line("cohorts", $"{overlap.Cohorts} contrasting tastes × {options.Count} films"));
        text.AppendLine(Line("took", $"{report.Elapsed.TotalSeconds:0.0}s"));
        text.AppendLine();

        text.AppendLine("  HOW ALIKE THE ANSWERS ARE");
        text.AppendLine(Line("whole list", $"{overlap.MeanPairwise:0.00}  {Verdict(overlap.MeanPairwise)}"));
        text.AppendLine(Line($"top {report.HeadSize}", $"{report.Head.MeanPairwise:0.00}  {Verdict(report.Head.MeanPairwise)}"));
        text.AppendLine(Line("in every list", overlap.ListSize > 0
            ? $"{overlap.Universal.Count} of {overlap.ListSize}"
            : overlap.Universal.Count.ToString(CultureInfo.InvariantCulture)));

        if (report.Head.MeanPairwise > overlap.MeanPairwise + 0.1)
        {
            // Worth saying outright. The two numbers side by side are easy to
            // read past, and the gap between them is the finding.
            text.AppendLine();
            text.AppendLine($"   ⚠ The opening slots agree more than the lists do. Tastes are being");
            text.AppendLine($"     read in the tail, where nobody looks, and not at the top.");
        }

        text.AppendLine();

        text.AppendLine("  BY TASTE");

        foreach (var cohort in report.Cohorts)
        {
            // The sourcing chain, because "not recommended" has two completely
            // different causes and only this tells them apart: a genre never
            // queried is a ceiling nothing downstream can lift, while candidates
            // that competed and lost are a ranking decision a weight could change.
            var sourcing = cohort.Queried
                ? $"queried, {cohort.Contending,3} of {cohort.PoolSize,3} competed"
                : $"NOT QUERIED{new string(' ', 14)}";

            text.AppendLine(
                $"   {Truncate(cohort.Taste, 16),-16} from {cohort.Library,3} films   "
                + $"{sourcing}   {cohort.OnTaste,2}/{cohort.Films.Count,-2} shown   "
                + $"{cohort.SharedWithOthers,2}/{cohort.Films.Count,-2} shared");

            // The top few by name, because a coefficient cannot be sanity-checked
            // and a list of films can. Every real fault in this project was found
            // by reading output, not by reading a number.
            text.AppendLine($"     {string.Join("  ·  ", cohort.Titles.Take(3).Select(title => Truncate(title, 24)))}");

            if (cohort.PassedOver is { } race)
            {
                text.AppendLine($"     lost:  {Race(race.LoserTitle, race.Loser)}");
                text.AppendLine($"     beat:  {Race(race.WinnerTitle, race.Winner)}");

                if (race.Loser.Score > race.Winner.Score)
                {
                    // Scores are not the last word. Diversification drops a film
                    // whose genres already had their turn, and availability
                    // nudges the order afterwards, so the better-scoring film can
                    // still be the one left out. Said here because otherwise
                    // these two lines look like an arithmetic bug.
                    text.AppendLine("            (outscored it — dropped by diversification or availability)");
                }
            }
        }

        if (report.UniversalTitles.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("  OFFERED TO EVERY TASTE");

            foreach (var title in report.UniversalTitles)
            {
                text.AppendLine($"   · {Truncate(title, 56)}");
            }
        }

        var onTaste = report.Cohorts.Sum(cohort => cohort.OnTaste);
        var offered = report.Cohorts.Sum(cohort => cohort.Films.Count);

        text.AppendLine();
        text.AppendLine(Line("on taste", offered == 0
            ? "—"
            : $"{onTaste} of {offered}  ({(double)onTaste / offered * 100:0}% carry the genre asked for)"));

        text.AppendLine();
        text.AppendLine("  ─────────────────────────────────────────────────────────────");
        text.AppendLine("  Two lists can differ completely and both be wrong for their reader,");
        text.AppendLine("  so 'on taste' matters as much as overlap: it asks whether a list is");
        text.AppendLine("  ABOUT the person it was made for.");
        text.AppendLine();
        text.AppendLine("  A HIGH overlap is BAD. These readers were built from deliberately");
        text.AppendLine("  different films, so films they are all offered are films chosen");
        text.AppendLine("  without reference to them. Genres share films, so some overlap is");
        text.AppendLine("  honest — a crime fan and a drama fan really do want some of the");
        text.AppendLine("  same things. The bands above are judgement, not science.");
        text.AppendLine();

        return text.ToString();
    }

    /// <remarks>
    /// Thresholds chosen to be legible rather than derived — there is no
    /// principled cut-off, and pretending otherwise would dress a guess as a
    /// measurement. They exist so a number nobody has a feel for still says
    /// something on first reading.
    /// </remarks>
    private static string Verdict(double overlap) => overlap switch
    {
        >= 0.6 => "← these are essentially the same list",
        >= 0.3 => "← partly personalised",
        _ => "← the lists genuinely differ",
    };

    /// <remarks>
    /// Weighted contributions, not raw component values. Raw values invite the
    /// reader to compare a taste of 0.31 with a quality of 0.82 as though the two
    /// were commensurable, and they are not — one is worth three times the other
    /// before either is compared.
    /// </remarks>
    private static string Race(string title, ContendingFilm film)
    {
        var parts = film.Breakdown is { } breakdown
            ? $"taste {breakdown.WeightedTaste:0.000}  quality {breakdown.WeightedQuality:0.000}  "
              + $"interaction {breakdown.WeightedInteraction:0.000}"
            : "no breakdown";

        return $"{Truncate(title, 28),-28} {film.Score:0.000}   {parts}";
    }

    private static string Line(string label, string value) => $"  {label,-16} {value}";

    private static string Percent(double share) =>
        (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Rating(double? rating) =>
        rating is null ? "—" : rating.Value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Truncate(string title, int width) =>
        title.Length <= width ? title : string.Concat(title.AsSpan(0, width - 1), "…");
}

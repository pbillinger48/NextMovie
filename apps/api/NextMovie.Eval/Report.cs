using System.Globalization;
using System.Text;

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

    private static string Line(string label, string value) => $"  {label,-16} {value}";

    private static string Percent(double share) =>
        (share * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private static string Rating(double? rating) =>
        rating is null ? "—" : rating.Value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string Truncate(string title, int width) =>
        title.Length <= width ? title : string.Concat(title.AsSpan(0, width - 1), "…");
}

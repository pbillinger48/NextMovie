using System.Globalization;

namespace NextMovie.Eval;

/// <summary>Which question an evaluation run is asking.</summary>
internal enum EvaluationMode
{
    /// <summary>Hide favourites and measure whether they come back (ADR-0012).</summary>
    HoldOut = 1,

    /// <summary>
    /// Compare what deliberately different tastes are offered.
    /// </summary>
    /// <remarks>
    /// The check hold-out cannot perform: a recommender that returns the same
    /// canonical films to everybody scores well on recall, because most people
    /// have seen the canon.
    /// </remarks>
    Personalisation = 2,
}

/// <summary>How one evaluation should be run.</summary>
/// <param name="Email">Whose library to evaluate.</param>
/// <param name="HoldOut">Share of their favourite films to hide, 0 to 1.</param>
/// <param name="Count">How many recommendations to ask for.</param>
/// <param name="Trials">How many hold-out splits to run and average.</param>
/// <param name="Seed">
/// Fixes every split. Two runs of different code with the same seed hide the same
/// films, which is the only thing that makes their scores comparable (ADR-0012).
/// </param>
/// <param name="LovedAtLeast">Rating that counts as a favourite.</param>
/// <param name="Mode">Which question to ask.</param>
/// <param name="Cohorts">How many contrasting tastes to compare, in personalisation mode.</param>
/// <param name="Tastes">
/// Genres to compare, named explicitly. Empty means "the largest" — which is the
/// safest default for reading a taste and the <em>weakest</em> test of
/// personalisation, because the biggest genres are the ones that share the most
/// films with each other.
/// </param>
internal sealed record EvaluationOptions(
    string Email,
    double HoldOut,
    int Count,
    int Trials,
    int Seed,
    decimal LovedAtLeast,
    EvaluationMode Mode,
    int Cohorts,
    IReadOnlyList<string> Tastes)
{
    private const string Usage = """
        Usage: dotnet run --project apps/api/NextMovie.Eval -- [options]

          --email <address>    Whose library to evaluate.        (required)
          --holdout <0-1>      Share of favourites to hide.      (default 0.2)
          --count <n>          Recommendations to request.       (default 12)
          --trials <n>         Splits to run and average.        (default 5)
          --seed <n>           Fixes the splits.                 (default 1)
          --loved <0.5-5.0>    Rating that counts as a favourite.(default 4.5)
          --mode <name>        holdout | personalisation.        (default holdout)
          --cohorts <n>        Tastes to compare.                (default 6)
          --tastes <a,b,c>     Name the genres instead of taking the largest.

        holdout         Hides favourites and measures whether they come back.
        personalisation Builds readers with contrasting tastes and measures how
                        much their recommendations overlap. A HIGH overlap is bad:
                        it means the engine is reciting the canon, not reading
                        anybody.
        """;

    /// <summary>Reads options from the command line, or explains what was wrong.</summary>
    /// <remarks>
    /// Hand-parsed rather than pulled from a command-line library. Six flags do
    /// not justify a dependency, and the whole tool is one file's worth of
    /// argument handling away from working.
    /// </remarks>
    public static bool TryParse(string[] args, out EvaluationOptions options, out string error)
    {
        options = null!;
        error = string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                error = $"Could not read '{args[index]}'.\n\n{Usage}";

                return false;
            }

            values[args[index][2..]] = args[index + 1];
        }

        if (!values.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email))
        {
            error = $"An --email is required.\n\n{Usage}";

            return false;
        }

        var mode = EvaluationMode.HoldOut;

        if (values.TryGetValue("mode", out var raw)
            && !Enum.TryParse(raw.Replace("-", string.Empty), ignoreCase: true, out mode))
        {
            error = $"Unknown mode '{raw}'.\n\n{Usage}";

            return false;
        }

        options = new EvaluationOptions(
            Email: email.Trim(),
            HoldOut: Number(values, "holdout", 0.2),
            Count: (int)Number(values, "count", 12),
            Trials: (int)Number(values, "trials", 5),
            Seed: (int)Number(values, "seed", 1),
            LovedAtLeast: (decimal)Number(values, "loved", 4.5),
            Mode: mode,
            Cohorts: (int)Number(values, "cohorts", 6),
            Tastes: values.TryGetValue("tastes", out var tastes)
                ? [.. tastes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                : []);

        if (options.HoldOut is <= 0 or > 1)
        {
            error = "--holdout must be greater than 0 and at most 1.";

            return false;
        }

        if (options.Count < 1 || options.Trials < 1)
        {
            error = "--count and --trials must both be at least 1.";

            return false;
        }

        if (options.Cohorts < 2)
        {
            error = "--cohorts must be at least 2. Comparing one list to nothing proves nothing.";

            return false;
        }

        return true;
    }

    private static double Number(IReadOnlyDictionary<string, string> values, string name, double fallback) =>
        values.TryGetValue(name, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}

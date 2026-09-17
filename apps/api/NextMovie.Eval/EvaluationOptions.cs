using System.Globalization;

namespace NextMovie.Eval;

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
internal sealed record EvaluationOptions(
    string Email,
    double HoldOut,
    int Count,
    int Trials,
    int Seed,
    decimal LovedAtLeast)
{
    private const string Usage = """
        Usage: dotnet run --project apps/api/NextMovie.Eval -- [options]

          --email <address>    Whose library to evaluate.        (required)
          --holdout <0-1>      Share of favourites to hide.      (default 0.2)
          --count <n>          Recommendations to request.       (default 12)
          --trials <n>         Splits to run and average.        (default 5)
          --seed <n>           Fixes the splits.                 (default 1)
          --loved <0.5-5.0>    Rating that counts as a favourite.(default 4.5)
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

        options = new EvaluationOptions(
            Email: email.Trim(),
            HoldOut: Number(values, "holdout", 0.2),
            Count: (int)Number(values, "count", 12),
            Trials: (int)Number(values, "trials", 5),
            Seed: (int)Number(values, "seed", 1),
            LovedAtLeast: (decimal)Number(values, "loved", 4.5));

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

        return true;
    }

    private static double Number(IReadOnlyDictionary<string, string> values, string name, double fallback) =>
        values.TryGetValue(name, out var raw)
        && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}

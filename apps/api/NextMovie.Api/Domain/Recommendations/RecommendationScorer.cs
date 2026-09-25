namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// Ranks candidate films against a taste profile, and says why.
/// </summary>
/// <remarks>
/// The weights below are ours, not the ones in
/// <c>docs/recommendation-engine.md</c>. That model spends 60% of its weight on
/// director similarity, themes, actors and a feedback model — none of which we
/// store (ADR-0008). Rather than keep the shape and quietly zero most of it,
/// these are the weights for the signals we actually have, chosen deliberately.
/// <para>
/// Every reason returned is derived from a component that materially moved the
/// score. An explanation that does not correspond to the ranking is a lie with
/// good manners, and it is the kind of lie users eventually catch.
/// </para>
/// </remarks>
public static class RecommendationScorer
{
    /// <summary>
    /// How much each signal contributes. They sum to 1.
    /// </summary>
    /// <remarks>
    /// Taste is about <em>this person</em>; quality is about the film; the
    /// interaction between them is where most of the signal actually lives.
    /// Popularity is deliberately the smallest: ranking by it would recommend the
    /// same films to everybody.
    /// </remarks>
    /// <summary>
    /// How good the film is, by the wider audience's reckoning.
    /// </summary>
    /// <remarks>
    /// The largest weight, and deliberately so. The purpose is to recommend the
    /// <b>best</b> films somebody would enjoy, not the most typical ones — and an
    /// earlier version of this got that backwards, returning films rated 6.0 and
    /// 6.3 from a catalogue holding a hundred films rated above 8.
    /// </remarks>
    public const double QualityWeight = 0.45;

    /// <summary>How much the film is this person's kind of thing.</summary>
    /// <remarks>
    /// Smaller than quality, because taste decides <em>among</em> good films
    /// rather than excusing a mediocre one. A film nobody rates highly is not
    /// rescued by being in a genre somebody watches a lot of.
    /// </remarks>
    public const double TasteWeight = 0.15;

    /// <summary>
    /// What a good film in a genre you actually watch is worth, beyond the two
    /// separately.
    /// </summary>
    /// <remarks>
    /// The largest single weight, and the correction that matters most. Added
    /// independently, taste and quality let a well-reviewed film be dragged down
    /// by a genre score and a genre you love be filled with mediocrity.
    /// Multiplied, they say the thing we actually mean: recommend the <em>good
    /// ones</em> from the kinds of film this person watches. It is why a
    /// well-reviewed animated film now ranks where it should for someone with
    /// ninety-seven animated films behind them.
    /// </remarks>
    public const double InteractionWeight = 0.30;

    public const double RuntimeWeight = 0.05;
    public const double EraWeight = 0.05;

    /// <summary>
    /// The rating below which a film is not recommended at all.
    /// </summary>
    /// <remarks>
    /// A floor rather than a penalty. Weights let a bad film win on other
    /// grounds — being the right length, or filling a thin genre slot in a
    /// diversified list — and no weighting survives that. Returning six good
    /// films beats returning twelve with three duds in it.
    /// </remarks>
    public const double MinimumRating = 6.5;

    /// <summary>
    /// The votes a rating needs before it is believed.
    /// </summary>
    /// <remarks>
    /// Nine out of ten from a dozen people is not evidence of anything, and the
    /// bar has to be well clear of that. Two failures set it here. A
    /// direct-to-video children's film reached a list at 7.5 from 205 votes,
    /// rated by exactly the audience that sought it out. Then films released
    /// weeks earlier arrived at 9.1 from a thousand votes, carrying the ratings
    /// of the people most excited to see them — reputations that had not yet
    /// settled.
    /// <para>
    /// Five thousand is where the classics sit comfortably (Psycho has eleven
    /// thousand, City of God eight) and hype has not yet reached. It excludes some
    /// genuinely good films with smaller audiences, which is the deliberate cost
    /// of asking for the best rather than the most agreeable.
    /// </para>
    /// <para>
    /// Films whose vote count we have not yet fetched are judged on rating alone
    /// rather than excluded, since the alternative is excluding most of the
    /// catalogue.
    /// </para>
    /// </remarks>
    public const int MinimumVotes = 5_000;

    /// <summary>
    /// The rating the quality scale treats as worth nothing.
    /// </summary>
    /// <remarks>
    /// <b>Not the floor.</b> These were one constant until offline evaluation
    /// showed what that cost: <see cref="MinimumRating"/> decided both what was
    /// admissible and where the scale began, so widening the scale meant lowering
    /// the bar. They are separate now, and only this pair moves.
    /// <para>
    /// The scale ran 6.5–8.5, which made one TMDb point worth half the range —
    /// more, weighted, than the taste term can contribute at its maximum. A film
    /// rated 7.95 in the viewer's favourite genre lost to one rated 8.39 outside
    /// it, every time, by margins as small as 0.002. Quality is still the largest
    /// single weight; it simply no longer settles the question before taste is
    /// consulted.
    /// </para>
    /// </remarks>
    private const double WorthlessRating = 5.0;

    /// <summary>The rating the quality scale treats as perfect.</summary>
    /// <remarks>
    /// Above TMDb's practical ceiling on purpose. Almost nothing rates above 8.7
    /// with a real vote count, so this keeps the top of the scale from
    /// compressing the films that actually compete.
    /// </remarks>
    private const double ExcellentRating = 9.5;

    /// <summary>
    /// The affinity above which a genre is worth naming as a reason.
    /// </summary>
    /// <remarks>
    /// Affinity runs 0–1 with a half meaning no opinion, so this is "clearly
    /// above indifference" rather than merely non-negative.
    /// </remarks>
    private const double NotableAffinity = 0.6;

    /// <summary>Ratings needed before a recommendation is called confident.</summary>
    private const int ConfidentRatings = 100;

    private const int ModestlyConfidentRatings = 25;

    /// <summary>Scores one candidate.</summary>
    /// <param name="candidate">The film being considered.</param>
    /// <param name="profile">What this person watches.</param>
    /// <param name="genreNames">For phrasing the reasons.</param>
    /// <param name="genreInformativeness">
    /// How much each genre distinguishes one candidate from another in the pool
    /// being ranked. See <see cref="Informativeness"/>.
    /// </param>
    public static ScoredRecommendation Score(
        RecommendationCandidate candidate,
        TasteProfile profile,
        IReadOnlyDictionary<int, string> genreNames,
        IReadOnlyDictionary<int, double>? genreInformativeness = null)
    {
        var taste = GenreScore(candidate, profile, genreInformativeness);
        var quality = CommunityScore(candidate);
        var runtime = RangeScore(candidate.Runtime, profile.PreferredRuntimes, tolerance: 30);
        var era = RangeScore(candidate.ReleaseYear, profile.PreferredEra, tolerance: 15);

        var score =
            (taste * TasteWeight)
            + (quality * QualityWeight)
            + (taste * quality * InteractionWeight)
            + (runtime * RuntimeWeight)
            + (era * EraWeight);

        return new ScoredRecommendation(
            candidate.MovieId,
            Math.Clamp(score, 0, 1),
            Reasons(candidate, profile, genreNames, runtime, era),
            ConfidenceFor(profile, candidate))
        {
            Breakdown = new ScoreBreakdown(taste, quality, runtime, era),
        };
    }

    /// <summary>
    /// How well the candidate's genres match what this person likes.
    /// </summary>
    /// <remarks>
    /// Averaged across the film's genres rather than taking the best one: a film
    /// that is part science fiction and part something the user dislikes is a
    /// worse bet than one that is wholly science fiction, and taking the maximum
    /// would rate them the same.
    /// </remarks>
    private static double GenreScore(
        RecommendationCandidate candidate,
        TasteProfile profile,
        IReadOnlyDictionary<int, double>? informativeness)
    {
        if (candidate.GenreIds.Count == 0 || profile.GenreAffinity.Count == 0)
        {
            // Nothing known either way. Neutral, not zero — zero would rank an
            // unknown film below one the user actively dislikes.
            return TasteProfile.NoOpinion;
        }

        // A weighted average across the film's genres rather than the best of
        // them: a film that is half something the user avoids is a worse bet than
        // one wholly in a genre they seek out, and a maximum would rate the two
        // the same.
        //
        // The weights are how much each genre distinguishes this film from the
        // others being ranked. When every candidate is a drama, being a drama
        // says nothing, and the genres that differ decide instead.
        var weighted = 0.0;
        var weights = 0.0;

        foreach (var genreId in candidate.GenreIds)
        {
            var weight = informativeness?.GetValueOrDefault(genreId, 1.0) ?? 1.0;

            weighted += profile.AffinityFor(genreId) * weight;
            weights += weight;
        }

        return weights <= 0
            ? TasteProfile.NoOpinion
            : Math.Clamp(weighted / weights, 0, 1);
    }

    /// <summary>
    /// How much each genre distinguishes one candidate from another.
    /// </summary>
    /// <remarks>
    /// A genre shared by nearly every candidate carries almost no information
    /// about which to prefer — the same reason a search engine discounts common
    /// words. Drama is 40% of all films; "you watch a lot of drama" is close to
    /// "you watch films".
    /// <para>
    /// Measured across the pool being ranked rather than the whole catalogue,
    /// which makes it self-correcting: when every candidate happens to be a
    /// drama, drama stops deciding and the genres that actually differ take over.
    /// </para>
    /// </remarks>
    public static Dictionary<int, double> Informativeness(
        IReadOnlyList<RecommendationCandidate> pool)
    {
        if (pool.Count == 0)
        {
            return [];
        }

        var counts = new Dictionary<int, int>();

        foreach (var genreId in pool.SelectMany(candidate => candidate.GenreIds.Distinct()))
        {
            counts[genreId] = counts.GetValueOrDefault(genreId) + 1;
        }

        return counts.ToDictionary(
            entry => entry.Key,

            // Inverse document frequency, floored so a ubiquitous genre is
            // discounted rather than silenced: it still carries a little signal.
            entry => Math.Max(0.1, Math.Log((double)pool.Count / entry.Value) + 0.1));
    }

    /// <summary>
    /// How good the film is, across the range that actually separates them.
    /// </summary>
    /// <remarks>
    /// Stretched over <see cref="WorthlessRating"/> to <see cref="ExcellentRating"/>
    /// rather than 0–10, so a point of difference still means something — but no
    /// longer over so narrow a band that a few tenths decide every ranking on
    /// their own.
    /// </remarks>
    private static double CommunityScore(RecommendationCandidate candidate) =>
        candidate.CommunityRating is { } rating
            ? Math.Clamp((rating - WorthlessRating) / (ExcellentRating - WorthlessRating), 0, 1)

            // No community verdict at all. Not zero, but well below anything with
            // a real reputation. Unreachable through recommendations — the floor
            // already refuses a film with no rating — and kept for callers that
            // score a candidate without going through it.
            : 0.2;

    /// <summary>
    /// Whether a film is good enough to be worth anybody's evening.
    /// </summary>
    /// <remarks>
    /// Applied before ranking rather than as a penalty within it, because a
    /// penalty can always be outvoted. Films with too few votes are refused for
    /// the same reason: their rating is not yet a fact about the film.
    /// </remarks>
    public static bool IsWorthRecommending(RecommendationCandidate candidate, DateOnly today) =>
        candidate.CommunityRating >= MinimumRating
        && candidate.VoteCount is null or >= MinimumVotes

        // Out already. A film nobody can watch is not a recommendation, and
        // unreleased films carry the most flattering ratings on TMDb — scored by
        // the people most excited about them, before anyone has been
        // disappointed.
        && candidate.ReleaseDate is { } released
        && released <= today;

    /// <summary>
    /// How near a value falls to a preferred range, fading out beyond it.
    /// </summary>
    /// <remarks>
    /// Inside the range scores full marks; outside, it decays linearly over
    /// <paramref name="tolerance"/> rather than dropping to nothing at the edge.
    /// A 145-minute film is not disqualified for someone whose usual range ends
    /// at 140.
    /// </remarks>
    private static double RangeScore(int? value, Range<int>? preferred, int tolerance)
    {
        if (value is not { } actual || preferred is null)
        {
            return 0.5;
        }

        if (preferred.Contains(actual))
        {
            return 1.0;
        }

        var distance = actual < preferred.From ? preferred.From - actual : actual - preferred.To;

        return Math.Clamp(1.0 - ((double)distance / tolerance), 0, 1);
    }

    /// <summary>
    /// The reasons that actually moved this recommendation.
    /// </summary>
    /// <remarks>
    /// Built from the same components as the score, and only where a component
    /// was genuinely favourable. A list padded with "it is a film" would make
    /// every recommendation look identical, which is how explanations stop being
    /// read.
    /// </remarks>
    private static List<string> Reasons(
        RecommendationCandidate candidate,
        TasteProfile profile,
        IReadOnlyDictionary<int, string> genreNames,
        double runtimeScore,
        double eraScore)
    {
        var reasons = new List<string>();

        // Quality leads, because it is now what mostly decides the ranking. A
        // reason list that opened with a genre while an 8.6 rating did the work
        // would be explaining the wrong thing.
        if (candidate.CommunityRating is { } rating and >= 7.5)
        {
            reasons.Add(rating >= 8.0
                ? $"Widely considered excellent — {rating:0.0} on TMDb"
                : $"Well regarded — {rating:0.0} on TMDb");
        }

        var strongGenres = candidate.GenreIds
            .Where(genreId => profile.AffinityFor(genreId) >= NotableAffinity)
            .OrderByDescending(profile.AffinityFor)
            .Select(genreId => genreNames.GetValueOrDefault(genreId))
            .OfType<string>()
            .Take(2)
            .ToList();

        if (strongGenres.Count > 0)
        {
            // Phrased as what they watch, not how they score it. The affinity
            // behind this is mostly appetite, and claiming they "rate it highly"
            // would be a reason that does not match the evidence.
            reasons.Add($"You watch a lot of {string.Join(" and ", strongGenres)}");
        }

        if (runtimeScore >= 1.0 && candidate.Runtime is { } runtime)
        {
            reasons.Add($"At {runtime} minutes, about the length you usually enjoy");
        }

        if (eraScore >= 1.0 && candidate.ReleaseYear is { } year)
        {
            reasons.Add($"From {year}, in the period you watch most");
        }

        return reasons;
    }

    /// <summary>
    /// How much evidence stands behind this particular recommendation.
    /// </summary>
    /// <remarks>
    /// Two things, and the weaker wins. How much the person has rated at all, and
    /// whether they have rated anything in <em>this film's</em> genres — a
    /// thousand ratings say nothing useful about a documentary if none of them
    /// are documentaries.
    /// </remarks>
    private static RecommendationConfidence ConfidenceFor(
        TasteProfile profile,
        RecommendationCandidate candidate)
    {
        var overall = profile.RatedFilms switch
        {
            >= ConfidentRatings => RecommendationConfidence.High,
            >= ModestlyConfidentRatings => RecommendationConfidence.Medium,
            _ => RecommendationConfidence.Low,
        };

        var knowsTheGenres = candidate.GenreIds.Count == 0
            || candidate.GenreIds.Any(profile.GenreAffinity.ContainsKey);

        return knowsTheGenres
            ? overall
            : (RecommendationConfidence)Math.Min((int)overall, (int)RecommendationConfidence.Low);
    }
}

/// <summary>A film that might be recommended.</summary>
/// <param name="MovieId">NextMovie identifier.</param>
/// <param name="GenreIds">TMDb genre identifiers.</param>
/// <param name="Runtime">Minutes, when known.</param>
/// <param name="ReleaseDate">When it came out, when known. Null means unreleased or unknown, and is not recommendable.</param>
/// <param name="CommunityRating">TMDb community rating 0–10, when the film has votes.</param>
/// <param name="VoteCount">How many people rated it, when known. Decides whether the rating is believed.</param>
public sealed record RecommendationCandidate(
    Guid MovieId,
    IReadOnlyList<int> GenreIds,
    int? Runtime,
    DateOnly? ReleaseDate,
    double? CommunityRating,
    int? VoteCount)
{
    /// <summary>The year it came out, for comparing against what someone watches.</summary>
    public int? ReleaseYear => ReleaseDate?.Year;
}

/// <summary>A scored candidate, with the reasons behind the score.</summary>
/// <param name="MovieId">The film.</param>
/// <param name="Score">0–1. Comparable only against other candidates for the same person.</param>
/// <param name="Reasons">Why it ranked where it did. Possibly empty, never invented.</param>
/// <param name="Confidence">How much evidence stands behind the judgement.</param>
public sealed record ScoredRecommendation(
    Guid MovieId,
    double Score,
    IReadOnlyList<string> Reasons,
    RecommendationConfidence Confidence)
{
    /// <summary>
    /// What each signal was worth before weighting, when the caller asked to know.
    /// </summary>
    /// <remarks>
    /// An init-only property rather than a positional member so that adding it
    /// did not rewrite every call site and every test that builds one of these.
    /// <para>
    /// Diagnostic only. Nothing in the ranking reads it — the score is already
    /// computed by the time this is attached.
    /// </para>
    /// </remarks>
    public ScoreBreakdown? Breakdown { get; init; }
}

/// <summary>
/// What each signal contributed to a score, before its weight was applied.
/// </summary>
/// <remarks>
/// Exists because "this film scored 0.62" cannot be acted on and "its taste term
/// was 0.31 against the winner's 0.44" can. Offline evaluation found a pool
/// containing ten musicals from which none were recommended; without the
/// components, the next step after that finding is a guess.
/// </remarks>
/// <param name="Taste">How much this is the person's kind of film, 0–1.</param>
/// <param name="Quality">How good the wider audience thinks it is, 0–1.</param>
/// <param name="Runtime">How well the length matches what they finish, 0–1.</param>
/// <param name="Era">How well the year matches what they watch, 0–1.</param>
public sealed record ScoreBreakdown(double Taste, double Quality, double Runtime, double Era)
{
    /// <summary>Quality and taste multiplied — the term carrying the most weight.</summary>
    public double Interaction => Taste * Quality;

    /// <summary>What this signal is worth once weighted.</summary>
    public double WeightedTaste => Taste * RecommendationScorer.TasteWeight;

    /// <inheritdoc cref="WeightedTaste"/>
    public double WeightedQuality => Quality * RecommendationScorer.QualityWeight;

    /// <inheritdoc cref="WeightedTaste"/>
    public double WeightedInteraction => Interaction * RecommendationScorer.InteractionWeight;
}

/// <summary>How much the engine trusts its own recommendation.</summary>
public enum RecommendationConfidence
{
    Low = 1,
    Medium = 2,
    High = 3,
}

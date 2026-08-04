# Recommendation Engine

Version: 0.1
Status: Draft
Last Updated: August 2026

---

# Purpose

The Recommendation Engine is the core feature of NextMovie.

Its purpose is to help users discover the best movie they haven't seen that they can watch right now.

Recommendations should feel:

- Personal
- Explainable
- Trustworthy
- Actionable
- Fast

The recommendation engine is the primary competitive advantage of NextMovie.

---

# Goals

Every recommendation should answer three questions.

## 1. Why this movie?

The recommendation should explain itself.

## 2. Why now?

The movie should currently be available to stream and fit the user's preferences.

## 3. How confident are we?

The recommendation should communicate how certain the system is that the user will enjoy it.

---

# Recommendation Pipeline

```
Letterboxd Import
        │
        ▼
User Ratings
        │
        ▼
Taste Profile Generation
        │
        ▼
Candidate Movie Selection
        │
        ▼
Streaming Availability Filter
        │
        ▼
Weighted Scoring
        │
        ▼
Ranking
        │
        ▼
Confidence Calculation
        │
        ▼
Explanation Generation
        │
        ▼
Recommendation API
```

---

# Inputs

The engine uses the following inputs.

## User Data

- Watch history
- Ratings
- Watchlist
- Favorite movies
- Dismissed recommendations
- Recommendation feedback

---

## Movie Metadata

- Genres
- Keywords
- Runtime
- Director
- Cast
- Crew
- Release year
- Language
- Collections
- Popularity
- Average community rating

---

## User Preferences

- Streaming subscriptions
- Preferred runtime
- Favorite decades
- Favorite genres
- Favorite directors
- Favorite actors

---

# User Taste Profile

Rather than analyzing every rating during every request, the engine builds a persistent taste profile.

Example

```
Favorite Genres

Sci-Fi         95
Thriller       82
Drama          71

Favorite Directors

Denis Villeneuve     100
Christopher Nolan     94

Preferred Runtime

100-140 minutes

Favorite Decades

2010s

Favorite Themes

Artificial Intelligence
Space
Psychological
Time Travel
```

The taste profile is recalculated whenever meaningful user data changes.

---

# Candidate Selection

Before ranking, remove movies that should never be recommended.

Exclude:

- Already watched
- Hidden by user
- Already dismissed
- Not available on selected streaming services
- Adult content (unless enabled)

The remaining movies become candidate recommendations.

---

# Weighted Scoring

Each candidate receives a score.

Example weights

| Factor | Weight |
|---------|--------|
| Genre Similarity | 25% |
| Director Similarity | 20% |
| Theme Similarity | 15% |
| Actor Similarity | 10% |
| Runtime Preference | 5% |
| Community Rating | 5% |
| Popularity | 5% |
| User Feedback Model | 15% |

Weights should be configurable.

---

# Confidence Score

Every recommendation should include a confidence level.

Example

```
Match Score

96%

Confidence

High
```

Confidence depends on:

- Amount of user history
- Rating consistency
- Similarity between watched movies
- Number of matching attributes

Example

High

- User has rated 300 movies.

Medium

- User has rated 40 movies.

Low

- User has rated 8 movies.

---

# Recommendation Explanation

Every recommendation should explain itself.

Example

```
Arrival

96% Match

Confidence: High

Why?

✓ You rated Dune ★★★★★

✓ You consistently enjoy thoughtful sci-fi.

✓ Similar director style.

✓ Available on Netflix.
```

Recommendations should never feel random.

---

# Recommendation Feedback

Every recommendation becomes new training data.

Available actions:

- Watched
- Liked
- Loved
- Not Interested
- Hide Similar Movies
- Already Seen

This feedback continuously improves recommendations.

---

# Recommendation Recipes

Different recommendation strategies can reuse the same engine.

## Tonight's Pick

Optimize for:

- Streaming availability
- High confidence
- Preferred runtime

---

## Hidden Gem

Optimize for:

- Lower popularity
- Strong taste match

---

## Comfort Pick

Recommend something highly likely to be enjoyed.

---

## Challenge Me

Recommend something slightly outside the user's normal preferences.

---

## Award Winners

Prioritize critically acclaimed films.

---

## Recently Added

Prioritize movies newly added to the user's streaming services.

---

# Recommendation Versions

## Version 1

Rule-based weighted scoring.

---

## Version 2

Persistent taste profiles.

---

## Version 3

Collaborative filtering.

Example

Users with similar ratings also loved:

Prisoners

---

## Version 4

Embeddings

Use semantic similarity between:

- Plot summaries
- Themes
- Keywords

---

## Version 5

AI Assistant

Natural language requests.

Example

"I want a slow emotional sci-fi movie with incredible visuals."

The AI converts the request into structured search criteria.

The recommendation engine performs the ranking.

AI explains recommendations rather than replacing the recommendation engine.

---

# Success Metrics

Measure recommendation quality using:

- Recommendation click rate
- Recommendation acceptance rate
- Movies watched after recommendation
- Average user rating after recommendation
- Repeat recommendation usage
- User retention

---

# Guiding Principles

Recommendations should be:

- Explainable
- Personalized
- Actionable
- Streaming-aware
- Fast
- Continuously improving
- Transparent

Users should always understand why a recommendation was made.

---

# Future Improvements

- TV show recommendations
- Friend-based recommendations
- Shared movie nights
- Mood-based recommendations
- Seasonal recommendations
- Holiday recommendations
- Family mode
- Group recommendation engine
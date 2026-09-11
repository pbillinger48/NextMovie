# NextMovie Database Design

Version: 0.1
Status: Draft

---

# Philosophy

The database should model the real-world concepts of the application rather than implementation details.

Each table should represent an important business entity.

The design should support future expansion while keeping the MVP simple.

---

# Core Domain

The MVP revolves around seven primary entities:

- User
- Movie
- Watch History
- Rating
- Watchlist
- Streaming Provider
- Recommendation

These entities form the foundation of the platform.

---

# Entity Relationship Diagram (Conceptual)

                User
                  │
      ┌───────────┼───────────┐
      │           │           │
 WatchHistory   Rating   Watchlist
      │           │           │
      └───────────┼───────────┘
                  │
               Movie
                  │
          MovieStreamingProvider
                  │
        StreamingProvider

Movie

↓

Recommendation

↓

User

---

# User

Represents an authenticated account.

Fields

Id

Email

DisplayName

ProfileImageUrl

CreatedAt

UpdatedAt

LastLoginAt

AuthenticationProvider

## Implementation notes

The `users` table as built deviates from the above. See ADR-0003.

- **`AuthenticationProvider` was dropped.** A single provider column makes it
  structurally impossible for one account to hold both a password and a linked
  external sign-in. Instead `PasswordHash` is nullable (null means no password
  is set), and external logins will arrive as an additive `user_external_logins`
  table when OAuth is built — no migration of existing rows, no change to the
  password flow.
- **`NormalizedEmail` was added** and carries the unique index, so `Parker@x.com`
  cannot register alongside `parker@x.com`. `Email` keeps the casing the user
  typed. Normalisation is invariant-culture.
- **`FailedLoginAttempts` and `LockoutEndsAt` were added** for the lockout
  required by ADR-0003. They live on the row rather than in a cache so lockout
  survives a process restart.
- **No soft delete.** Milestone 2 has no account deletion, so none was built.
  ⚠️ Whoever adds it must also convert `ix_users_normalized_email` to a partial
  index (`WHERE deleted_at IS NULL`); a plain unique index makes a deleted user's
  address permanently unusable. That is a migration against live data.

---

# RefreshToken

Not in the original design; required by ADR-0003, which specifies rotation and
family revocation. Neither is possible with a purely stateless token.

Fields

Id

UserId

TokenHash — SHA-256 of the token, hex. The token itself is never stored.

FamilyId — groups a rotation chain; replaying a rotated token revokes the family

ExpiresAt

CreatedAt

RevokedAt

ReplacedByTokenId

Rows are **never deleted on rotation**. A rotated token must remain so that
presenting it again is recognisable as replay rather than as an unknown token.

---

# Movie

Represents a single movie.

Most movie metadata comes from TMDb.

Fields

Id

TmdbId

Title

OriginalTitle

Overview

PosterPath

BackdropPath

ReleaseDate

Runtime

AverageRating

Popularity

Language

Status

CreatedAt

UpdatedAt

---

# Genre

Genres are reusable.

Examples:

Action

Drama

Comedy

Sci-Fi

Horror

Animation

Fields

Id

Name

---

# MovieGenre

Many-to-many relationship.

MovieId

GenreId

---

# Person

Represents actors, directors, writers, etc.

Fields

Id

TmdbId

Name

ProfileImage

Biography

---

# MoviePerson

Relationship table.

MovieId

PersonId

Role

CharacterName

Order

---

# WatchHistory

Represents every movie watched by a user.

Fields

Id

UserId

MovieId

WatchedDate

Source

CreatedAt

Source examples:

Letterboxd

Manual

Import

---

# Rating

Stores user ratings.

Fields

Id

UserId

MovieId

Rating

Review

CreatedAt

UpdatedAt

## Implementation notes

**Built.** See [ADR-0006](adr/0006-ratings-and-watch-history.md) for the reasoning;
the table above is the original sketch and differs from what shipped:

- **`Source` was added** (`Native`, `LetterboxdImport`). The sketch gave one to
  `WatchHistory` and not to `Rating`, which would have let a re-import silently
  overwrite a hand-entered rating.
- **`(UserId, MovieId)` is unique.** One opinion per person per film; re-rating
  updates the row.
- **The scale is 0.5–5.0 in half-steps**, stored as `numeric(2,1)` with a check
  constraint pinning both the range and the step. Identical to Letterboxd's, so
  import is a copy rather than a lossy conversion.
- **`Review` was not built.** Nothing consumes it yet, and a text column nobody
  reads is a migration waiting to be undone.
- **`WatchedDate` became `WatchedOn`, nullable, and a date rather than a
  timestamp.** "I have seen this, I do not remember when" is a real state that
  `ratings.csv` produces on every row.
- Rating a film **creates a viewing** if there is none, so "have they seen it"
  and "what do they think of it" cannot disagree.

---

# Watchlist

Represents movies saved for later.

Fields

Id

UserId

MovieId

AddedAt

Priority

Notes

---

# StreamingProvider

Examples

Netflix

Max

Disney+

Prime Video

Apple TV+

Fields

Id

Name

LogoPath

---

# MovieStreamingProvider

Which services currently offer a movie.

MovieId

StreamingProviderId

AvailableFrom

AvailableUntil

---

# UserStreamingProvider

Streaming subscriptions selected by the user.

UserId

StreamingProviderId

---

# Recommendation

Stores generated recommendations.

Fields

Id

UserId

MovieId

RecommendationScore

Reason

GeneratedAt

Version

Dismissed

Accepted

Watched

---

# Recommendation Feedback

Future table.

Stores user interactions.

Examples:

Ignored

Liked

Watched

Not Interested

This will improve recommendation quality.

---

# Future Entities

Collection

Custom Lists

Friends

Reviews

Notifications

Movie Night

TV Shows

Achievements

---

# Design Principles

- UUID primary keys
- Foreign keys for relationships
- Soft deletes where appropriate
- Audit timestamps
- Normalize reference data
- Index frequently queried fields
- Never duplicate user-specific data unnecessarily
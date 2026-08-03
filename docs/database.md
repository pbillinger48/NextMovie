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
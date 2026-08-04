# NextMovie API

Version: 0.1
Status: Draft
Last Updated: August 2026

---

# Overview

The NextMovie API provides a REST interface for both the mobile and web applications.

The API is the single source of truth for all business logic.

All clients consume the same endpoints.

---

# Design Principles

The API should be:

- RESTful
- Versioned
- Secure
- Stateless
- Mobile-first
- Fast
- Consistent

All endpoints return JSON.

---

# Authentication

Authentication uses JWT access tokens.

Supported login methods:

- Email/password
- Google OAuth
- Apple Sign In (future)

Every authenticated request includes:

Authorization: Bearer {token}

---

# API Versioning

/api/v1/

Future breaking changes will use:

/api/v2/

---

# Authentication

## Register

POST

/api/v1/auth/register

Creates a new account.

---

## Login

POST

/api/v1/auth/login

Returns:

- Access Token
- Refresh Token
- User

---

## Refresh Token

POST

/api/v1/auth/refresh

Returns a new access token.

---

## Logout

POST

/api/v1/auth/logout

---

# User

## Current User

GET

/api/v1/users/me

Returns:

- Profile
- Taste Profile
- Streaming Providers

---

## Update Profile

PUT

/api/v1/users/me

---

# Movies

## Search Movies

GET

/api/v1/movies/search

Query Parameters

- title
- actor
- director
- genre
- page

---

## Movie Details

GET

/api/v1/movies/{movieId}

Returns

Movie metadata

Streaming availability

Recommendation explanation (if available)

---

## Trending Movies

GET

/api/v1/movies/trending

---

## New Releases

GET

/api/v1/movies/new

---

# Recommendations

## Tonight's Recommendation

GET

/api/v1/recommendations/tonight

Returns:

- Movie
- Match Score
- Confidence
- Explanation

---

## Recommendation Feed

GET

/api/v1/recommendations

Supports:

- Page
- Recipe
- Genre

---

## Recommendation Feedback

POST

/api/v1/recommendations/{id}/feedback

Actions

- Watched
- Liked
- Loved
- Not Interested
- Already Seen

---

# Ratings

## Rate Movie

POST

/api/v1/ratings

---

## Update Rating

PUT

/api/v1/ratings/{id}

---

## Delete Rating

DELETE

/api/v1/ratings/{id}

---

# Watch History

## Add Watched Movie

POST

/api/v1/watch-history

---

## Get Watch History

GET

/api/v1/watch-history

---

# Watchlist

## Get Watchlist

GET

/api/v1/watchlist

---

## Add Movie

POST

/api/v1/watchlist

---

## Remove Movie

DELETE

/api/v1/watchlist/{movieId}

---

# Streaming Providers

## User Providers

GET

/api/v1/streaming/providers

---

## Update Providers

PUT

/api/v1/streaming/providers

---

# Letterboxd

## Import

POST

/api/v1/import/letterboxd

Starts an asynchronous import.

Returns

Import Job Id

---

## Import Status

GET

/api/v1/import/{jobId}

Returns:

- Pending
- Running
- Completed
- Failed

---

# Taste Profile

## Get Taste Profile

GET

/api/v1/taste-profile

Returns:

- Favorite genres
- Favorite directors
- Favorite themes
- Favorite runtime
- Favorite decades

---

# Statistics

## Dashboard

GET

/api/v1/statistics

Returns:

- Movies watched
- Average rating
- Favorite genres
- Rating distribution
- Watch streak

---

# Health

GET

/api/v1/health

Used for monitoring.

---

# Error Format

All errors return:

{
    "status": 404,
    "title": "Movie Not Found",
    "detail": "The requested movie could not be found."
}

---

# Future APIs

- Friends
- Movie Night
- AI Assistant
- Notifications
- TV Shows
- Collections
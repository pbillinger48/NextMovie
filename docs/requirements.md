# Product Requirements Document (PRD)

# NextMovie

Version: 0.1
Status: Draft
Author: Parker Billinger
Last Updated: August 2026

---

# 1. Vision

NextMovie helps users discover the perfect movie to watch in under 30 seconds.

Instead of endlessly scrolling through multiple streaming services, users receive personalized recommendations based on their movie history, preferences, and available streaming subscriptions.

Our goal is to become the smartest personal movie recommendation platform available on both mobile and web.

---

# 2. Problem Statement

Choosing a movie has become harder than ever.

Users often have:
- Multiple streaming subscriptions
- Hundreds of watched movies
- Multiple watchlists
- Limited time

Current platforms solve only part of the problem.

Letterboxd tracks watched movies.

JustWatch shows streaming availability.

IMDb provides information.

TMDb provides metadata.

None combine these into one intelligent recommendation experience.

---

# 3. Mission

Help movie lovers decide what to watch quickly and confidently.

Every recommendation should answer:

"What should I watch tonight?"

---

# 4. Success Metrics

MVP Success

- User can create an account.
- User can import Letterboxd history.
- User can choose streaming services.
- User receives personalized recommendations.
- User can mark movies watched.

Future Success

- Daily active users
- Recommendation acceptance rate
- Average recommendation quality
- User retention
- Premium subscriptions

---

# 5. Target Audience

Primary Users

Movie enthusiasts

Characteristics

- Uses Letterboxd
- Watches 50+ movies annually
- Owns multiple streaming subscriptions
- Enjoys discovering new movies
- Wants recommendations beyond the obvious

Secondary Users

Casual movie watchers who struggle deciding what to watch.

---

# 6. Core Principles

1. Mobile First

The mobile app is the primary experience.

The web application complements it.

2. Recommendations First

Everything should improve recommendations.

3. Fast

Users should receive recommendations instantly.

4. Explainable

Every recommendation should include why it was recommended.

5. Beautiful

Modern, polished UI with delightful animations.

6. Privacy

Users own their data.

---

# 7. MVP Features

Authentication

- Email/password
- Google login

User Profile

- Favorite genres
- Favorite directors
- Streaming subscriptions

Movie Data

- Search
- Movie details
- Posters
- Ratings

Letterboxd

- Import watched movies
- Import ratings
- Import watchlist (if technically feasible)

Recommendations

- Personalized recommendations
- Explainable recommendations
- Streaming filters

Watchlist

- Save movies
- Mark watched
- Remove

---

# 8. Future Features

AI movie concierge

"What should I watch if I loved Arrival but want something shorter?"

Friend recommendations

Movie nights

Taste compatibility

Advanced statistics

Year in review

Shared watchlists

Notifications

Upcoming releases

Theater recommendations

TV Shows

Collections

Premium subscriptions

---

# 9. Technical Goals

Cross-platform

React Native

React Web

ASP.NET Core API

PostgreSQL

Docker

Azure

GitHub Actions

Infrastructure as Code

Redis

Application Insights

---

# 10. Recommendation Philosophy

Recommendations should be:

Relevant

Explainable

Fast

Personal

Streaming-aware

The recommendation engine should evolve over time:

Version 1

Rule-based scoring

Version 2

Taste profiles

Version 3

Collaborative filtering

Version 4

AI-enhanced recommendations

---

# 11. Non-Goals (MVP)

No chat system

No social feed

No user reviews

No TV show support

No fantasy movie leagues

No public APIs

These may be added in future versions.

---

# 12. Definition of Success

A user can:

Open the app

Receive a recommendation

Understand why it was recommended

Know exactly where to watch it

Begin watching within 30 seconds

If we accomplish that, we've succeeded.
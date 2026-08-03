# NextMovie Architecture

Version: 0.1
Status: Draft

---

# Overview

NextMovie is a mobile-first movie recommendation platform with a shared backend supporting both mobile and web applications.

The system is designed as a modular monolith using Vertical Slice Architecture.

This approach prioritizes maintainability, rapid feature development, and the ability to scale individual components if needed.

---

# High-Level Architecture

```text
                Internet
                    │
        ┌───────────┴───────────┐
        │                       │
   React Native App        React Web App
        │                       │
        └───────────┬───────────┘
                    │
              ASP.NET Core API
                    │
      ┌─────────────┼─────────────┐
      │             │             │
 PostgreSQL     Redis Cache   Background Jobs
      │
      └─────────────┬─────────────┐
                    │
            External Providers
                    │
      ├── TMDb
      ├── Letterboxd Import
      └── Streaming Availability
```

---

# Applications

## Mobile

React Native + Expo

Primary user experience.

Responsibilities:

- Recommendations
- Search
- Watchlist
- Profile
- Ratings

---

## Web

React + TypeScript + Vite

Responsibilities:

- Statistics
- Imports
- Settings
- Administration
- Rich browsing

---

## API

ASP.NET Core (.NET)

Responsibilities:

- Authentication
- Business logic
- Recommendation engine
- User management
- Movie management
- Integrations

---

# Backend Architecture

The backend follows Vertical Slice Architecture.

Major feature modules:

- Authentication
- Movies
- Recommendations
- Watch History
- Streaming Providers
- Watchlists
- User Profile

Each feature contains:

- Endpoint
- Request
- Response
- Handler
- Validation
- Data access

Features should have minimal dependencies on one another.

---

# Database

Primary database:

PostgreSQL

Stores:

- Users
- Movies
- Ratings
- Watch History
- Streaming Providers
- Recommendations
- Watchlists

---

# Recommendation Engine

The recommendation engine is the core business domain.

Version 1

Weighted scoring based on:

- Genres
- Directors
- Actors
- Keywords
- Runtime
- User ratings

Future versions:

- Taste profiles
- Collaborative filtering
- AI-assisted recommendations

---

# External Services

TMDb

Movie metadata.

Streaming Provider

Streaming availability.

Letterboxd

User history import.

---

# Authentication

Email/password

Google OAuth

JWT authentication

---

# Infrastructure

Docker

GitHub Actions

Azure

Application Insights

Terraform

---

# Guiding Principles

1. Mobile-first.

2. API-first.

3. Feature-based organization.

4. Keep components loosely coupled.

5. Optimize for readability over cleverness.

6. Build for maintainability before scalability.
# NextMovie User Flows

Version: 0.1  
Status: Draft  
Last Updated: August 2026

---

# Overview

NextMovie is designed around one core experience:

> Help users stop browsing and start watching.

Every user flow should reduce decision fatigue and help users find a movie they are excited to watch as quickly as possible.

The primary user journey:
Discover → Understand → Decide → Watch → Reflect → Improve Recommendations

---

# Flow 1: New User Onboarding

## Goal

Create a personalized experience for a new user within their first few minutes.

A new user should quickly understand the value of NextMovie and receive their first recommendations.

---

## User Journey
Open App
|
Create Account
|
Welcome Experience
|
Import Movie History
|
Select Streaming Services
|
Build Initial Taste Profile
|
Generate Recommendations
|
Home Screen

---

# Screen 1: Welcome

## Purpose

Communicate the value proposition immediately.

## Content


Welcome to NextMovie

Stop scrolling.
Start watching.

Personalized movie recommendations
based on what you love and where you stream.

[Get Started]

---

# Screen 2: Account Creation

## Options

- Email/password
- Google authentication
- Apple authentication (future)

## Requirements

User account must be created before storing personalized data.

---

# Screen 3: Movie History Import

## Purpose

Build an initial understanding of user preferences.

## Options

### Letterboxd Import

Primary option.

Import:
- Watched movies
- Ratings
- Reviews (future)
- Watchlist

---

### CSV Import

Allow users to upload exported movie data.

---

### Start Fresh

Allow users without existing tracking history to continue.

---

# Screen 4: Streaming Services

## Purpose

Ensure recommendations are immediately actionable.

Users select services they currently have access to.

## Examples

- Netflix
- Max
- Disney+
- Hulu
- Prime Video
- Apple TV+
- Paramount+
- Peacock

---

# Screen 5: Taste Calibration

## Purpose

Generate recommendations even when a user has limited history.

## Experience

User selects movies they love.

Example:
Choose movies you enjoyed:

Interstellar
Dune
Parasite
Whiplash
The Godfather

The system uses these selections to create an initial taste profile.

---

# Screen 6: First Recommendations

## Goal

Deliver immediate value.

Example:


Your first recommendations are ready.

Based on your love of:

Sci-Fi
Christopher Nolan
Denis Villeneuve

Tonight's recommendations:

Arrival
98% Match

Prisoners
94% Match

Ex Machina
91% Match

---

# Flow 2: Daily Recommendation Experience

## Goal

The primary recurring experience.

A user opens NextMovie and immediately knows what to watch.

---

## User Journey


Open App
|
Home Screen
|
View Recommendation
|
Review Explanation
|
Choose Action


---

# Home Screen

## Purpose

Answer:

"What should I watch tonight?"

Example:


Good evening Parker

Tonight's Recommendation

Arrival

98% Match

Available on Netflix

Why you'll like it:

✓ You loved Dune
✓ Similar sci-fi themes
✓ Same director style
✓ Fits your preferred runtime

[Watch Trailer]

[Add to Watchlist]

[Already Watched]


---

# Recommendation Actions

Users can:

## Watch

Open streaming provider.

## Save

Add to watchlist.

## Dismiss

Tell the system:

"Not interested."

## Watched

Record viewing history and improve recommendations.

---

# Flow 3: Movie Discovery

## Goal

Allow users to explore movies beyond generated recommendations.

---

## User Journey


Search
|
Movie Results
|
Movie Details
|
Action


---

# Search

Users can search by:

- Movie title
- Actor
- Director
- Genre
- Keyword

---

# Movie Details Page

Example:


Arrival

2016

★★★★★

Director:
Denis Villeneuve

Runtime:
116 minutes

Genres:
Sci-Fi
Drama

Available On:

Netflix

Why you may like it:

✓ Similar to Interstellar
✓ You rate sci-fi highly
✓ Matches your preferred runtime

[Add Watchlist]


---

# Flow 4: Watchlist Management

## Goal

Allow users to maintain a personal queue of movies.

---

## User Journey


Save Movie
|
Watchlist
|
Select Movie
|
Mark Watched


---

# Watchlist Features

Users can:

- Add movies
- Remove movies
- Sort by:
  - Recommendation score
  - Release date
  - Runtime
  - Streaming availability
- Mark movies watched

---

# Flow 5: Rating and Feedback

## Goal

Continuously improve recommendations.

---

## User Journey


Watch Movie
|
Rate Movie
|
Update Taste Profile
|
Improve Future Recommendations


---

# Rating Options

Initial:


★★★★★


Future:

- Like/dislike
- Favorite
- Review
- Tags

---

# Flow 6: User Profile and Taste

## Goal

Help users understand their own movie preferences.

---

# Profile Information

Example:


Parker's Movie Taste

Movies Watched:
347

Average Rating:
4.2 stars

Favorite Genres:

Sci-Fi
Thriller
Drama

Favorite Directors:

Christopher Nolan
Denis Villeneuve

Favorite Decades:
2010s
2020s


---

# Future Taste Insights

Potential features:

- "Your movie personality"
- Genre evolution
- Most watched actors
- Favorite cinematographers
- Yearly summaries

---

# Flow 7: Web Application Experience

## Goal

Provide deeper management and exploration.

The web application complements the mobile app.

---

# Web Features

## Dashboard


Welcome back Parker

Your Movie Journey

[Recommendations]

[Statistics]

[Collections]

[Settings]


---

## Data Management

Users can:

- Manage imports
- Update streaming services
- Adjust preferences
- Manage account settings

---

## Advanced Statistics

Examples:

- Movies watched by year
- Genre trends
- Director rankings
- Rating distribution
- Watch history timeline

---

# Flow 8: Future Social Features

## Goal

Allow users to share movie experiences.

---

Potential features:

## Friends

- Follow users
- Compare tastes
- See activity

## Shared Watchlists

- Create lists together
- Vote on movies

## Taste Compatibility

Example:


You and Sarah have 87% movie compatibility.


---

# Flow 9: Future AI Experience

## Goal

Allow natural language movie discovery.

---

Example:

User:

> "I want something like Dune but shorter and more emotional."

System:


Recommended:

Arrival

Why:

Similar sci-fi themes
Emotional focus
116 minute runtime
You rated similar movies highly

---

# Core Product Loop

The long-term product loop:


Watch Movies
|
Rate Movies
|
Build Taste Profile
|
Generate Better Recommendations
|
Discover New Movies
|
Repeat


The better NextMovie understands the user, the more valuable it becomes.
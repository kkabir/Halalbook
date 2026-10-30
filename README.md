# HalalBook

A beautiful, intelligent social network for the Muslim community: Facebook-style social features, an Islamic
architecture design system (emerald and gold, eight-point star geometry, mihrab arches), AI help powered by Claude,
and everyday deen tools.

| | Production | Local dev |
|---|---|---|
| Web (Blazor) | https://halalbook.app | https://localhost:7203 |
| REST API (Swagger UI) | https://api.halalbook.app | https://localhost:7121 |

## Solution layout

```
HalalBook.slnx
src/HalalBook.Shared   DTOs and enums shared by API and web
src/HalalBook.Api      ASP.NET Core minimal API, EF Core, JWT auth, SignalR hub, AI + Islamic services
src/HalalBook.Web      Blazor Web App (interactive server) that calls the API
tests/HalalBook.Api.Tests  xUnit unit + integration tests
```

## Features

**Social**: profiles with arched cover headers, friends (requests, suggestions by mutual friends), follow, block,
smart ranked news feed, posts with photos and videos, feelings and locations, visibility (public, friends, only me),
shares, six reactions (Like, Love, MashaAllah, SubhanAllah, Ameen, Sad), threaded comments with likes, @mentions,
#hashtags and trending, bookmarks, 24-hour stories, short-video reels, groups (public and private with approvals),
pages, events with RSVPs, real-time 1:1 and group messaging with typing indicators, live notifications, global search.

**AI (Claude)**: Noor assistant for questions about Islam and Muslim life; composer help (polish, shorter, warmer,
add a dua, caption); hashtag suggestions; AI halal moderation layered on a fast word list; translation; summaries of long
comment threads. Every AI feature falls back gracefully when no key is configured.

**Deen tools**: prayer times (9 calculation methods, Hanafi or standard Asr, high-latitude fallback), monthly timetable,
Qibla compass with live device heading, Hijri (Umm al-Qura) calendar with Islamic occasions and white days.

**Live**: live shows, live events (halaqas, talks, classes), audio-only live podcasts and video calls where everyone is on
camera. Hosts can go live now or schedule, share their screen, mute and turn off the camera. Viewers get live chat,
reactions and a viewer count, and friends and followers are notified when someone goes live. Media is peer-to-peer
WebRTC with SignalR signaling (`/hubs/live`), which suits video calls of up to about 6 people and broadcasts of up to about
25 viewers. For bigger audiences, plug in a media server (SFU) such as LiveKit. Add a TURN server under `Live:IceServers`
so people behind strict firewalls can connect.

**Safety**: blocked content is rejected, borderline content waits in a moderator queue, user reports, suspensions;
the first account registered becomes Admin.

## Run locally

Requires the .NET 10 SDK.

```bash
dotnet run --project src/HalalBook.Api --launch-profile https
```

```bash
dotnet run --project src/HalalBook.Web --launch-profile https
```

Open https://localhost:7203. In Development the API uses SQLite (`halalbook.db`) and seeds demo users
(`aisha` is an admin; also `omar`, `yusuf`, `maryam`) with the password in `Seed:Password` in
`src/HalalBook.Api/appsettings.Development.json`.

```bash
dotnet test
```

## Configuration

API (`src/HalalBook.Api`):

| Key | Purpose |
|---|---|
| `Database:Provider` | `Sqlite` (default) or `SqlServer` |
| `ConnectionStrings:Default` | database connection string |
| `Jwt:Key` | random secret, 32+ characters (required) |
| `Ai:ApiKey` / `Ai:Model` | Claude API key and model; leave the key empty to disable AI |
| `PublicBaseUrl` | public URL used for uploaded media links |
| `Cors:Origins` | allowed browser origins |
| `Moderation:BlockedTerms` / `ReviewTerms` | word lists for the local moderator |

Web (`src/HalalBook.Web`): `Api:BaseUrl` (defaults to `https://api.halalbook.app/`).

Secrets live in `src/HalalBook.Api/appsettings.Production.json`, which is git-ignored. Copy
`appsettings.Production.example.json` to create it on a new machine.

## Deploy to SmarterASP.net

1. Fill in `src/HalalBook.Api/appsettings.Production.json`: the SQL Server password and, optionally, `Ai:ApiKey`.
2. Publish both apps (self-contained win-x64, so the server does not need the .NET 10 runtime):

   ```bash
   dotnet publish src/HalalBook.Api -p:PublishProfile=SmarterASP-Folder
   ```

   ```bash
   dotnet publish src/HalalBook.Web -p:PublishProfile=SmarterASP-Folder
   ```

3. Upload `publish/api` to the api.halalbook.app site root and `publish/web` to the halalbook.app site root.
4. In the SmarterASP control panel: enable **WebSockets** for both sites (Blazor Server and live chat need them), give
   the API site **write permission** on `wwwroot/uploads`, and enable SSL for both domains.
5. Create or upgrade the database schema with EF Core migrations (`src/HalalBook.Api/Data/Migrations`), using one of these:
   - Run `scripts/update-database.ps1`, which reads the connection string from `appsettings.Production.json`.
   - Run `docs/db/halalbook-schema.sql`, an idempotent script, in the SmarterASP SQL manager.
   - Do nothing: the API applies pending migrations when it starts in Production.
6. Register your own account first: it becomes the Admin.

After changing entities, add a migration with `dotnet tool run dotnet-ef migrations add <Name> --project src/HalalBook.Api --output-dir Data/Migrations`.

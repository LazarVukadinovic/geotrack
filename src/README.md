# GeoTrack

GeoTrack je mobile-first PWA za otkrivanje destinacija, mesta, smeštaja i događaja, sa pratećim .NET 8 backend-om koji pokriva autentikaciju, geo-prostorni sadržaj, recenzije, korisničke doprinose, mapne pretrage i analitiku.

## Pregled projekta

Glavna aplikacija — `user-app` — je Angular 21 PWA optimizovana za telefon. Pruža:

- otkrivanje destinacija po lokaciji (Explore hub),
- listinge i detalje za smeštaje, događaje, kulturna mesta, prirodu, restorane i lokalne lokacije,
- omiljene stavke, kolekcije i recenzije,
- korisničke doprinose za nove smeštaje (sa odobravanjem od strane regionalnih moderatora),
- mapu sa pretragom, reverse geocoding-om, pešačkim rutama i markerima koji se traže po viewport-u,
- onboarding eksplicitnih interesa i pasivno učenje implicitnih interesa kroz analitiku,
- Ask Agent ekran (chat surface; backend trenutno daje stub odgovore).

Backend — `server/Geotrack` — je .NET 8 clean-architecture rešenje sa PostgreSQL + PostGIS bazom, JWT bearer autentikacijom i HttpOnly refresh kolačićem, full-time analytics worker-om i moderacijom doprinosa.

## Trenutno stanje na ovoj grani

Branch `feature/frontend-integracije` predstavlja prelazak `user-app`-a sa mock podataka na pravi backend. Veliki deo ekrana koji su ranije čitali iz `data/*.ts` fajlova sada prolazi kroz HTTP servise i pravi `/api/*` endpoint-e iz `server/Geotrack`-a.

Konkretno, na ovoj grani su povezani:

- ceo auth flow (registracija, prijava, refresh, odjava, onboarding interesa),
- profil, podešavanja, promena lozinke, support zahtevi,
- Explore hub i sve listing stranice,
- detail stranice (mesto, smeštaj, događaj),
- omiljene stavke i pune kolekcije,
- recenzije iz detail stranica,
- pun CRUD korisničkih doprinosa (sa upload-om slike),
- mapa sa svih pet `/api/map/*` poziva,
- analytics pipeline od klijenta do worker-a koji puni implicitne interese.

Delimično ili planirano: Ask Agent (samo wire), admin analitika (servis postoji, kontroler ne), recommendations ruta i `admin-app` UI. Detalji u sekciji "Šta je delimično ili planirano".

## Funkcionalnosti povezane sa backendom

Tabela ispod pokriva ono što je stvarno priključeno na ovoj grani.

| Funkcionalnost | Frontend | Backend endpoint(i) | Status |
|---|---|---|---|
| Registracija / prijava / refresh / odjava | `auth.service.ts`, `login-page`, `register-page` | `POST /api/auth/{register,login,refresh,logout}` + HttpOnly cookie `geotrack_refresh` | Pun |
| Cold-start hidracija sesije | `app.config.ts` (app initializer) | `POST /api/auth/refresh` + `GET /api/user/me` pre prve rute | Pun |
| Onboarding interesa | `register/interests`, `interestsOnboardingGuard`, `tag.service.ts` | `GET /api/tags?forUser=true`, `GET/PUT /api/user/me/interests`, `DELETE /api/user/me/interests/inferred/{tagId}` | Pun |
| Profil i izmena | `user.service.ts`, `edit-profile` | `GET /api/user/me`, `PUT /api/user/me`, `PUT /api/user/me/avatar` (multipart) | Pun |
| Podešavanja | `user-settings.service.ts` | `GET/PUT /api/user/me/notifications`, `PUT /api/user/me/password`, `GET/POST /api/user/me/support-requests` | Pun |
| Lokacija | `location.service.ts` | GPS → reverse geocode preko `/api/map/reverse` → IP fallback `GET /api/locations/approximate` | Pun |
| Destinacija (sesija) | `destination.service.ts` | Vrednost dolazi iz `GET /api/explore`, čuva se u sessionStorage | Pun |
| Explore hub | `explore.service.ts` | `GET /api/explore?latitude=&longitude=` (jedna ruta vraća sve sekcije) | Pun |
| Hotels listing | `hotels-listing.service.ts` | `GET /api/places/hotels?destinationId=…` | Pun |
| Events listing | `events-listing.service.ts` | `GET /api/events?destinationId=…` | Pun |
| Culture & History / Food & Music / Nature | feature servisi | `GET /api/places?tags=…&destinationId=…` | Pun |
| Local Places (grid + kategorija) | `local-places.service.ts`, `local-places-category.service.ts` | `GET /api/explore/categories`, `GET /api/places?category=…&destinationId=…` | Pun |
| Detail stranice | `place-detail.service.ts`, `hotel-detail.service.ts`, `event-detail.service.ts` | `GET /api/places/:id`, `GET /api/places/hotels/:id`, `GET /api/events/:id` (iskreni loading/error/not-found, bez mock fallback-a) | Pun |
| Recenzije | `place-detail-page`, `hotel-detail-page` | `POST /api/places/:id/reviews` (validacija na backend-u: 1–5 ★, ≤ 1000 znakova, jedan korisnik jedna recenzija po entitetu) | Pun |
| Omiljeno | `favorite-state.service.ts` | `POST /api/favorites/toggle` (polimorfno: `place`/`event`), `GET /api/favorites` | Pun |
| Kolekcije | `features/profile/services/collections.ts` | Pun CRUD na `/api/user/me/collections` i `/items` | Pun |
| Doprinosi (smeštaji) | `contributions.service.ts`, `submit-contribution`, `my-contributions`, `contribution-detail` | `GET/POST/PUT/DELETE /api/contributions`, `POST /api/contributions/stays` (multipart FormData sa cover slikom), `PUT /api/contributions/stays/:id` | Pun |
| Mapa | `map-api.service.ts`, `MapPageComponent` (maplibre-gl) | `GET /api/map/{search,reverse,route,markers,entities}` (svi pozivi ka Nominatim-u i OSRM-u idu kroz backend; markeri se traže po viewport bounds + zoom + kategorijama) | Pun |
| Analitika | `analytics.service.ts` (eager init) | `POST /api/analytics/events` (batch envelope ≤ 50 događaja, dedup u skliznom prozoru); backend pipeline: `IAnalyticsProcessingQueue` → `AnalyticsEventProcessingWorker` → `UserInterestSignalUpdater` koji puni `UserInterestSignal` (implicitni interesi) | Pun |
| Ask Agent | `agent.service.ts`, `agent-page` | `POST /api/agent/message` — wire postoji, ali backend trenutno odgovara keyword-stub-om | Delimičan |

## Pokretanje projekta

### Preduslovi

- Node.js sa `npm`
- .NET 8 SDK
- PostgreSQL 12+ sa **PostGIS** ekstenzijom (pri startu, server pokušava `CREATE EXTENSION IF NOT EXISTS postgis` i pada ako PostGIS nije dostupan)

### Backend

```bash
cd server/Geotrack/Geotrack.Server
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=geotrack;Username=postgres;Password=<lozinka>"
dotnet user-secrets set "Jwt:Key" "<random-string-od-bar-32-karaktera>"

cd ..
dotnet run --project Geotrack.Server
```

Podrazumevane vrednosti za `Jwt:Issuer` i `Jwt:Audience` (koje nisu tajne) su već u `appsettings.json`. U Development modu server pri startu automatski pokreće EF migracije i `SeedData.InitializeAsync()`. Backend slušao na `http://localhost:5003`.

Demo nalog posle seed-a:

- email: `marko.p@example.com`
- lozinka: `password123`

### Frontend

```bash
cd user-app
npm install
npm run start
```

`proxy.conf.json` prosleđuje `/api/*` i `/media/*` zahteve na `http://localhost:5003`, tako da Angular kod radi sa relativnim putanjama bez ikakve promene u izvornom kodu.

### Provera (build i testovi)

Frontend:

```bash
cd user-app
npm run build
npm test -- --watch=false
```

Backend:

```bash
cd server/Geotrack
dotnet build Geotrack.sln
dotnet test Geotrack.IntegrationTests/Geotrack.IntegrationTests.csproj
```

Backend integracioni testovi prave privremenu PostgreSQL bazu na osnovu konfigurisane lokalne konekcije, primene migracije, izvrše seed, izvrše scenarije (auth + favorites + collections, analitika, podešavanja, doprinosi, ratings) i obrišu bazu po završetku.

## Tehnologije

- **Frontend:** Angular 21.2 (standalone, signals-first, novi control flow `@if/@for/@switch`), Tailwind v4 (samo auth stranice), SCSS + BEM + design tokens (sve ostale stranice), `maplibre-gl` 5.x, Vitest + jsdom.
- **Backend:** .NET 8 / C#, EF Core 8 + Npgsql, NetTopologySuite (PostGIS), ASP.NET Core Web API, JWT Bearer + rotirajući HttpOnly refresh cookie, rate-limit politike (`auth` 10/min, `analytics` 60/min, `agent` 20/min), security header-i (CSP, HSTS, X-Frame-Options DENY, COOP same-origin), xUnit + `WebApplicationFactory`.
- **Baza:** PostgreSQL 12+ sa PostGIS — `geography(point)` kolone i GIST indeksi za prostorne upite na destinacijama, lokacijama, mestima i događajima.

## Bitne arhitektonske napomene

- **Refresh token** je HttpOnly cookie `geotrack_refresh` (path `/api/auth`, SameSite=Lax, Secure na ne-localhost). Nikada nije u JSON body-ju i nije čitljiv iz JS-a. Tokeni su hash-ovani u bazi i rotiraju se na svaki refresh.
- **Auth interceptor** zakači `Authorization: Bearer` samo na same-origin `/api/*` URL-ove. Ovo je namerno — raniji bug je curio bearer ka eksternim servisima.
- **Analitika je backend-first.** Server uvek puni `UserId` iz JWT-a; klijent ga ne podiže. Whitelist događaja (`AnalyticsEventTypes`) i entiteta (`AnalyticsEntityTypes`) je ugovor — mora se sinhronizovati i u `analytics.types.ts` na frontu, jer eventi van whitelist-a server tiho odbacuje.
- **Mapa** je viewport-driven. Markeri se traže za trenutne `bbox + zoom + categories`, nikada "učitaj sve odjednom". Sve eksterne pozive (Nominatim, OSRM) izvršava backend; browser nema direktnu vezu ka tim servisima (zbog rate limits-a i security header-a).
- **Doprinosi (smeštaji)** se šalju kao `multipart/form-data` (JSON `Payload` + `CoverImage` do 8 MB). Migracija `20260427003946` osigurava da kontakt email, cena po noći i URL-ovi booking partnera budu očuvani prilikom odobravanja doprinosa (kreiranja `Place` zapisa).
- **Detail stranice** koriste sealed-viewport layout (`height: 100dvh; overflow: hidden`) i ne skroluju kroz `app-shell` — imaju sopstveni scroll container sa progresivnim fade-in top-bar-om.
- **Auth stranice** (`login-page`, `register-page`, `interests-page`) namerno koriste starije Angular obrasce (constructor injection, `[(ngModel)]`, `.css` umesto `.scss`). Nije tehnički dug — preneto iz tuđeg rada i ne treba ga "popravljati" osim ako to nije eksplicitan zahtev.

## Šta je delimično ili planirano

- **Ask Agent** — `POST /api/agent/message` postoji i frontend ga zaista poziva, ali backend trenutno generiše odgovore kroz keyword-matching stub. Provider/LLM apstrakcija, persistencija razgovora i tool registry nisu implementirani.
- **Admin analitika** — `IAnalyticsReadService` postoji u backend-u (top entiteti, top destinacije, outbound clicks, search trends), ali nijedan kontroler ga još ne izlaže i ne postoji admin UI koji bi ga koristio.
- **Recommendations ruta** — folder postoji, ali nije priključena na backend i nije dostupna u glavnom UI-ju.
- **`admin-app`** — Angular skela bez ruta. Backend već ima primitive (`[Authorize(Roles="Admin")]` endpoint-i, `StayContribution` queue za odobravanje), ali UI još ne postoji.
- **Settings statičke stranice** (`privacy-settings`, `help-center`, `terms-and-privacy`) — informativne, bez backend-a.
- **Reset lozinke / brisanje naloga / privacy export** — još nisu implementirani.
- **Object storage** — slike (avatari, doprinosi) se trenutno čuvaju lokalno u `/media/uploads/...` i serviraju kao statički fajlovi sa per-folder cache pravilima.

## Sledeći logični koraci

1. Backend kontroler iznad `IAnalyticsReadService` + minimalan admin dashboard.
2. Ask Agent v2 — provider apstrakcija, persistencija razgovora, tool/MCP registry.
3. Build-out `admin-app`-a (moderacija doprinosa, role/region admin, analitika).
4. Reset lozinke i flow brisanja naloga.

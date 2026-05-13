# GeoTrack

GeoTrack je web aplikacija za istraživanje destinacija, mesta, smeštaja i događaja. Korisnička aplikacija je Angular frontend koji pokriva Explore, Map, listing stranice, detalje mesta/smeštaja/događaja, pretragu, profil, omiljene stavke, kolekcije, interesovanja, preporuke i korisničke doprinose.

Bekend je ASP.NET Core API koji čuva katalog, korisnike, recenzije, kolekcije, doprinose, notifikacije, analitiku, mapne pretrage i AI/MCP integracije. Frontend ne koristi direktne pozive ka eksternim mapnim i AI provider servisima za glavne tokove: ti pozivi idu kroz bekend.

Ask Agent je backend-driven AI funkcionalnost za pitanja nad GeoTrack katalogom. Angular prikazuje provider/model/key opcije koje dobije od bekenda, a chat se izvršava preko servera. GeoTrack MCP je zaseban remote HTTP JSON-RPC tool endpoint za spoljne AI alate koji podržavaju MCP.

## Tehnologije

**Frontend**

- Angular 21.x
- TypeScript 5.9
- SCSS, uz Tailwind v4 gde je već uveden
- MapLibre GL za mapu i map picker tokove
- RxJS

**Bekend**

- .NET 8 / ASP.NET Core Web API
- EF Core 8
- PostgreSQL preko Npgsql
- PostGIS i NetTopologySuite za prostorne upite
- ASP.NET Core Data Protection za šifrovanje sačuvanih korisničkih AI ključeva
- JWT bearer autentikacija i HttpOnly refresh cookie
- Gemini AI provider adapter; OpenAI i Claude postoje kao trenutno nedostupni scaffolding provideri
- Remote HTTP JSON-RPC MCP endpoint na `/api/mcp`

## Struktura projekta

- `user-app` - Angular korisnički frontend.
- `admin-app` - Angular admin površina u repozitorijumu; nije primarni fokus ovog README-a.
- `server/Geotrack` - .NET solution.
  - `Geotrack.Domain` - entiteti i enum-i.
  - `Geotrack.Application` - DTO-ovi, interfejsi servisa i opcije.
  - `Geotrack.Infrastructure` - EF Core `AppDbContext`, repozitorijumi, servisi, migracije i Agent/MCP alatni sloj.
  - `Geotrack.Server` - API kontroleri, DI/startup, middleware i statički `/media` fajlovi.
  - `Geotrack.IntegrationTests` - integracioni testovi.

## Pokretanje frontend-a

Komande se pokreću iz ovog `src` foldera:

```bash
cd user-app
npm install
npm run start
```

Angular dev server je podrazumevano na `http://localhost:4200/`. `user-app/proxy.conf.json` prosleđuje `/api` i `/media` ka `http://localhost:5003`, pa lokalni frontend očekuje da bekend radi na tom portu. U Windows PowerShell-u možete koristiti `npm.cmd` umesto `npm` ako je potrebno.

Build frontend-a:

```bash
cd user-app
npm run build
```

## Pokretanje bekenda

Preduslovi:

- .NET 8 SDK
- PostgreSQL baza
- instaliran PostGIS za PostgreSQL server

Osnovno podešavanje lokalnih tajni:

```bash
dotnet user-secrets init --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=geotrack;Username=<korisnik>;Password=<lozinka>" --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "Jwt:Key" "<random-string-od-najmanje-32-karaktera>" --project server/Geotrack/Geotrack.Server
```

`Jwt:Issuer` i `Jwt:Audience` imaju netajne podrazumevane vrednosti u `appsettings.json`.

Restore, build i pokretanje:

```bash
dotnet restore server/Geotrack/Geotrack.sln
dotnet build server/Geotrack/Geotrack.sln
dotnet run --project server/Geotrack/Geotrack.Server
```

Bekend u razvojnom profilu sluša na `http://localhost:5003`. U Development modu pri startu proverava PostGIS, automatski primenjuje EF migracije i pokreće seed podataka.

Demo korisnik iz seed podataka:

- regional manager
- email: `pera@gmail.com`
- lozinka: `Proba123!`

Ako migracije želite da primenite ručno preko `dotnet-ef`, iz `src` foldera koristite:

```bash
dotnet ef database update --project server/Geotrack/Geotrack.Infrastructure --startup-project server/Geotrack/Geotrack.Server
```

## Konfiguracija baze

GeoTrack koristi PostgreSQL sa PostGIS ekstenzijom. `AppDbContext` registruje `postgis`, koristi `UseNetTopologySuite()` i ima `geography(point)` kolone i GIST indekse za destinacije, lokacije, mesta i događaje.

Connection string se podešava kroz user-secrets, environment variable ili lokalni `appsettings.*.json`. Ne stavljajte lozinke u git. Design-time `AppDbContextFactory` može da pročita `ConnectionStrings__DefaultConnection` iz environment-a ili user-secrets vrednost iz `Geotrack.Server` projekta.

U Development modu server pokušava:

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
```

Ako korisnik baze nema dozvolu za to, ekstenziju napravite ručno nad bazom pre pokretanja aplikacije.

## Ask Agent

Ask Agent je backend-driven AI funkcionalnost. Angular ne zove Gemini, OpenAI, Claude niti druge model providere direktno. Frontend učitava opcije preko:

- `GET /api/agent/providers`

Glavni chat ide preko:

- `POST /api/agent/chat`

Legacy endpoint `POST /api/agent/message` još postoji zbog kompatibilnosti, ali novi tok koristi `/api/agent/chat`.

Provider i model liste dolaze sa bekenda. Gemini je trenutno implementirani provider adapter. OpenAI i Claude postoje u konfiguraciji kao nedostupni scaffolding provideri dok se ne dodaju realni adapteri i server-side konfiguracija.

Korisnik može da koristi:

- `project-demo` key, ako je Gemini API key podešen na bekendu;
- sačuvani korisnički API key;
- privremeni session key koji se šalje samo za jedan zahtev.

API key se ne čuva u browser storage-u. Sačuvani korisnički ključ čuva se server-side u `UserAiProviderKeys` i šifruje se preko ASP.NET Core Data Protection. Frontend dobija samo bezbedne metapodatke, kao što su provider id i poslednja 4 karaktera ključa.

## Testiranje Ask Agent-a pomoću Gemini API key-ja

Za lokalno testiranje možete napraviti Gemini API key u Google AI Studio-u. Nemojte ga stavljati u Angular, source kod, `environment` fajlove frontenda ili commit-ovane konfiguracije.

Project/demo podešavanje preko user-secrets:

```bash
dotnet user-secrets set "Agent:AllowProjectDemoProvider" "true" --project server/Geotrack/Geotrack.Server
dotnet user-secrets set "Agent:Providers:gemini:ApiKey" "<gemini-api-key>" --project server/Geotrack/Geotrack.Server
```

Ista podešavanja kao environment variable imena:

```text
Agent__AllowProjectDemoProvider=true
Agent__Providers__gemini__ApiKey=<gemini-api-key>
```

`project-demo` režim se prikazuje samo kada je dozvoljen i kada bekend ima stvaran Gemini API key. Alternativno, prijavljen korisnik može u Ask Agent podešavanjima da unese Gemini key kao sačuvani key ili privremeni session key.

Free quota i uslovi korišćenja zavise od Google AI Studio/Gemini pravila. Za produkciju moraju da se kontrolišu kvote, rate-limit-i, monitoring i trajno čuvanje Data Protection ključeva.

## MCP konektor

GeoTrack MCP je remote HTTP JSON-RPC endpoint:

- `GET /api/mcp` vraća bezbedan status.
- `POST /api/mcp` obrađuje MCP JSON-RPC zahteve.

Ovo je tool server, ne chatbot. Spoljni MCP hostovi kao Codex, Claude ili ChatGPT-compatible klijenti odlučuju kada pozivaju GeoTrack alate. Trenutni MCP alati su public, read-only i ograničeni; ne izlažu privatne korisničke podatke, write operacije, admin operacije, moderation operacije, SQL, fajlove, tokene ili provider API ključeve.

Trenutni allowlist:

- `geotrack_search_places`
- `geotrack_get_place_details`
- `geotrack_search_stays`
- `geotrack_get_stay_details`
- `geotrack_search_events`
- `geotrack_get_event_details`
- `geotrack_get_map_filters`

Lokalno povezivanje u Codex aplikaciji:

- prvo pokrenite bekend;
- koristite bekend port, ne Angular port, osim ako ste posebno proverili proxy;
- MCP URL: `http://localhost:5003/api/mcp`
- Transport: `Streamable HTTP`
- Bearer token: prazno za trenutni public read-only MVP
- Headers: prazno

Primeri zahteva koje možete dati Codex-u:

```text
Use GeoTrack MCP to search places in Dubrovnik.
Call geotrack_search_places with location Dubrovnik and query places.
Call geotrack_search_stays with location Dubrovnik and query hotels.
```

Za javni ChatGPT/Claude connector potreban je javni HTTPS URL, ne `localhost`. Private MCP tools nisu implementirani dok se ne završi auth dizajn i review.

## Bezbednosne napomene

- Ne commitovati API ključeve, lozinke, connection string lozinke ili provider tokene.
- Provider API key ne sme da ide u frontend kod ili browser storage.
- Sačuvani korisnički AI key čuva se samo server-side i šifruje se Data Protection mehanizmom.
- Privremeni session key koristi se samo za zahtev koji ga šalje.
- Precizna GPS lokacija ne sme da se čuva u `localStorage`; trenutni kod koristi `sessionStorage` za `geotrack_location` i briše staru `localStorage` kopiju.
- MCP trenutno ne izlaže write, admin, moderation ili private user tools.
- Bekend endpoint-i treba da zadrže postojeću validaciju i rate-limit politike.

## Poznata ograničenja i budući rad

- MCP private user tools nisu dostupni dok se ne uradi auth dizajn i security review.
- OpenAI i Claude provider adapteri nisu aktivni; trenutno su nedostupni scaffolding provideri.
- Produkcioni deployment treba da podesi trajno čuvanje Data Protection ključeva pre oslanjanja na sačuvane korisničke AI ključeve.
- Javni MCP connector zahteva HTTPS deployment.
- Notification center postoji, ali `/settings/notifications` trenutno preusmerava nazad na `/settings`.
- `admin-app` postoji u repozitorijumu, ali nije opisan kao kompletan produkcioni admin proizvod u ovom README-u.
- Angular build ima stroge bundle/style budget-e u `user-app/angular.json`; veće SCSS izmene mogu da izazovu budget upozorenja.

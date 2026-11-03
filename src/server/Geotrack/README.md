# GeoTrack Server

`src/server/Geotrack` sadrži .NET 8 API, EF Core model/migracije i integracione testove.

Komande ispod pokreću se iz `src/server/Geotrack`. Za instalaciju, frontend i
produkciono pokretanje pogledajte [root README](../../../README.md).

## Potrebne lokalne tajne

Ovo podesite jednom pomoću user-secrets:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=geotrack;Username=postgres;Password=<your-password>" --project Geotrack.Server
dotnet user-secrets set "Jwt:Key" "<a-random-32+-character-string>" --project Geotrack.Server
```

`Jwt:Issuer` i `Jwt:Audience` dolaze kao podrazumevane vrednosti koje nisu tajne u `appsettings.json`. Aplikacija sada odmah prekida pokretanje ako nedostaje connection string ili JWT ključ.

## PostgreSQL extension preduslovi

GeoTrack koristi PostgreSQL ekstenzije za prostorne upite i javnu pretragu kataloga.
Lokalna PostgreSQL baza treba da ima:

- `postgis` za location-aware Explore i nearest-content upite
- `pg_trgm` za fuzzy/trigram pretragu javnog kataloga
- `unaccent` za pretragu bez osetljivosti na dijakritike

`vector` / `pgvector` nije potreban i aplikacija ne pravi hard dependency na tu ekstenziju.

Lokalni setup:

1. Instalirajte PostGIS i standardne PostgreSQL contrib ekstenzije za server koji koristite.
2. Proverite da korisnik iz `ConnectionStrings:DefaultConnection` moze da izvrsi:

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;
```

3. U razvojnom okruzenju aplikacija ce pre migracija pokusati da ukljuci ove ekstenzije automatski.
4. Ako neka ekstenzija nije instalirana ili korisnik nema dozvolu za `CREATE EXTENSION`, startup ce pasti sa jasnom porukom.

Rucna provera:

```bash
psql -d geotrack -c "CREATE EXTENSION IF NOT EXISTS postgis;"
psql -d geotrack -c "CREATE EXTENSION IF NOT EXISTS pg_trgm;"
psql -d geotrack -c "CREATE EXTENSION IF NOT EXISTS unaccent;"
psql -d geotrack -c "SELECT PostGIS_Version();"
psql -d geotrack -c "SELECT extname, extversion FROM pg_extension WHERE extname IN ('postgis', 'pg_trgm', 'unaccent', 'vector') ORDER BY extname;"
```

Integracioni testovi kreiraju privremenu PostgreSQL bazu i u njoj ukljucuju iste obavezne ekstenzije pre pokretanja aplikacije.

Trenutna migraciona istorija u repozitorijumu je objedinjena u `20260503002945_InitialCreate`. Taj model ukljucuje PostGIS podršku:

- dodaje `geography(point)` kolone za `Destinations`, `Locations`, `Places` i `Events`
- backfill-uje koordinate iz postojecih `Latitude` / `Longitude` vrednosti
- dodaje GIST prostorne indekse za nearest upite

## Pokretanje

```bash
dotnet run --project Geotrack.Server
```

U razvojnom okruženju, pri pokretanju se automatski primenjuju migracije i unose demo podaci ako je baza prazna.

Demo korisnik:

- Email: `marko.p@example.com`
- Lozinka: `password123`

## Provera

```bash
dotnet build Geotrack.sln
dotnet test Geotrack.IntegrationTests/Geotrack.IntegrationTests.csproj
```

Integracioni testovi kreiraju privremenu PostgreSQL bazu na osnovu podešene lokalne konekcije, pokreću migracije i inicijalni unos demo podataka, izvršavaju tokove autentikacije, favorita i kolekcija, a zatim brišu bazu.

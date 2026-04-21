# GeoTrack Server

`server/Geotrack` sadrži .NET 8 API, EF Core model/migracije i Batch 11 integracione testove.

## Potrebne lokalne tajne

Ovo podesite jednom pomoću user-secrets:

```bash
cd Geotrack.Server
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=geotrack;Username=postgres;Password=<your-password>"
dotnet user-secrets set "Jwt:Key" "<a-random-32+-character-string>"
```

`Jwt:Issuer` i `Jwt:Audience` dolaze kao podrazumevane vrednosti koje nisu tajne u `appsettings.json`. Aplikacija sada odmah prekida pokretanje ako nedostaje connection string ili JWT ključ.

## PostGIS preduslov

GeoTrack sada koristi PostGIS za location-aware Explore i nearest-content upite.
PostgreSQL instanca zato mora imati instaliran PostGIS paket/ekstenziju.

Lokalni setup:

1. Instalirajte PostGIS za PostgreSQL server koji koristite.
2. Proverite da korisnik iz `ConnectionStrings:DefaultConnection` moze da izvrsi:

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
```

3. U razvojnom okruzenju aplikacija ce pre migracija pokusati da ukljuci ekstenziju automatski.
4. Ako PostGIS nije instaliran ili korisnik nema dozvolu za `CREATE EXTENSION`, startup ce pasti sa jasnom porukom.

Rucna provera:

```bash
psql -d geotrack -c "CREATE EXTENSION IF NOT EXISTS postgis;"
psql -d geotrack -c "SELECT PostGIS_Version();"
```

Migracija `AddPostgisExploreSupport`:

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

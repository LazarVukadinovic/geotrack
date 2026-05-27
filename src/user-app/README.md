# GeoTrack User App

`user-app/` je Angular frontend za GeoTrack. Sa .NET API-jem u `../server/Geotrack` komunicira preko lokalne proxy konfiguracije.

## Lokalno pokretanje

```bash
npm install
npm run dev
```

Aplikacija je podrazumevano dostupna na `http://localhost:4200/` i prosleđuje `/api/*` zahteve ka backend-u.

## Build i testiranje

```bash
npm run build
npm run build:production
npm test -- --watch=false
```

## Šta je stvarno, a šta je simulirano

- Autentikacija/sesija, trenutni korisnik, tagovi/interesovanja, favoriti, kolekcije i glavni tokovi za listanje i prikaz detalja sadržaja integrisani su sa backend-om.
- Neki lokalni `data/` fajlovi po funkcionalnostima i dalje postoje kao fiksni primeri podataka ili rezervni sadržaj za izolovane UI celine, ali ponašanje kolekcija, autentikacije i profila tokom rada aplikacije više nije zasnovano samo na mock podacima.

Za full-stack podešavanje, backend tajne i demo kredencijale pogledajte u [README](../README.md) fajlu u korenu repozitorijuma.

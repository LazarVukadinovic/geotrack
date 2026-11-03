# GeoTrack Admin App

Angular frontend for GeoTrack administration, moderation, and territory management.
Run these commands from `src/admin-app`:

```bash
npm ci
npm start
```

The development server listens on `http://localhost:4300`. `proxy.conf.json`
forwards `/api` and `/media` to the backend at `http://localhost:5003`.

```bash
npm run test:ci
npm run build:production
npm run serve:static
```

The production build is written to `dist/admin-app/browser`. The static server
starts on port `10132` by default; production HTTPS and backend configuration are
in the [root README](../../README.md). Tests use Vitest; no e2e target is configured.

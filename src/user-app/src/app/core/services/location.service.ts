import { Injectable, signal, computed, OnDestroy, NgZone, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ResolvedLocation } from '../models/location.model';

/** Minimum metres between two positions before we re-geocode. */
const MIN_DISTANCE_METRES = 500;
/** Minimum milliseconds between reverse-geocode calls. */
const MIN_GEOCODE_INTERVAL_MS = 30_000;
const LOCATION_KEY = 'geotrack_location';
const ENTRY_COMPLETE_KEY = 'geotrack_location_entry_complete';

@Injectable({ providedIn: 'root' })
export class LocationService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly zone = inject(NgZone);

  private watchId: number | null = null;
  private lastGeocodedLat = 0;
  private lastGeocodedLng = 0;
  private lastGeocodeTime = 0;
  private ipFallbackStarted = false;

  /* ── Public reactive state ─────────────────────────────────────────── */

  private readonly _location = signal<ResolvedLocation | null>(null);
  private readonly _loading = signal(true);
  private readonly _error = signal<string | null>(null);

  readonly location = computed(() => this._location());
  readonly loading = computed(() => this._loading());
  readonly error = computed(() => this._error());

  /** True once we have *any* resolved location (session cache, IP, or GPS). */
  readonly ready = computed(() => this._location() !== null);

  /** True after the user has completed the initial location-entry flow at least once. */
  readonly hasCompletedLocationEntry = computed(() => {
    try { return localStorage.getItem(ENTRY_COMPLETE_KEY) === 'true'; } catch { return false; }
  });

  readonly displayLabel = computed(() => {
    const loc = this._location();
    if (loc) return loc.displayLabel;
    if (this._loading()) return 'Locating…';
    return 'Explore';
  });

  readonly displayCity = computed(() => this._location()?.city ?? this.displayLabel());
  readonly displayCountry = computed(() => this._location()?.country ?? '');

  /* ── Lifecycle ─────────────────────────────────────────────────────── */

  constructor() {
    // Restore from local cache — instant, no flicker
    const cached = this.restoreFromStorage();
    if (cached) {
      this._location.set(cached);
      this._loading.set(false);
      this.lastGeocodedLat = cached.latitude;
      this.lastGeocodedLng = cached.longitude;
      this.lastGeocodeTime = Date.now();
    } else {
      // No cache → silent IP fallback (no GPS popup)
      this.fallbackToIp();
    }
  }

  ngOnDestroy(): void {
    this.stopWatching();
  }

  /**
   * Mark the initial location-entry flow as completed.
   * After this, returning users skip the landing page.
   */
  markLocationEntryComplete(): void {
    try { localStorage.setItem(ENTRY_COMPLETE_KEY, 'true'); } catch { /* quota */ }
  }

  /**
   * Attempt a background GPS refresh without blocking the user.
   * Used on Explore for returning users — silently upgrades location if GPS is available.
   */
  tryBackgroundGpsRefresh(): void {
    if (!('geolocation' in navigator)) return;

    navigator.geolocation.getCurrentPosition(
      (pos) => {
        this.zone.run(() => {
          const { latitude, longitude } = pos.coords;
          const current = this._location();
          // Only update if meaningfully different from cached location
          if (current) {
            const dist = this.haversineMetres(current.latitude, current.longitude, latitude, longitude);
            if (dist < MIN_DISTANCE_METRES) return;
          }
          this._location.set({
            latitude,
            longitude,
            city: current?.city ?? '',
            region: current?.region ?? '',
            country: current?.country ?? '',
            displayLabel: current?.displayLabel ?? 'Explore',
            source: 'gps',
          });
          this._loading.set(false);
          this.saveToStorage(this._location()!);
          this.reverseGeocode(latitude, longitude, 'gps');
          this.startWatching();
        });
      },
      () => { /* GPS unavailable — no action, keep cached location */ },
      { enableHighAccuracy: false, timeout: 10_000 },
    );
  }

  /* ── Explicit GPS request (called only from landing page) ──────────── */

  /**
   * Requests browser geolocation permission and resolves GPS coordinates.
   * Does NOT trigger automatically — only when user explicitly asks.
   * Returns coordinates immediately; reverse-geocode runs in background.
   */
  requestGps(): Promise<{ latitude: number; longitude: number }> {
    return new Promise((resolve, reject) => {
      if (!('geolocation' in navigator)) {
        reject(new Error('Geolocation not supported'));
        return;
      }

      navigator.geolocation.getCurrentPosition(
        (pos) => {
          const { latitude, longitude } = pos.coords;

          // Set a minimal location immediately (coords known, city pending)
          const current = this._location();
          if (!current || current.source !== 'gps') {
            this._location.set({
              latitude,
              longitude,
              city: current?.city ?? '',
              region: current?.region ?? '',
              country: current?.country ?? '',
              displayLabel: current?.displayLabel ?? 'Explore',
              source: 'gps',
            });
            this._loading.set(false);
            this.saveToStorage(this._location()!);
          }

          // Reverse-geocode in background to get city/country
          this.reverseGeocode(latitude, longitude, 'gps');

          // Start watching for position updates
          this.startWatching();

          resolve({ latitude, longitude });
        },
        (err) => {
          reject(err);
        },
        { enableHighAccuracy: false, timeout: 10_000 },
      );
    });
  }

  /**
   * Ensures IP fallback has been triggered if no location is available yet.
   * Safe to call multiple times — only runs once.
   * Returns a promise that resolves when IP location is available
   * (or immediately if we already have a location).
   */
  ensureIpFallback(): Promise<ResolvedLocation | null> {
    if (this._location()) {
      return Promise.resolve(this._location());
    }
    return new Promise((resolve) => {
      if (!this.ipFallbackStarted) {
        this.fallbackToIp(() => resolve(this._location()));
      } else {
        // Already in-flight — poll until resolved
        const check = () => {
          if (this._location()) { resolve(this._location()); return; }
          if (!this._loading()) { resolve(null); return; }
          setTimeout(check, 100);
        };
        check();
      }
    });
  }

  /**
   * Set a manually selected destination as the active location.
   * Used when user picks a destination from search on the landing page.
   */
  setManualDestination(lat: number, lng: number, name: string): void {
    const loc: ResolvedLocation = {
      latitude: lat,
      longitude: lng,
      city: name,
      region: '',
      country: '',
      displayLabel: name,
      source: 'manual',
    };
    this._location.set(loc);
    this._loading.set(false);
    this._error.set(null);
    this.saveToStorage(loc);

    // Reverse-geocode in background to fill in region/country
    this.reverseGeocode(lat, lng, 'manual');
  }

  /* ── GPS watch (continuous updates after initial fix) ───────────────── */

  private handlePosition(pos: GeolocationPosition, startWatching: boolean): void {
    const { latitude, longitude } = pos.coords;

    if (this.shouldReverseGeocode(latitude, longitude)) {
      this.reverseGeocode(latitude, longitude, 'gps');
    }

    if (startWatching) {
      this.startWatching();
    }
  }

  private startWatching(): void {
    if (this.watchId !== null) return;

    this.watchId = navigator.geolocation.watchPosition(
      (pos) => {
        this.zone.run(() => this.handlePosition(pos, false));
      },
      () => { /* watch errors are non-fatal — we already have a position */ },
      { enableHighAccuracy: false, maximumAge: 60_000 },
    );
  }

  private stopWatching(): void {
    if (this.watchId !== null) {
      navigator.geolocation.clearWatch(this.watchId);
      this.watchId = null;
    }
  }

  /* ── Throttle logic ────────────────────────────────────────────────── */

  private shouldReverseGeocode(lat: number, lng: number): boolean {
    const now = Date.now();
    if (now - this.lastGeocodeTime < MIN_GEOCODE_INTERVAL_MS) return false;

    const dist = this.haversineMetres(
      this.lastGeocodedLat, this.lastGeocodedLng, lat, lng,
    );
    // First call (0,0 → actual) always passes; subsequent need MIN_DISTANCE
    if (this.lastGeocodeTime > 0 && dist < MIN_DISTANCE_METRES) return false;

    return true;
  }

  private haversineMetres(
    lat1: number, lon1: number, lat2: number, lon2: number,
  ): number {
    const R = 6_371_000;
    const toRad = (deg: number) => (deg * Math.PI) / 180;
    const dLat = toRad(lat2 - lat1);
    const dLon = toRad(lon2 - lon1);
    const a =
      Math.sin(dLat / 2) ** 2 +
      Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
    return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  }

  /* ── Reverse geocoding (OpenStreetMap Nominatim) ───────────────────── */

  private reverseGeocode(lat: number, lng: number, source: 'gps' | 'ip' | 'manual'): void {
    this.lastGeocodedLat = lat;
    this.lastGeocodedLng = lng;
    this.lastGeocodeTime = Date.now();

    const url =
      `https://nominatim.openstreetmap.org/reverse?lat=${lat}&lon=${lng}&format=json&accept-language=en`;

    this.http.get<NominatimResponse>(url).subscribe({
      next: (res) => {
        const addr = res.address ?? {};
        const city = this.extractCity(addr);
        const region = addr.state ?? addr.county ?? '';
        const country = addr.country ?? '';
        const displayLabel = this.buildDisplayLabel(city, region, country);

        const loc: ResolvedLocation = {
          latitude: lat,
          longitude: lng,
          city,
          region,
          country,
          displayLabel,
          source,
        };
        this._location.set(loc);
        this._loading.set(false);
        this._error.set(null);
        this.saveToStorage(loc);
      },
      error: () => {
        // Geocode failed but we still have coords
        if (!this._location()) {
          this._location.set({
            latitude: lat,
            longitude: lng,
            city: '',
            region: '',
            country: '',
            displayLabel: 'Explore',
            source,
          });
        }
        this._loading.set(false);
      },
    });
  }

  private extractCity(addr: NominatimAddress): string {
    return (
      addr.city ??
      addr.town ??
      addr.village ??
      addr.municipality ??
      addr.county ??
      addr.state_district ??
      addr.state ??
      ''
    );
  }

  private buildDisplayLabel(city: string, region: string, country: string): string {
    if (city && country) return `${city}, ${country}`;
    if (city) return city;
    if (region && country) return `${region}, ${country}`;
    if (region) return region;
    if (country) return country;
    return 'Explore';
  }

  /* ── IP-based fallback ─────────────────────────────────────────────── */

  private fallbackToIp(onComplete?: () => void): void {
    this.ipFallbackStarted = true;
    // ip-api.com free tier — no key needed, returns city/region/country
    this.http
      .get<IpApiResponse>('http://ip-api.com/json/?fields=status,city,regionName,country,lat,lon')
      .subscribe({
        next: (res) => {
          if (res.status === 'success') {
            const city = res.city ?? '';
            const region = res.regionName ?? '';
            const country = res.country ?? '';
            const displayLabel = this.buildDisplayLabel(city, region, country);

            const loc: ResolvedLocation = {
              latitude: res.lat ?? 0,
              longitude: res.lon ?? 0,
              city,
              region,
              country,
              displayLabel,
              source: 'ip',
            };
            this._location.set(loc);
            this.saveToStorage(loc);
          }
          this._loading.set(false);
          onComplete?.();
        },
        error: () => {
          this._loading.set(false);
          this._error.set('Unable to determine location');
          onComplete?.();
        },
      });
  }

  /* ── Local persistence ─────────────────────────────────────────────── */

  private saveToStorage(loc: ResolvedLocation): void {
    try {
      localStorage.setItem(LOCATION_KEY, JSON.stringify(loc));
    } catch { /* quota exceeded — ignore */ }
  }

  private restoreFromStorage(): ResolvedLocation | null {
    try {
      // Check localStorage first, fall back to sessionStorage for migration
      const raw = localStorage.getItem(LOCATION_KEY) ?? sessionStorage.getItem(LOCATION_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw);
      if (typeof parsed.latitude === 'number' && typeof parsed.longitude === 'number') {
        return parsed as ResolvedLocation;
      }
    } catch { /* corrupt data — ignore */ }
    return null;
  }
}

/* ── External API response shapes ────────────────────────────────────── */

interface NominatimAddress {
  city?: string;
  town?: string;
  village?: string;
  municipality?: string;
  county?: string;
  state_district?: string;
  state?: string;
  country?: string;
  [key: string]: string | undefined;
}

interface NominatimResponse {
  address?: NominatimAddress;
}

interface IpApiResponse {
  status: string;
  city?: string;
  regionName?: string;
  country?: string;
  lat?: number;
  lon?: number;
}

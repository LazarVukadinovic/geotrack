import { Injectable, signal, computed, OnDestroy, NgZone, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ResolvedLocation } from '../models/location.model';
import { MapApiService } from './map-api.service';

/** Minimum metres between two positions before we re-geocode. */
const MIN_DISTANCE_METRES = 500;
/** Minimum milliseconds between reverse-geocode calls. */
const MIN_GEOCODE_INTERVAL_MS = 30_000;
const LOCATION_KEY = 'geotrack_location';
const ENTRY_COMPLETE_KEY = 'geotrack_location_entry_complete';

@Injectable({ providedIn: 'root' })
export class LocationService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly mapApi = inject(MapApiService);
  private readonly zone = inject(NgZone);

  private watchId: number | null = null;
  private lastGeocodedLat = 0;
  private lastGeocodedLng = 0;
  private lastGeocodeTime = 0;
  private ipFallbackStarted = false;

  /* ── Public reactive state ─────────────────────────────────────────── */

  private readonly _location = signal<ResolvedLocation | null>(null);
  private readonly _approximateLocation = signal<ResolvedLocation | null>(null);
  private readonly _loading = signal(true);
  private readonly _error = signal<string | null>(null);
  private readonly _hasCompletedLocationEntry = signal(this.readEntryCompleteFromStorage());

  readonly location = computed(() => this._location());
  readonly routingOrigin = computed(() => {
    const loc = this._location();
    if (this.isRoutingOrigin(loc)) return loc;

    const approximate = this._approximateLocation();
    return this.isRoutingOrigin(approximate) ? approximate : null;
  });
  readonly loading = computed(() => this._loading());
  readonly error = computed(() => this._error());

  /** True once we have *any* resolved location (session cache, IP, or GPS). */
  readonly ready = computed(() => this._location() !== null);

  /** True after the user has completed the initial location-entry flow at least once. */
  readonly hasCompletedLocationEntry = computed(() => this._hasCompletedLocationEntry());

  readonly displayLabel = computed(() => {
    const loc = this._location();
    if (loc) return loc.displayLabel;
    if (this._loading()) return 'Locating…';
    return 'Explore';
  });

  readonly displayCity = computed(() => {
    const loc = this._location();
    if (!loc) return this.displayLabel();

    return loc.city.trim()
      || loc.region.trim()
      || loc.country.trim()
      || loc.displayLabel.trim()
      || 'Explore';
  });
  readonly displayCountry = computed(() => this._location()?.country.trim() ?? '');
  readonly sourceLabel = computed(() => {
    const source = this._location()?.source;
    if (source === 'gps') return 'exact location';
    if (source === 'manual') return 'selected location';
    return 'nearby location';
  });

  /* ── Lifecycle ─────────────────────────────────────────────────────── */

  constructor() {
    // Restore from local cache — instant, no flicker
    const cached = this.restoreFromStorage();
    if (cached) {
      this._location.set(cached);
      if (cached.source === 'ip' && this.hasUsableCoordinates(cached.latitude, cached.longitude)) {
        this._approximateLocation.set(cached);
      }
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
    this._hasCompletedLocationEntry.set(true);
    try { localStorage.setItem(ENTRY_COMPLETE_KEY, 'true'); } catch { /* quota */ }
  }

  clearSavedLocation(): void {
    this.stopWatching();
    this.lastGeocodedLat = 0;
    this.lastGeocodedLng = 0;
    this.lastGeocodeTime = 0;
    this.ipFallbackStarted = false;
    this._location.set(null);
    this._approximateLocation.set(null);
    this._loading.set(false);
    this._error.set(null);
    this._hasCompletedLocationEntry.set(false);

    try {
      localStorage.removeItem(LOCATION_KEY);
      localStorage.removeItem(ENTRY_COMPLETE_KEY);
      sessionStorage.removeItem(LOCATION_KEY);
    } catch {
      // storage may be unavailable; silent
    }
  }

  /**
   * Attempt a background GPS refresh without blocking the user.
   * Used on Explore for returning users — silently upgrades location if GPS is available.
   */
  tryBackgroundGpsRefresh(): void {
    if (!('geolocation' in navigator)) {
      // No geolocation API — refresh via IP so we don't keep stale cache
      this.refreshFromIp();
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (pos) => {
        this.zone.run(() => {
          const { latitude, longitude } = pos.coords;
          const current = this._location();
          // Only update if meaningfully different from cached location
          if (current?.source === 'gps') {
            const dist = this.haversineMetres(current.latitude, current.longitude, latitude, longitude);
            if (dist < MIN_DISTANCE_METRES) return;
          }
          this._location.set({
            latitude,
            longitude,
            city: current?.source === 'gps' ? current.city : '',
            region: current?.source === 'gps' ? current.region : '',
            country: current?.source === 'gps' ? current.country : '',
            displayLabel: current?.source === 'gps' ? current.displayLabel : 'Explore',
            source: 'gps',
          });
          this._loading.set(false);
          this.saveToStorage(this._location()!);
          this.reverseGeocode(latitude, longitude, 'gps');
          this.startWatching();
        });
      },
      () => {
        this.zone.run(() => {
          // GPS unavailable/denied — refresh via IP so the header
          // shows the current IP-based location instead of stale cache
          this.refreshFromIp();
        });
      },
      { enableHighAccuracy: false, timeout: 10_000 },
    );
  }

  /* ── Explicit GPS request (called only from landing page) ──────────── */

  /**
   * Requests browser geolocation permission and resolves GPS coordinates.
   * Does NOT trigger automatically — only when user explicitly asks.
   * Returns after reverse-geocode settles so location labels are ready.
   */
  requestGps(): Promise<{ latitude: number; longitude: number }> {
    return new Promise((resolve, reject) => {
      if (!('geolocation' in navigator)) {
        reject(new Error('Geolocation not supported'));
        return;
      }

      navigator.geolocation.getCurrentPosition(
        (pos) => {
          this.zone.run(() => {
            const { latitude, longitude } = pos.coords;

            // Set coordinates immediately; wait for reverse-geocode before the
            // caller navigates so the Explore hero has the country label ready.
            const current = this._location();
            const canReuseCurrentLabel = current?.source === 'gps'
              && this.haversineMetres(current.latitude, current.longitude, latitude, longitude) < MIN_DISTANCE_METRES;
            this._location.set({
              latitude,
              longitude,
              city: canReuseCurrentLabel ? current.city : '',
              region: canReuseCurrentLabel ? current.region : '',
              country: canReuseCurrentLabel ? current.country : '',
              displayLabel: canReuseCurrentLabel ? current.displayLabel : 'Explore',
              source: 'gps',
            });
            this._loading.set(false);
            this.saveToStorage(this._location()!);

            this.reverseGeocode(latitude, longitude, 'gps').finally(() => {
              resolve({ latitude, longitude });
            });

            // Start watching for position updates
            this.startWatching();
          });
        },
        (err) => {
          this.zone.run(() => reject(err));
        },
        { enableHighAccuracy: false, timeout: 10_000 },
      );
    });
  }

  /**
   * Stores exact browser coordinates resolved outside the landing-page flow.
   * Used by the map page's locate control so map directions and app-wide
   * location state share the same GPS source of truth.
   */
  setGpsLocation(latitude: number, longitude: number): void {
    if (!this.hasUsableCoordinates(latitude, longitude)) return;

    const current = this._location();
    const canReuseCurrentLabel = current?.source === 'gps'
      && this.haversineMetres(current.latitude, current.longitude, latitude, longitude) < MIN_DISTANCE_METRES;

    const loc: ResolvedLocation = {
      latitude,
      longitude,
      city: canReuseCurrentLabel ? current.city : '',
      region: canReuseCurrentLabel ? current.region : '',
      country: canReuseCurrentLabel ? current.country : '',
      displayLabel: canReuseCurrentLabel ? current.displayLabel : 'Explore',
      source: 'gps',
    };

    this._location.set(loc);
    this._loading.set(false);
    this._error.set(null);
    this.saveToStorage(loc);
    void this.reverseGeocode(latitude, longitude, 'gps');
    this.startWatching();
  }

  /**
   * Resolve the origin that should be used for route drawing.
   *
   * Routing must never use manual/search/destination state. Exact GPS wins
   * when available; otherwise we use a backend-backed IP location. If neither
   * is honestly available, callers should not draw a route.
   */
  async resolveRoutingOrigin(): Promise<ResolvedLocation | null> {
    const geolocationPermission = await this.getGeolocationPermissionState();
    if (geolocationPermission === 'granted') {
      const exact = await this.getCurrentGpsForRouting();
      if (exact) return exact;
    }

    const existing = this.routingOrigin();
    if (existing?.source === 'gps' && geolocationPermission === null) {
      return existing;
    }
    if (existing?.source === 'ip') {
      return existing;
    }

    const approximate = this._approximateLocation();
    if (this.isRoutingOrigin(approximate)) {
      return approximate;
    }

    return this.fetchApproximateLocationForRouting();
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

  private reverseGeocode(lat: number, lng: number, source: 'gps' | 'ip' | 'manual'): Promise<ResolvedLocation | null> {
    this.lastGeocodedLat = lat;
    this.lastGeocodedLng = lng;
    this.lastGeocodeTime = Date.now();

    return new Promise((resolve) => {
      this.mapApi.reverseGeocode(lat, lng).subscribe({
        next: (res) => {
          const city = res.city ?? '';
          const region = res.region ?? '';
          const country = res.country ?? '';
          const displayLabel = res.displayLabel ?? this.buildDisplayLabel(city, region, country);

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
          resolve(loc);
        },
        error: () => {
          let loc = this._location();
          if (!loc) {
            loc = {
              latitude: lat,
              longitude: lng,
              city: '',
              region: '',
              country: '',
              displayLabel: 'Explore',
              source,
            };
            this._location.set(loc);
          }
          this._loading.set(false);
          resolve(loc);
        },
      });
    });
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

  /**
   * Fetch a fresh IP-based location, replacing any stale cached value.
   * Called when GPS is unavailable so the Explore header always reflects
   * the user's current approximate location rather than a stale cache.
   */
  private refreshFromIp(): void {
    this.http
      .get<ApproximateLocationResponse>('/api/locations/approximate')
      .subscribe({
        next: (res) => {
          const loc = this.mapApproximateLocation(res);
          if (!this.isRoutingOrigin(loc)) {
            return;
          }
          this._approximateLocation.set(loc);
          this._location.set(loc);
          this._loading.set(false);
          this._error.set(null);
          this.saveToStorage(loc);
        },
        error: () => {
          // IP lookup failed — keep whatever we have (cached or nothing)
        },
      });
  }

  private fallbackToIp(onComplete?: () => void): void {
    this.ipFallbackStarted = true;
    this.http
      .get<ApproximateLocationResponse>('/api/locations/approximate')
      .subscribe({
        next: (res) => {
          const loc = this.mapApproximateLocation(res);
          if (!this.isRoutingOrigin(loc)) {
            this._loading.set(false);
            this._error.set('Unable to determine location');
            onComplete?.();
            return;
          }
          this._approximateLocation.set(loc);
          this._location.set(loc);
          this.saveToStorage(loc);
          this._loading.set(false);
          this._error.set(null);
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

  private readEntryCompleteFromStorage(): boolean {
    try {
      return localStorage.getItem(ENTRY_COMPLETE_KEY) === 'true';
    } catch {
      return false;
    }
  }

  private fetchApproximateLocationForRouting(): Promise<ResolvedLocation | null> {
    return new Promise((resolve) => {
      this.http
        .get<ApproximateLocationResponse>('/api/locations/approximate')
        .subscribe({
          next: (res) => {
            const loc = this.mapApproximateLocation(res);
            if (!this.isRoutingOrigin(loc)) {
              resolve(null);
              return;
            }

            this._approximateLocation.set(loc);
            if (!this._location()) {
              this._location.set(loc);
              this._loading.set(false);
              this._error.set(null);
              this.saveToStorage(loc);
            }
            resolve(loc);
          },
          error: () => resolve(null),
        });
    });
  }

  private async getGeolocationPermissionState(): Promise<PermissionState | null> {
    if (typeof navigator === 'undefined' || !('permissions' in navigator)) {
      return null;
    }

    try {
      const status = await navigator.permissions.query({ name: 'geolocation' as PermissionName });
      return status.state;
    } catch {
      return null;
    }
  }

  private getCurrentGpsForRouting(): Promise<ResolvedLocation | null> {
    if (typeof navigator === 'undefined' || !('geolocation' in navigator)) {
      return Promise.resolve(null);
    }

    return new Promise((resolve) => {
      navigator.geolocation.getCurrentPosition(
        (pos) => {
          this.zone.run(() => {
            const { latitude, longitude } = pos.coords;
            this.setGpsLocation(latitude, longitude);
            resolve(this._location());
          });
        },
        () => resolve(null),
        { enableHighAccuracy: false, timeout: 10_000, maximumAge: 60_000 },
      );
    });
  }

  private isRoutingOrigin(loc: ResolvedLocation | null): loc is ResolvedLocation {
    return loc !== null &&
      (loc.source === 'gps' || loc.source === 'ip') &&
      this.hasUsableCoordinates(loc.latitude, loc.longitude);
  }

  private hasUsableCoordinates(latitude: number | null | undefined, longitude: number | null | undefined): boolean {
    return typeof latitude === 'number' &&
      typeof longitude === 'number' &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      latitude >= -90 &&
      latitude <= 90 &&
      longitude >= -180 &&
      longitude <= 180 &&
      (latitude !== 0 || longitude !== 0);
  }

  private mapApproximateLocation(res: ApproximateLocationResponse): ResolvedLocation {
    const city = res.city ?? '';
    const region = res.region ?? '';
    const country = res.country ?? '';

    return {
      latitude: res.latitude ?? 0,
      longitude: res.longitude ?? 0,
      city,
      region,
      country,
      displayLabel: res.displayLabel ?? this.buildDisplayLabel(city, region, country),
      source: 'ip',
    };
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

interface ApproximateLocationResponse {
  latitude?: number;
  longitude?: number;
  city?: string;
  region?: string;
  country?: string;
  displayLabel?: string;
  source?: string;
}

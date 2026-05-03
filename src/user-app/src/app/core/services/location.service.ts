import { Injectable, signal, computed, OnDestroy, NgZone, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ResolvedLocation } from '../models/location.model';
import { MapApiService } from './map-api.service';

/** Minimum metres between two positions before we re-geocode. */
const MIN_DISTANCE_METRES = 500;
/** Minimum milliseconds between reverse-geocode calls. */
const MIN_GEOCODE_INTERVAL_MS = 30_000;
const LOCATION_CACHE_TTL_MS = 60 * 60 * 1000;
const MAP_WATCH_MIN_INTERVAL_MS = 15_000;
const MAP_WATCH_MIN_DISTANCE_METRES = 50;
const LOCATION_KEY = 'geotrack_location';
const ENTRY_COMPLETE_KEY = 'geotrack_location_entry_complete';
const LOCATION_SESSION_SCHEMA_VERSION = 2;
const APPROXIMATE_DISCLOSURE_KEY = 'geotrack_approximate_location_disclosure_v1';

type LocationSource = ResolvedLocation['source'];
type ApproximateDisclosureState = 'accepted' | 'declined' | null;

interface StoredLocationPayload {
  schemaVersion: number;
  location: ResolvedLocation;
}

@Injectable({ providedIn: 'root' })
export class LocationService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly mapApi = inject(MapApiService);
  private readonly zone = inject(NgZone);

  private watchId: number | null = null;
  private lastGeocodedLat = 0;
  private lastGeocodedLng = 0;
  private lastGeocodeTime = 0;
  private approximateInFlight: Promise<ResolvedLocation | null> | null = null;
  private approximateDisclosurePromise: Promise<boolean> | null = null;
  private resolveApproximateDisclosure: ((accepted: boolean) => void) | null = null;
  private lastMapWatchLat = 0;
  private lastMapWatchLng = 0;
  private lastMapWatchUpdateTime = 0;

  /* ── Public reactive state ─────────────────────────────────────────── */

  private readonly _location = signal<ResolvedLocation | null>(null);
  private readonly _approximateLocation = signal<ResolvedLocation | null>(null);
  private readonly _loading = signal(false);
  private readonly _error = signal<string | null>(null);
  private readonly _hasCompletedLocationEntry = signal(this.readEntryCompleteFromStorage());
  private readonly _approximateDisclosureState = signal<ApproximateDisclosureState>(this.readApproximateDisclosureFromStorage());
  private readonly _approximateDisclosureVisible = signal(false);

  readonly location = computed(() => this._location());
  readonly routingOrigin = computed(() => {
    const loc = this._location();
    if (this.isRoutingOrigin(loc)) return loc;

    const approximate = this._approximateLocation();
    return this.isRoutingOrigin(approximate) ? approximate : null;
  });
  readonly loading = computed(() => this._loading());
  readonly error = computed(() => this._error());
  readonly approximateDisclosureVisible = computed(() => this._approximateDisclosureVisible());
  readonly approximateDisclosureState = computed(() => this._approximateDisclosureState());

  /** True once we have *any* resolved location (session cache, approximate, manual, or GPS). */
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
    if (source === 'approximate') return 'approximate location';
    return 'nearby location';
  });

  /* ── Lifecycle ─────────────────────────────────────────────────────── */

  constructor() {
    this.discardUnsafeLocalStorageLocation();

    // Restore only from the current browser session. Long-term storage never
    // carries coordinates.
    const cached = this.restoreFromStorage();
    if (cached) {
      this._location.set(cached);
      if (cached.source === 'approximate' && this.hasUsableCoordinates(cached.latitude, cached.longitude)) {
        this._approximateLocation.set(cached);
      }
      this._loading.set(false);
      this.lastGeocodedLat = cached.latitude;
      this.lastGeocodedLng = cached.longitude;
      this.lastGeocodeTime = Date.now();
    } else {
      this._loading.set(false);
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

  acceptApproximateFallback(): void {
    this.setApproximateDisclosureState('accepted');
    this._approximateDisclosureVisible.set(false);
    this.resolveApproximateDisclosure?.(true);
  }

  declineApproximateFallback(): void {
    this.setApproximateDisclosureState('declined');
    this._approximateDisclosureVisible.set(false);
    this._loading.set(false);
    this.resolveApproximateDisclosure?.(false);
  }

  clearSavedLocation(): void {
    this.stopWatching();
    this.lastGeocodedLat = 0;
    this.lastGeocodedLng = 0;
    this.lastGeocodeTime = 0;
    this.approximateInFlight = null;
    this.approximateDisclosurePromise = null;
    this.resolveApproximateDisclosure?.(false);
    this.resolveApproximateDisclosure = null;
    this.lastMapWatchLat = 0;
    this.lastMapWatchLng = 0;
    this.lastMapWatchUpdateTime = 0;
    this._location.set(null);
    this._approximateLocation.set(null);
    this._loading.set(false);
    this._error.set(null);
    this._hasCompletedLocationEntry.set(false);
    this._approximateDisclosureState.set(null);
    this._approximateDisclosureVisible.set(false);

    try {
      localStorage.removeItem(LOCATION_KEY);
      localStorage.removeItem(ENTRY_COMPLETE_KEY);
      localStorage.removeItem(APPROXIMATE_DISCLOSURE_KEY);
      sessionStorage.removeItem(LOCATION_KEY);
    } catch {
      // storage may be unavailable; silent
    }
  }

  /**
   * Attempt a background GPS refresh without blocking the user.
   * Used on Explore for returning users — silently upgrades location only when
   * browser permission is already granted. Approximate fallback is never started
   * from this background path.
   */
  tryBackgroundGpsRefresh(): void {
    const current = this._location();
    if (current?.source === 'gps' && this.isCacheFresh(current)) {
      return;
    }

    void this.getGeolocationPermissionState().then((permission) => {
      if (permission === 'granted') {
        void this.fetchExactLocation({ enableHighAccuracy: false, maximumAge: 60_000, timeout: 10_000 });
        return;
      }

      this._loading.set(false);
    });
  }

  async resolveLocationForProductFlow(): Promise<ResolvedLocation | null> {
    const current = this._location();
    if (current && (current.source === 'manual' || this.isCacheFresh(current))) {
      return current;
    }

    const permission = await this.getGeolocationPermissionState();
    if (permission === 'granted') {
      const exact = await this.fetchExactLocation({ enableHighAccuracy: false, maximumAge: 60_000, timeout: 10_000 });
      if (exact) return exact;
    }

    return this.ensureIpFallback();
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
              cachedAt: Date.now(),
            });
            this._loading.set(false);
            this.saveToStorage(this._location()!);

            this.reverseGeocode(latitude, longitude, 'gps').finally(() => {
              resolve({ latitude, longitude });
            });

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
      cachedAt: Date.now(),
    };

    this._location.set(loc);
    this._loading.set(false);
    this._error.set(null);
    this.saveToStorage(loc);
    void this.reverseGeocode(latitude, longitude, 'gps');
  }

  /**
   * Resolve the origin that should be used for route drawing.
   *
   * Routing must never use manual/search/destination state. Exact GPS wins
   * when available; otherwise we use a disclosed approximate network location.
   * If neither is honestly available, callers should not draw a route.
   */
  async resolveRoutingOrigin(): Promise<ResolvedLocation | null> {
    const current = this._location();
    if (current?.source === 'gps' && this.isRoutingOrigin(current) && this.isCacheFresh(current)) {
      return current;
    }

    const geolocationPermission = await this.getGeolocationPermissionState();
    if (geolocationPermission === 'granted') {
      const exact = await this.fetchExactLocation({ enableHighAccuracy: false, timeout: 10_000, maximumAge: 60_000 });
      if (exact) return exact;
    }

    if (current?.source === 'approximate' && this.isRoutingOrigin(current) && this.isCacheFresh(current)) {
      return current;
    }

    const existing = this.routingOrigin();
    if (existing?.source === 'gps' && geolocationPermission === null && this.isCacheFresh(existing)) {
      return existing;
    }
    if (existing?.source === 'approximate' && this.isCacheFresh(existing)) {
      return existing;
    }

    const approximate = this._approximateLocation();
    if (this.isRoutingOrigin(approximate) && this.isCacheFresh(approximate)) {
      return approximate;
    }

    return this.fetchApproximateLocationForRouting();
  }

  /**
   * Ensures approximate fallback has been triggered if no location is available yet.
   * Safe to call multiple times. The backend call runs only after the user
   * accepts the approximate-location disclosure.
   * Returns a promise that resolves when approximate location is available
   * (or immediately if we already have a location).
   */
  ensureIpFallback(): Promise<ResolvedLocation | null> {
    const current = this._location();
    if (current && (current.source === 'manual' || this.isCacheFresh(current))) {
      return Promise.resolve(current);
    }

    const approximate = this._approximateLocation();
    if (this.isRoutingOrigin(approximate) && this.isCacheFresh(approximate)) {
      if (!current || current.source === 'approximate') {
        this._location.set(approximate);
        this._loading.set(false);
      }
      return Promise.resolve(approximate);
    }

    return this.fetchApproximateLocationForRouting();
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
      cachedAt: Date.now(),
    };
    this._location.set(loc);
    this._loading.set(false);
    this._error.set(null);
    this.saveToStorage(loc);

    // Reverse-geocode in background to fill in region/country
    this.reverseGeocode(lat, lng, 'manual');
  }

  async refreshExactLocationForMap(): Promise<ResolvedLocation | null> {
    const permission = await this.getGeolocationPermissionState();
    if (permission && permission !== 'granted') return null;

    return this.fetchExactLocation({
      enableHighAccuracy: true,
      maximumAge: 0,
      timeout: 12_000,
    });
  }

  startMapLocationUpdates(onUpdate?: (loc: ResolvedLocation) => void): void {
    if (this.watchId !== null || typeof navigator === 'undefined' || !('geolocation' in navigator)) return;

    const current = this._location();
    if (current?.source === 'gps') {
      this.lastMapWatchLat = current.latitude;
      this.lastMapWatchLng = current.longitude;
      this.lastMapWatchUpdateTime = Date.now();
    }

    this.watchId = navigator.geolocation.watchPosition(
      (pos) => {
        this.zone.run(() => {
          const { latitude, longitude } = pos.coords;
          if (!this.shouldAcceptMapWatchUpdate(latitude, longitude)) return;

          this.applyGpsCoordinates(latitude, longitude);
          const loc = this._location();
          if (loc?.source === 'gps') {
            onUpdate?.(loc);
          }
        });
      },
      () => { /* map location watch errors are non-fatal */ },
      {
        enableHighAccuracy: true,
        maximumAge: 10_000,
        timeout: 20_000,
      },
    );
  }

  stopMapLocationUpdates(): void {
    this.stopWatching();
  }

  /* ── GPS watch (continuous updates after initial fix) ───────────────── */

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

  private shouldAcceptMapWatchUpdate(lat: number, lng: number): boolean {
    const now = Date.now();
    if (now - this.lastMapWatchUpdateTime < MAP_WATCH_MIN_INTERVAL_MS) return false;

    const dist = this.haversineMetres(this.lastMapWatchLat, this.lastMapWatchLng, lat, lng);
    if (this.lastMapWatchUpdateTime > 0 && dist < MAP_WATCH_MIN_DISTANCE_METRES) return false;

    this.lastMapWatchLat = lat;
    this.lastMapWatchLng = lng;
    this.lastMapWatchUpdateTime = now;
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

  private reverseGeocode(lat: number, lng: number, source: 'gps' | 'approximate' | 'manual'): Promise<ResolvedLocation | null> {
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
            cachedAt: Date.now(),
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
              cachedAt: Date.now(),
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

  private fetchExactLocation(options: PositionOptions): Promise<ResolvedLocation | null> {
    if (typeof navigator === 'undefined' || !('geolocation' in navigator)) {
      return Promise.resolve(null);
    }

    return new Promise((resolve) => {
      navigator.geolocation.getCurrentPosition(
        (pos) => {
          this.zone.run(() => {
            const { latitude, longitude } = pos.coords;
            resolve(this.applyGpsCoordinates(latitude, longitude));
          });
        },
        () => resolve(null),
        options,
      );
    });
  }

  private applyGpsCoordinates(latitude: number, longitude: number): ResolvedLocation | null {
    if (!this.hasUsableCoordinates(latitude, longitude)) return null;

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
      cachedAt: Date.now(),
    };

    this._location.set(loc);
    this._loading.set(false);
    this._error.set(null);
    this.saveToStorage(loc);

    if (this.shouldReverseGeocode(latitude, longitude)) {
      void this.reverseGeocode(latitude, longitude, 'gps');
    }

    return loc;
  }

  /* ── Approximate fallback ──────────────────────────────────────────── */

  /* ── Local persistence ─────────────────────────────────────────────── */

  private saveToStorage(loc: ResolvedLocation): void {
    try {
      const payload: StoredLocationPayload = {
        schemaVersion: LOCATION_SESSION_SCHEMA_VERSION,
        location: {
          ...loc,
          cachedAt: loc.cachedAt ?? Date.now(),
        },
      };
      sessionStorage.setItem(LOCATION_KEY, JSON.stringify(payload));
      localStorage.removeItem(LOCATION_KEY);
    } catch { /* quota exceeded - ignore */ }
  }

  private restoreFromStorage(): ResolvedLocation | null {
    try {
      const raw = sessionStorage.getItem(LOCATION_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw);
      const candidate = this.extractStoredLocationCandidate(parsed);
      const loc = this.normalizeStoredLocation(candidate);
      if (!loc) {
        sessionStorage.removeItem(LOCATION_KEY);
        return null;
      }

      if ((loc.source === 'gps' || loc.source === 'approximate') && !this.isCacheFresh(loc)) {
        sessionStorage.removeItem(LOCATION_KEY);
        return null;
      }

      this.saveToStorage(loc);
      return loc;
    } catch {
      try { sessionStorage.removeItem(LOCATION_KEY); } catch { /* storage unavailable */ }
    }
    return null;
  }

  private readEntryCompleteFromStorage(): boolean {
    try {
      return localStorage.getItem(ENTRY_COMPLETE_KEY) === 'true';
    } catch {
      return false;
    }
  }

  private readApproximateDisclosureFromStorage(): ApproximateDisclosureState {
    try {
      const value = localStorage.getItem(APPROXIMATE_DISCLOSURE_KEY);
      return value === 'accepted' || value === 'declined' ? value : null;
    } catch {
      return null;
    }
  }

  private setApproximateDisclosureState(state: ApproximateDisclosureState): void {
    this._approximateDisclosureState.set(state);
    try {
      if (state) {
        localStorage.setItem(APPROXIMATE_DISCLOSURE_KEY, state);
      } else {
        localStorage.removeItem(APPROXIMATE_DISCLOSURE_KEY);
      }
    } catch {
      // Storage may be unavailable; in-memory state still gates this session.
    }
  }

  private ensureApproximateDisclosureAccepted(): Promise<boolean> {
    const state = this._approximateDisclosureState();
    if (state === 'accepted') return Promise.resolve(true);
    if (state === 'declined') return Promise.resolve(false);

    if (!this.approximateDisclosurePromise) {
      this._approximateDisclosureVisible.set(true);
      this.approximateDisclosurePromise = new Promise<boolean>((resolve) => {
        this.resolveApproximateDisclosure = resolve;
      }).finally(() => {
        this.approximateDisclosurePromise = null;
        this.resolveApproximateDisclosure = null;
      });
    }

    return this.approximateDisclosurePromise;
  }

  private discardUnsafeLocalStorageLocation(): void {
    try {
      if (localStorage.getItem(LOCATION_KEY) !== null) {
        localStorage.removeItem(LOCATION_KEY);
      }
    } catch {
      // Storage may be unavailable; session restore will simply be skipped.
    }
  }

  private extractStoredLocationCandidate(parsed: unknown): unknown {
    if (this.isRecord(parsed) &&
      parsed['schemaVersion'] === LOCATION_SESSION_SCHEMA_VERSION &&
      this.isRecord(parsed['location'])) {
      return parsed['location'];
    }

    return parsed;
  }

  private normalizeStoredLocation(candidate: unknown): ResolvedLocation | null {
    if (!this.isRecord(candidate)) return null;

    const latitude = candidate['latitude'];
    const longitude = candidate['longitude'];
    if (!this.hasUsableCoordinates(latitude, longitude)) return null;

    const source = this.normalizeLocationSource(candidate['source']);
    if (!source) return null;

    return {
      latitude: latitude as number,
      longitude: longitude as number,
      city: this.stringValue(candidate['city']),
      region: this.stringValue(candidate['region']),
      country: this.stringValue(candidate['country']),
      displayLabel: this.stringValue(candidate['displayLabel']) || 'Explore',
      source,
      cachedAt: typeof candidate['cachedAt'] === 'number' ? candidate['cachedAt'] : Date.now(),
    };
  }

  private normalizeLocationSource(value: unknown): LocationSource | null {
    if (value === 'ip') return 'approximate';
    if (value === 'gps' || value === 'approximate' || value === 'manual' || value === 'default' || value === 'unknown') {
      return value;
    }
    return null;
  }

  private stringValue(value: unknown): string {
    return typeof value === 'string' ? value : '';
  }

  private isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
  }

  private async fetchApproximateLocationForRouting(): Promise<ResolvedLocation | null> {
    const current = this._location();
    if (this.isRoutingOrigin(current) && this.isCacheFresh(current)) {
      return current;
    }

    const approximate = this._approximateLocation();
    if (this.isRoutingOrigin(approximate) && this.isCacheFresh(approximate)) {
      return approximate;
    }

    if (this.approximateInFlight) return this.approximateInFlight;

    const accepted = await this.ensureApproximateDisclosureAccepted();
    if (!accepted) {
      this._loading.set(false);
      return null;
    }

    this._loading.set(true);
    const request = new Promise<ResolvedLocation | null>((resolve) => {
      this.http
        .get<ApproximateLocationResponse>('/api/locations/approximate')
        .subscribe({
          next: (res) => {
            const loc = this.mapApproximateLocation(res);
            if (!this.isRoutingOrigin(loc)) {
              this._loading.set(false);
              this._error.set('Unable to determine location');
              resolve(null);
              return;
            }

            this._approximateLocation.set(loc);
            const active = this._location();
            if (!active || active.source === 'approximate' || !this.isCacheFresh(active)) {
              this._location.set(loc);
              this._loading.set(false);
              this._error.set(null);
              this.saveToStorage(loc);
            }
            resolve(loc);
          },
          error: () => {
            this._loading.set(false);
            this._error.set('Unable to determine location');
            resolve(null);
          },
        });
    }).finally(() => {
      this.approximateInFlight = null;
    });
    this.approximateInFlight = request;

    return request;
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

  private isRoutingOrigin(loc: ResolvedLocation | null): loc is ResolvedLocation {
    return loc !== null &&
      (loc.source === 'gps' || loc.source === 'approximate') &&
      this.hasUsableCoordinates(loc.latitude, loc.longitude);
  }

  private isCacheFresh(loc: ResolvedLocation | null): boolean {
    if (!loc) return false;
    if (loc.source === 'manual') return true;

    const cachedAt = typeof loc.cachedAt === 'number' ? loc.cachedAt : Date.now();
    return Date.now() - cachedAt < LOCATION_CACHE_TTL_MS;
  }

  private hasUsableCoordinates(latitude: unknown, longitude: unknown): boolean {
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
      source: 'approximate',
      cachedAt: Date.now(),
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

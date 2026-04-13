import { Injectable, signal, computed, OnDestroy, NgZone, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ResolvedLocation } from '../models/location.model';

/** Minimum metres between two positions before we re-geocode. */
const MIN_DISTANCE_METRES = 500;
/** Minimum milliseconds between reverse-geocode calls. */
const MIN_GEOCODE_INTERVAL_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class LocationService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly zone = inject(NgZone);

  private watchId: number | null = null;
  private lastGeocodedLat = 0;
  private lastGeocodedLng = 0;
  private lastGeocodeTime = 0;

  /* ── Public reactive state ─────────────────────────────────────────── */

  private readonly _location = signal<ResolvedLocation | null>(null);
  private readonly _loading = signal(true);
  private readonly _error = signal<string | null>(null);

  readonly location = computed(() => this._location());
  readonly loading = computed(() => this._loading());
  readonly error = computed(() => this._error());

  readonly displayLabel = computed(() => {
    const loc = this._location();
    if (loc) return loc.displayLabel;
    if (this._loading()) return 'Locating...';
    return 'Explore';
  });

  readonly displayCity = computed(() => this._location()?.city ?? this.displayLabel());
  readonly displayCountry = computed(() => this._location()?.country ?? '');

  /* ── Lifecycle ─────────────────────────────────────────────────────── */

  constructor() {
    this.init();
  }

  ngOnDestroy(): void {
    this.stopWatching();
  }

  /* ── Init: try GPS first, fall back to IP ──────────────────────────── */

  private init(): void {
    if (!('geolocation' in navigator)) {
      this.fallbackToIp();
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (pos) => this.handlePosition(pos, true),
      (err) => {
        if (err.code === err.PERMISSION_DENIED) {
          this.fallbackToIp();
        } else {
          this.fallbackToIp();
        }
      },
      { enableHighAccuracy: false, timeout: 10_000 },
    );
  }

  /* ── GPS handling ──────────────────────────────────────────────────── */

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

  private reverseGeocode(lat: number, lng: number, source: 'gps' | 'ip'): void {
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

        this._location.set({
          latitude: lat,
          longitude: lng,
          city,
          region,
          country,
          displayLabel,
          source,
        });
        this._loading.set(false);
        this._error.set(null);
      },
      error: () => {
        // Geocode failed but we still have coords
        this._location.set({
          latitude: lat,
          longitude: lng,
          city: '',
          region: '',
          country: '',
          displayLabel: 'Explore',
          source,
        });
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

  private fallbackToIp(): void {
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

            this._location.set({
              latitude: res.lat ?? 0,
              longitude: res.lon ?? 0,
              city,
              region,
              country,
              displayLabel,
              source: 'ip',
            });
          }
          this._loading.set(false);
        },
        error: () => {
          this._loading.set(false);
          this._error.set('Unable to determine location');
        },
      });
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

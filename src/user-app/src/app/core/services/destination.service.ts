import { Injectable, computed, signal } from '@angular/core';

const STORAGE_KEY = 'destination';

export type DestinationBrowseMode = 'location' | 'destination';

interface StoredDestination {
  id: number;
  name: string;
  latitude?: number;
  longitude?: number;
  mode?: DestinationBrowseMode;
}

/**
 * Shared state for the destination (city/locality) the user is currently browsing.
 *
 * Resolved on the /api/explore call: the backend returns the nearest destination
 * for the given lat/lng, and ExploreService writes the { id, name } here so that
 * downstream listing services can scope their queries to the same destination.
 *
 * Persisted in sessionStorage so it survives a soft refresh but re-resolves on
 * a fresh session. Not tied to auth state.
 */
@Injectable({ providedIn: 'root' })
export class DestinationService {
  private readonly _id = signal<number | null>(null);
  private readonly _name = signal<string>('');
  private readonly _latitude = signal<number | null>(null);
  private readonly _longitude = signal<number | null>(null);
  private readonly _mode = signal<DestinationBrowseMode>('location');

  readonly id = computed(() => this._id());
  readonly name = computed(() => this._name());
  readonly latitude = computed(() => this._latitude());
  readonly longitude = computed(() => this._longitude());
  readonly mode = computed(() => this._mode());
  readonly isDestinationMode = computed(() => this._mode() === 'destination');

  constructor() {
    const stored = this.readFromStorage();
    if (stored) {
      this._id.set(stored.id);
      this._name.set(stored.name);
      this._latitude.set(this.hasUsableCoordinates(stored.latitude, stored.longitude) ? stored.latitude! : null);
      this._longitude.set(this.hasUsableCoordinates(stored.latitude, stored.longitude) ? stored.longitude! : null);
      this._mode.set(stored.mode === 'destination' ? 'destination' : 'location');
    }
  }

  set(d: { id: number; name: string; latitude?: number; longitude?: number }): void {
    if (!d || d.id <= 0) return;
    this._id.set(d.id);
    this._name.set(d.name ?? '');
    this._latitude.set(this.hasUsableCoordinates(d.latitude, d.longitude) ? d.latitude! : this._latitude());
    this._longitude.set(this.hasUsableCoordinates(d.latitude, d.longitude) ? d.longitude! : this._longitude());
    this.writeCurrentToStorage();
  }

  setDestinationOverride(d: { id: number; name: string; latitude?: number; longitude?: number }): void {
    if (!d || d.id <= 0) return;
    this._mode.set('destination');
    this._id.set(d.id);
    this._name.set(d.name ?? '');
    this._latitude.set(this.hasUsableCoordinates(d.latitude, d.longitude) ? d.latitude! : null);
    this._longitude.set(this.hasUsableCoordinates(d.latitude, d.longitude) ? d.longitude! : null);
    this.writeCurrentToStorage();
  }

  useLocationMode(): void {
    this._mode.set('location');
    this._id.set(null);
    this._name.set('');
    this._latitude.set(null);
    this._longitude.set(null);
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // storage may be unavailable (e.g. private mode); ignore
    }
  }

  clear(): void {
    this._id.set(null);
    this._name.set('');
    this._latitude.set(null);
    this._longitude.set(null);
    this._mode.set('location');
    try {
      sessionStorage.removeItem(STORAGE_KEY);
    } catch {
      // storage may be unavailable (e.g. private mode); ignore
    }
  }

  private readFromStorage(): StoredDestination | null {
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as StoredDestination;
      if (typeof parsed?.id !== 'number' || parsed.id <= 0) return null;
      return {
        id: parsed.id,
        name: typeof parsed.name === 'string' ? parsed.name : '',
        latitude: typeof parsed.latitude === 'number' ? parsed.latitude : undefined,
        longitude: typeof parsed.longitude === 'number' ? parsed.longitude : undefined,
        mode: parsed.mode === 'destination' ? 'destination' : 'location',
      };
    } catch {
      return null;
    }
  }

  private writeToStorage(d: StoredDestination): void {
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(d));
    } catch {
      // storage may be unavailable; silent
    }
  }

  private writeCurrentToStorage(): void {
    const id = this._id();
    if (id == null || id <= 0) return;

    this.writeToStorage({
      id,
      name: this._name(),
      latitude: this._latitude() ?? undefined,
      longitude: this._longitude() ?? undefined,
      mode: this._mode(),
    });
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
}

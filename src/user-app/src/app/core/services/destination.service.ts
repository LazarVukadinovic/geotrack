import { Injectable, computed, signal } from '@angular/core';

const STORAGE_KEY = 'destination';

interface StoredDestination {
  id: number;
  name: string;
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

  readonly id = computed(() => this._id());
  readonly name = computed(() => this._name());

  constructor() {
    const stored = this.readFromStorage();
    if (stored) {
      this._id.set(stored.id);
      this._name.set(stored.name);
    }
  }

  set(d: { id: number; name: string }): void {
    if (!d || d.id <= 0) return;
    this._id.set(d.id);
    this._name.set(d.name ?? '');
    this.writeToStorage({ id: d.id, name: d.name ?? '' });
  }

  clear(): void {
    this._id.set(null);
    this._name.set('');
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
      return { id: parsed.id, name: typeof parsed.name === 'string' ? parsed.name : '' };
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
}

import { Injectable, signal } from '@angular/core';

export interface LocalPlacesScrollState {
  scrollTop: number;
}

/**
 * Persists Local Places & Landmarks page scroll state across route navigation.
 * State is saved synchronously at tile-click time before router.navigate() fires.
 */
@Injectable({ providedIn: 'root' })
export class LocalPlacesScrollStateService {
  private readonly _state = signal<LocalPlacesScrollState | null>(null);

  save(scrollTop: number): void {
    this._state.set({ scrollTop });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): LocalPlacesScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}

import { Injectable, signal } from '@angular/core';

export interface EventsListingScrollState {
  scrollTop: number;
  pillsLeft: number;
  clickedItemId: string | null;
}

/**
 * Persists Events listing page scroll state across route navigation.
 * State is saved synchronously at click time before router.navigate() fires —
 * the same pattern used by FoodMusicPageComponent, CultureHistoryPageComponent, and NaturePageComponent.
 */
@Injectable({ providedIn: 'root' })
export class EventsListingScrollStateService {
  private readonly _state = signal<EventsListingScrollState | null>(null);

  save(scrollTop: number, pillsLeft: number, clickedItemId: string | null = null): void {
    this._state.set({ scrollTop, pillsLeft, clickedItemId });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): EventsListingScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}

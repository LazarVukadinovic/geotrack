import { Injectable, signal } from '@angular/core';

export interface HotelsScrollState {
  scrollTop: number;
  pillsLeft: number;
  clickedCardId: string | null;
}

/**
 * Persists Hotels listing page scroll state across route navigation.
 * State is saved synchronously at click time (not in ngOnDestroy) to guarantee
 * the correct scroll position is captured before any navigation or lifecycle hooks run.
 */
@Injectable({ providedIn: 'root' })
export class HotelsScrollStateService {
  private readonly _state = signal<HotelsScrollState | null>(null);

  save(scrollTop: number, pillsLeft: number, clickedCardId: string | null = null): void {
    this._state.set({ scrollTop, pillsLeft, clickedCardId });
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): HotelsScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}

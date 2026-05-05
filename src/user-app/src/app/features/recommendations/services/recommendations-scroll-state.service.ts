import { Injectable, signal } from '@angular/core';

export interface RecommendationsScrollState {
  scrollTop: number;
  filtersLeft: number;
  clickedItemId: string | null;
}

@Injectable({ providedIn: 'root' })
export class RecommendationsScrollStateService {
  private readonly _state = signal<RecommendationsScrollState | null>(null);

  save(scrollTop: number, filtersLeft: number, clickedItemId: string | null = null): void {
    this._state.set({ scrollTop, filtersLeft, clickedItemId });
  }

  pop(): RecommendationsScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }
}

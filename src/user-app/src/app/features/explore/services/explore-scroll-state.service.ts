import { Injectable, signal } from '@angular/core';

interface ClickedItem {
  sliderId: string;
  itemId: string;
}

interface ExploreScrollState {
  scrollTop: number;
  sliders: Record<string, number>;
  clickedItem: ClickedItem | null;
}

@Injectable({ providedIn: 'root' })
export class ExploreScrollStateService {
  private readonly _state = signal<ExploreScrollState | null>(null);
  private _pendingClick: ClickedItem | null = null;

  /** Call immediately before router.navigate() when a card is tapped. */
  markClickedItem(sliderId: string, itemId: string): void {
    this._pendingClick = { sliderId, itemId };
  }

  save(scrollTop: number, sliders: Record<string, number>): void {
    this._state.set({
      scrollTop,
      sliders,
      clickedItem: this._pendingClick,
    });
    this._pendingClick = null;
  }

  /** Returns and clears the saved state. Returns null if nothing was saved. */
  pop(): ExploreScrollState | null {
    const state = this._state();
    this._state.set(null);
    return state;
  }

}

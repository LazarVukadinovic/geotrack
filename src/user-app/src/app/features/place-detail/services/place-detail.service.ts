import { Injectable, signal } from '@angular/core';
import type { PlaceDetail } from '../../../core/models/place.model';
import { MOCK_PLACE_DETAIL_MAP } from '../data/mock-place-detail.data';

@Injectable({ providedIn: 'root' })
export class PlaceDetailService {

  /** Saved-state overrides keyed by place id — isolates mutations from static mock data. */
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  /**
   * Returns the full detail page data for a given place id.
   * Reading `_savedOverrides()` here means any computed that calls this
   * method will automatically re-evaluate when saved state changes.
   *
   * Replace mock lookup with an HTTP call when a real API is available.
   */
  getPlaceDetailPageData(id: string): PlaceDetail | undefined {
    const overrides = this._savedOverrides();
    const base = MOCK_PLACE_DETAIL_MAP[id];
    if (!base) return undefined;
    const isSaved = overrides[id] !== undefined ? overrides[id] : base.isSaved;
    return { ...base, isSaved };
  }

  toggleSaved(id: string): void {
    this._savedOverrides.update((m) => {
      const base = MOCK_PLACE_DETAIL_MAP[id];
      const current = m[id] !== undefined ? m[id] : (base?.isSaved ?? false);
      return { ...m, [id]: !current };
    });
  }
}

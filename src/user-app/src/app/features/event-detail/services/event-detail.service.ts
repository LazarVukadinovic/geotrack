import { Injectable, signal } from '@angular/core';
import type { EventDetail } from '../models/event-detail.model';
import { MOCK_EVENT_DETAIL_MAP } from '../data/mock-event-detail.data';

@Injectable({ providedIn: 'root' })
export class EventDetailService {
  private readonly _savedOverrides = signal<Record<string, boolean>>({});

  /** Returns the event for the given id, merging any transient saved-state overrides. */
  getEventDetailPageData(id: string): EventDetail | null {
    const base = MOCK_EVENT_DETAIL_MAP[id];
    if (!base) return null;
    const overrides = this._savedOverrides();
    return {
      ...base,
      isSaved: overrides[id] ?? base.isSaved,
    };
  }

  toggleSaved(id: string): void {
    const current = this.getEventDetailPageData(id);
    if (!current) return;
    this._savedOverrides.update((m) => ({ ...m, [id]: !current.isSaved }));
  }
}

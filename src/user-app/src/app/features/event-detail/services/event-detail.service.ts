import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import type { EventDetail } from '../models/event-detail.model';
import { MOCK_EVENT_DETAIL_MAP } from '../data/mock-event-detail.data';

interface ApiEventDetail {
  id: number;
  title: string;
  location?: string;
  heroImage?: string;
  tags: { label: string }[];
  distance?: string;
  about?: string;
  info: { date?: string; timeRange?: string; venue?: string; ticket?: string; duration?: string; hostedBy?: string };
  venueInfo: { description?: string; arrivalTips?: string; accessibility?: string };
  similar: { id: number; title: string; subtitle?: string; image?: string; isSaved: boolean }[];
  isSaved: boolean;
}

@Injectable({ providedIn: 'root' })
export class EventDetailService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _cache = signal<Record<string, EventDetail>>({});

  getEventDetailPageData(id: string): EventDetail | null {
    const overrides = this._savedOverrides();
    const cached = this._cache()[id];
    if (cached) {
      return { ...cached, isSaved: overrides[id] ?? cached.isSaved };
    }
    const base = MOCK_EVENT_DETAIL_MAP[id];
    if (!base) return null;
    return { ...base, isSaved: overrides[id] ?? base.isSaved };
  }

  loadEventDetail(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.get<ApiEventDetail>(`/api/events/${numId}`).subscribe({
      next: (dto) => {
        const detail: EventDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          heroImage: dto.heroImage ?? '',
          tags: dto.tags,
          distance: dto.distance ?? '',
          about: dto.about ?? '',
          info: {
            date: dto.info.date ?? '',
            timeRange: dto.info.timeRange ?? '',
            venue: dto.info.venue ?? '',
            ticket: dto.info.ticket ?? '',
            duration: dto.info.duration ?? '',
            hostedBy: dto.info.hostedBy ?? '',
          },
          venueInfo: {
            description: dto.venueInfo.description ?? '',
            arrivalTips: dto.venueInfo.arrivalTips ?? '',
            accessibility: dto.venueInfo.accessibility ?? '',
          },
          similar: dto.similar.map(s => ({
            id: String(s.id),
            title: s.title,
            subtitle: s.subtitle ?? '',
            image: s.image ?? '',
            isSaved: s.isSaved,
          })),
          isSaved: dto.isSaved,
        };
        this._cache.update(c => ({ ...c, [String(dto.id)]: detail }));
      },
    });
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;

    const current = this.getEventDetailPageData(id);
    if (!current) return;

    const flip = () => {
      const snap = this.getEventDetailPageData(id);
      if (!snap) return;
      this._savedOverrides.update((m) => ({ ...m, [id]: !snap.isSaved }));
    };
    flip();

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.post('/api/favorites/toggle', { entityType: 'event', entityId: numId }).subscribe({
      error: () => flip(),
    });
  }
}

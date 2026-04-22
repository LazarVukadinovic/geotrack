import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import type { EventDetail } from '../models/event-detail.model';
import { MOCK_EVENT_DETAIL_MAP } from '../data/mock-event-detail.data';

interface ApiEventDetail {
  id: number;
  title: string;
  location?: string;
  latitude?: number | null;
  longitude?: number | null;
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
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly _cache = signal<Record<string, EventDetail>>({});
  private readonly lastRequestKeyById = new Map<string, string>();

  getEventDetailPageData(id: string): EventDetail | null {
    const cached = this._cache()[id];
    if (cached) {
      return {
        ...cached,
        isSaved: this.favoriteState.resolve('event', id, cached.isSaved),
      };
    }
    const base = MOCK_EVENT_DETAIL_MAP[id];
    if (!base) return null;
    return {
      ...base,
      isSaved: this.favoriteState.resolve('event', id, base.isSaved),
    };
  }

  loadEventDetail(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const locationQuery = this.buildLocationQuery();
    const requestKey = `${numId}${locationQuery}`;
    if (this.lastRequestKeyById.get(id) === requestKey) return;
    this.lastRequestKeyById.set(id, requestKey);

    this.http.get<ApiEventDetail>(`/api/events/${numId}${locationQuery}`).subscribe({
      next: (dto) => {
        if (this.lastRequestKeyById.get(id) !== requestKey) {
          return;
        }

        const detail: EventDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          latitude: dto.latitude ?? null,
          longitude: dto.longitude ?? null,
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
        this.lastRequestKeyById.delete(id);
      },
      error: () => {
        if (this.lastRequestKeyById.get(id) === requestKey) {
          this.lastRequestKeyById.delete(id);
        }
      },
    });
  }

  private buildLocationQuery(): string {
    const loc = this.locationSvc.location();
    if (!loc) return '';
    return `?latitude=${loc.latitude}&longitude=${loc.longitude}`;
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;

    const current = this.getEventDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.favoriteState.set('event', id, !current.isSaved);
    this.http.post('/api/favorites/toggle', { entityType: 'event', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('event', id, current.isSaved),
    });
  }
}

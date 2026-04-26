import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import type { PlaceDetail } from '../../../core/models/place.model';

export type DetailLoadState = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error';

interface ApiPlaceDetail {
  id: number;
  title: string;
  location?: string;
  latitude?: number | null;
  longitude?: number | null;
  heroImage?: string;
  tags: { label: string }[];
  rating?: number;
  reviewCount?: string;
  distance?: string;
  openStatus?: string;
  about?: string;
  reviews: { id: number; authorName: string; avatarUrl?: string; rating: number; timeAgo?: string; body: string; helpfulCount: number }[];
  similar: { id: number; title: string; image?: string; rating?: number; distance?: string; isSaved: boolean }[];
  isSaved: boolean;
  contentType: string;
  category?: string;
}

@Injectable({ providedIn: 'root' })
export class PlaceDetailService {

  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly _cache = signal<Record<string, PlaceDetail>>({});
  private readonly _loadState = signal<Record<string, DetailLoadState>>({});
  private readonly lastRequestKeyById = new Map<string, string>();

  getPlaceDetailPageData(id: string): PlaceDetail | undefined {
    const cached = this._cache()[id];
    if (cached) {
      return {
        ...cached,
        isSaved: this.favoriteState.resolve('place', id, cached.isSaved),
      };
    }

    return undefined;
  }

  getLoadState(id: string): DetailLoadState {
    return this._loadState()[id] ?? 'idle';
  }

  loadPlaceDetail(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) {
      this.setLoadState(id, 'notFound');
      return;
    }
    const locationQuery = this.buildLocationQuery();
    const requestKey = `${numId}${locationQuery}`;
    if (this.lastRequestKeyById.get(id) === requestKey) return;
    this.lastRequestKeyById.set(id, requestKey);
    this.setLoadState(id, 'loading');

    this.http.get<ApiPlaceDetail>(`/api/places/${numId}${locationQuery}`).subscribe({
      next: (dto) => {
        if (this.lastRequestKeyById.get(id) !== requestKey) {
          return;
        }

        const detail: PlaceDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          latitude: dto.latitude ?? null,
          longitude: dto.longitude ?? null,
          heroImage: dto.heroImage ?? '',
          tags: dto.tags,
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          distance: dto.distance ?? '',
          openStatus: dto.openStatus ?? '',
          about: dto.about ?? '',
          reviews: dto.reviews.map(r => ({
            id: String(r.id),
            authorName: r.authorName,
            avatarUrl: r.avatarUrl ?? '',
            rating: r.rating,
            timeAgo: r.timeAgo ?? '',
            body: r.body,
            helpfulCount: r.helpfulCount,
          })),
          similar: dto.similar.map(s => ({
            id: String(s.id),
            title: s.title,
            image: s.image ?? '',
            rating: s.rating,
            distance: s.distance ?? '',
            isSaved: s.isSaved,
          })),
          isSaved: dto.isSaved,
          contentType: (dto.contentType ?? 'place') as PlaceDetail['contentType'],
          category: (dto.category ?? 'attraction') as PlaceDetail['category'],
        };
        this._cache.update(c => ({ ...c, [String(dto.id)]: detail }));
        this.setLoadState(id, 'loaded');
        this.lastRequestKeyById.delete(id);
      },
      error: (err) => {
        if (this.lastRequestKeyById.get(id) === requestKey) {
          this.setLoadState(id, err?.status === 404 ? 'notFound' : 'error');
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

  private setLoadState(id: string, state: DetailLoadState): void {
    this._loadState.update(current => ({ ...current, [id]: state }));
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;
    const current = this.getPlaceDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.favoriteState.set('place', id, !current.isSaved);
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current.isSaved),
    });
  }
}

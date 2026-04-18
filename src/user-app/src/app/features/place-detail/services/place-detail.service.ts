import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import type { PlaceDetail } from '../../../core/models/place.model';
import { MOCK_PLACE_DETAIL_MAP } from '../data/mock-place-detail.data';

interface ApiPlaceDetail {
  id: number;
  title: string;
  location?: string;
  heroImage?: string;
  tags: { label: string }[];
  rating: number;
  reviewCount: string;
  distance?: string;
  openStatus?: string;
  about?: string;
  reviews: { id: number; authorName: string; avatarUrl?: string; rating: number; timeAgo?: string; body: string; helpfulCount: number }[];
  similar: { id: number; title: string; image?: string; rating: number; distance?: string; isSaved: boolean }[];
  isSaved: boolean;
  contentType: string;
  category?: string;
}

@Injectable({ providedIn: 'root' })
export class PlaceDetailService {

  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _cache = signal<Record<string, PlaceDetail>>({});

  getPlaceDetailPageData(id: string): PlaceDetail | undefined {
    const overrides = this._savedOverrides();
    const cached = this._cache()[id];
    if (cached) {
      const isSaved = overrides[id] !== undefined ? overrides[id] : cached.isSaved;
      return { ...cached, isSaved };
    }
    // Fallback to mock while loading
    const base = MOCK_PLACE_DETAIL_MAP[id];
    if (!base) return undefined;
    const isSaved = overrides[id] !== undefined ? overrides[id] : base.isSaved;
    return { ...base, isSaved };
  }

  loadPlaceDetail(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.get<ApiPlaceDetail>(`/api/places/${numId}`).subscribe({
      next: (dto) => {
        const detail: PlaceDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
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
      },
    });
  }

  toggleSaved(id: string): void {
    if (!this.auth.isLoggedIn()) return;

    const flip = () =>
      this._savedOverrides.update((m) => {
        const cached = this._cache()[id];
        const base = cached ?? MOCK_PLACE_DETAIL_MAP[id];
        const current = m[id] !== undefined ? m[id] : (base?.isSaved ?? false);
        return { ...m, [id]: !current };
      });
    flip();

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => flip(),
    });
  }
}

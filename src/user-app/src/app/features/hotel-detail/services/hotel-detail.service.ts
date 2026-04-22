import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import type { HotelDetail } from '../models/hotel-detail.model';
import { MOCK_HOTEL_DETAIL_MAP } from '../data/mock-hotel-detail.data';

interface ApiHotelDetail {
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
  pricePerNight: number;
  about?: string;
  bookingPartners: { id: number; name: string; variant: string; url?: string | null }[];
  amenities: { id: number; label: string; icon: string }[];
  reviews: { id: number; authorName: string; avatarUrl?: string; rating: number; timeAgo?: string; body: string; helpfulCount: number }[];
  similar: { id: number; title: string; image?: string; rating?: number; distance?: string; isSaved: boolean }[];
  isSaved: boolean;
}

@Injectable({ providedIn: 'root' })
export class HotelDetailService {

  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly locationSvc = inject(LocationService);
  private readonly _cache = signal<Record<string, HotelDetail>>({});
  private readonly lastRequestKeyById = new Map<string, string>();

  getHotelDetailPageData(id: string): HotelDetail | undefined {
    const cached = this._cache()[id];
    if (cached) {
      return {
        ...cached,
        isSaved: this.favoriteState.resolve('place', id, cached.isSaved),
      };
    }

    const base = MOCK_HOTEL_DETAIL_MAP[id];
    if (!base) return undefined;

    return {
      ...base,
      isSaved: this.favoriteState.resolve('place', id, base.isSaved),
    };
  }

  loadHotelDetail(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const locationQuery = this.buildLocationQuery();
    const requestKey = `${numId}${locationQuery}`;
    if (this.lastRequestKeyById.get(id) === requestKey) return;
    this.lastRequestKeyById.set(id, requestKey);

    this.http.get<ApiHotelDetail>(`/api/places/hotels/${numId}${locationQuery}`).subscribe({
      next: (dto) => {
        if (this.lastRequestKeyById.get(id) !== requestKey) {
          return;
        }

        const detail: HotelDetail = {
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
          pricePerNight: dto.pricePerNight,
          about: dto.about ?? '',
          bookingPartners: dto.bookingPartners.map(b => ({
            id: String(b.id),
            name: b.name,
            variant: b.variant as HotelDetail['bookingPartners'][0]['variant'],
            url: b.url ?? null,
          })),
          amenities: dto.amenities.map(a => ({
            id: String(a.id),
            label: a.label,
            icon: a.icon as HotelDetail['amenities'][0]['icon'],
          })),
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
    const current = this.getHotelDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.favoriteState.set('place', id, !current.isSaved);
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current.isSaved),
    });
  }
}

import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../../core/services/auth.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import type { HotelDetail } from '../models/hotel-detail.model';
import type { DetailLoadState } from '../../place-detail/services/place-detail.service';

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
  pricePerNight?: number | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
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
  private readonly analytics = inject(AnalyticsService);
  private readonly _cache = signal<Record<string, HotelDetail>>({});
  private readonly _loadState = signal<Record<string, DetailLoadState>>({});
  private readonly lastRequestKeyById = new Map<string, string>();

  getHotelDetailPageData(id: string): HotelDetail | undefined {
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

  loadHotelDetail(id: string): void {
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
          pricePerNight: dto.pricePerNight ?? null,
          about: dto.about ?? '',
          contactEmail: dto.contactEmail ?? null,
          phoneNumber: dto.phoneNumber ?? null,
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
        this.setLoadState(id, 'loaded');
        this.lastRequestKeyById.delete(id);
        this.analytics.track('detail_view', { entityType: 'hotel', entityId: dto.id });
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
    const current = this.getHotelDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    const next = !current.isSaved;
    this.favoriteState.set('place', id, next);
    this.analytics.track(next ? 'favorite_added' : 'favorite_removed', { entityType: 'hotel', entityId: numId });
    this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe({
      error: () => this.favoriteState.set('place', id, current.isSaved),
    });
  }
}

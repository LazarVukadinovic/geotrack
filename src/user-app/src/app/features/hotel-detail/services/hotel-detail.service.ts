import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import type { HotelDetail } from '../models/hotel-detail.model';
import { MOCK_HOTEL_DETAIL_MAP } from '../data/mock-hotel-detail.data';

interface ApiHotelDetail {
  id: number;
  title: string;
  location?: string;
  heroImage?: string;
  tags: { label: string }[];
  rating: number;
  reviewCount: string;
  distance?: string;
  pricePerNight: number;
  about?: string;
  bookingPartners: { id: number; name: string; variant: string }[];
  amenities: { id: number; label: string; icon: string }[];
  reviews: { id: number; authorName: string; avatarUrl?: string; rating: number; timeAgo?: string; body: string; helpfulCount: number }[];
  similar: { id: number; title: string; image?: string; rating: number; distance?: string; isSaved: boolean }[];
  isSaved: boolean;
}

@Injectable({ providedIn: 'root' })
export class HotelDetailService {

  private readonly http = inject(HttpClient);
  private readonly _savedOverrides = signal<Record<string, boolean>>({});
  private readonly _cache = signal<Record<string, HotelDetail>>({});

  getHotelDetailPageData(id: string): HotelDetail | undefined {
    const overrides = this._savedOverrides();
    const cached = this._cache()[id];
    if (cached) {
      const isSaved = overrides[id] !== undefined ? overrides[id] : cached.isSaved;
      return { ...cached, isSaved };
    }
    const base = MOCK_HOTEL_DETAIL_MAP[id];
    if (!base) return undefined;
    const isSaved = overrides[id] !== undefined ? overrides[id] : base.isSaved;
    return { ...base, isSaved };
  }

  loadHotelDetail(id: string): void {
    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.http.get<ApiHotelDetail>(`/api/places/hotels/${numId}`).subscribe({
      next: (dto) => {
        const detail: HotelDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
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
      },
    });
  }

  toggleSaved(id: string): void {
    this._savedOverrides.update((m) => {
      const cached = this._cache()[id];
      const base = cached ?? MOCK_HOTEL_DETAIL_MAP[id];
      const current = m[id] !== undefined ? m[id] : (base?.isSaved ?? false);
      return { ...m, [id]: !current };
    });
    const numId = parseInt(id, 10);
    if (!isNaN(numId)) {
      this.http.post('/api/favorites/toggle', { entityType: 'place', entityId: numId }).subscribe();
    }
  }
}

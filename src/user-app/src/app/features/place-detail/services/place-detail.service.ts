import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { LocationService } from '../../../core/services/location.service';
import { DestinationService } from '../../../core/services/destination.service';
import { getCoverImageUrl, getGalleryImages, normalizeEntityImages } from '../../../core/models/entity-image.model';
import type { ApiEntityImage } from '../../../core/models/entity-image.model';
import type { PlaceDetail, PlaceRelatedEvent } from '../../../core/models/place.model';

export type DetailLoadState = 'idle' | 'loading' | 'loaded' | 'notFound' | 'error';

interface ApiPlaceDetail {
  id: number;
  title: string;
  location?: string;
  locationId?: number | null;
  locationName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  heroImage?: string;
  images?: ApiEntityImage[] | null;
  tags: { label: string }[];
  rating?: number;
  reviewCount?: string;
  distance?: string;
  openStatus?: string;
  openingHours?: string | null;
  openingHoursSchedule?: { day: string; hours: string }[] | null;
  website?: string | null;
  about?: string;
  reviews: { id: number; authorUserId?: number; authorName: string; avatarUrl?: string; rating: number; timeAgo?: string; body: string; helpfulCount: number; reportedByCurrentUser?: boolean; isOwnReview?: boolean }[];
  relatedEvents?: { id: number; title: string; subtitle?: string | null; image?: string | null }[] | null;
  similar?: { id: number; title: string; image?: string; rating?: number; distance?: string; isSaved: boolean }[] | null;
  isSaved: boolean;
  isOwnable?: boolean;
  isOwnedByCurrentUser?: boolean;
  hasPendingOwnershipClaim?: boolean;
  contentType: string;
  category?: string;
}

@Injectable({ providedIn: 'root' })
export class PlaceDetailService {

  private readonly http = inject(HttpClient);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly analytics = inject(AnalyticsService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly locationSvc = inject(LocationService);
  private readonly destinationSvc = inject(DestinationService);
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

        const images = normalizeEntityImages(dto.images);
        const detail: PlaceDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          locationId: dto.locationId != null ? String(dto.locationId) : null,
          locationName: dto.locationName ?? dto.location ?? null,
          latitude: dto.latitude ?? null,
          longitude: dto.longitude ?? null,
          heroImage: getCoverImageUrl(images, dto.heroImage),
          images: getGalleryImages(images),
          tags: dto.tags,
          rating: dto.rating,
          reviewCount: dto.reviewCount,
          distance: dto.distance ?? '',
          openStatus: dto.openStatus ?? '',
          openingHours: dto.openingHours ?? null,
          openingHoursSchedule: dto.openingHoursSchedule ?? null,
          website: dto.website ?? null,
          about: dto.about ?? '',
          relatedEvents: (dto.relatedEvents ?? []).map((e): PlaceRelatedEvent => ({
            id: String(e.id),
            title: e.title,
            subtitle: e.subtitle ?? null,
            image: e.image ?? null,
          })),
          reviews: dto.reviews.map(r => ({
            id: String(r.id),
            authorUserId: r.authorUserId ?? 0,
            authorName: r.authorName,
            avatarUrl: r.avatarUrl ?? '',
            rating: r.rating,
            timeAgo: r.timeAgo ?? '',
            body: r.body,
            helpfulCount: r.helpfulCount,
            reportedByCurrentUser: r.reportedByCurrentUser ?? false,
            isOwnReview: r.isOwnReview ?? false,
          })),
          similar: (dto.similar ?? []).map(s => ({
            id: String(s.id),
            title: s.title,
            image: s.image ?? '',
            rating: s.rating,
            distance: s.distance ?? '',
            isSaved: s.isSaved,
          })),
          isSaved: dto.isSaved,
          isOwnable: dto.isOwnable ?? false,
          isOwnedByCurrentUser: dto.isOwnedByCurrentUser ?? false,
          hasPendingOwnershipClaim: dto.hasPendingOwnershipClaim ?? false,
          contentType: (dto.contentType ?? 'place') as PlaceDetail['contentType'],
          category: (dto.category ?? 'attraction') as PlaceDetail['category'],
        };
        this._cache.update(c => ({ ...c, [String(dto.id)]: detail }));
        this.setLoadState(id, 'loaded');
        this.lastRequestKeyById.delete(id);
        this.analytics.track('detail_view', { entityType: 'place', entityId: dto.id });
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
    if (this.destinationSvc.mode() === 'location') {
      const loc = this.locationSvc.location();
      if (loc) {
        return `?latitude=${loc.latitude}&longitude=${loc.longitude}&distanceOrigin=current-location`;
      }
    }
    return '?distanceOrigin=destination-centre';
  }

  private setLoadState(id: string, state: DetailLoadState): void {
    this._loadState.update(current => ({ ...current, [id]: state }));
  }

  toggleSaved(id: string): void {
    const current = this.getPlaceDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;
    this.saveToCollection.open({
      entityType: 'place',
      entityId: numId,
      title: current.title,
      imageUrl: current.heroImage,
      surface: 'place_detail',
      analyticsEntityType: 'place',
    });
  }
}

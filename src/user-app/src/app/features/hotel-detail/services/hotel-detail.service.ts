import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { AnalyticsService } from '../../../core/services/analytics.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { LocationService } from '../../../core/services/location.service';
import { DestinationService } from '../../../core/services/destination.service';
import { CollectionsService } from '../../profile/services/collections';
import { getCoverImageUrl, getGalleryImages, normalizeEntityImages } from '../../../core/models/entity-image.model';
import type { ApiEntityImage } from '../../../core/models/entity-image.model';
import type { HotelDetail } from '../models/hotel-detail.model';
import type { DetailLoadState } from '../../place-detail/services/place-detail.service';

interface ApiHotelDetail {
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
  website?: string | null;
  contactEmail?: string | null;
  phoneNumber?: string | null;
  about?: string;
  bookingPartners: { id: number; name: string; variant: string; url?: string | null }[];
  amenities: { id: number; label: string; icon: string }[];
  reviews: { id: number; authorUserId?: number; authorName: string; avatarUrl?: string; rating: number; timeAgo?: string; body: string; helpfulCount: number; reportedByCurrentUser?: boolean; isOwnReview?: boolean }[];
  similar: { id: number; title: string; image?: string; rating?: number; distance?: string; isSaved: boolean }[];
  isSaved: boolean;
  isOwnable?: boolean;
  isOwnedByCurrentUser?: boolean;
  hasPendingOwnershipClaim?: boolean;
  ownerContributionId?: number | null;
}

@Injectable({ providedIn: 'root' })
export class HotelDetailService {

  private readonly http = inject(HttpClient);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly analytics = inject(AnalyticsService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly collectionsService = inject(CollectionsService);
  private readonly locationSvc = inject(LocationService);
  private readonly destinationSvc = inject(DestinationService);
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

  invalidate(id?: string | number | null): void {
    if (id == null) {
      this._cache.set({});
      this._loadState.set({});
      this.lastRequestKeyById.clear();
      return;
    }

    const key = String(id);
    this._cache.update(current => {
      const { [key]: _removed, ...rest } = current;
      return rest;
    });
    this._loadState.update(current => {
      const { [key]: _removed, ...rest } = current;
      return rest;
    });
    this.lastRequestKeyById.delete(key);
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

        const images = normalizeEntityImages(dto.images);
        const detail: HotelDetail = {
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
          about: dto.about ?? '',
          website: dto.website ?? null,
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
          similar: dto.similar.map(s => ({
            id: String(s.id),
            title: s.title,
            image: s.image ?? '',
            rating: s.rating,
            distance: s.distance ?? '',
            isSaved: s.isSaved,
          })),
          isSaved: dto.isSaved,
          isOwnable: dto.isOwnable ?? true,
          isOwnedByCurrentUser: dto.isOwnedByCurrentUser ?? false,
          hasPendingOwnershipClaim: dto.hasPendingOwnershipClaim ?? false,
          ownerContributionId: dto.ownerContributionId ?? null,
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

  toggleSaved(id: string, collectionId?: number | null): void {
    const current = this.getHotelDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;

    const target = {
      entityType: 'place',
      entityId: numId,
      title: current.title,
      imageUrl: current.heroImage,
      surface: 'hotel_detail',
      analyticsEntityType: 'hotel',
    } as const;

    if (collectionId != null && collectionId > 0) {
      this.collectionsService.addItem(collectionId, {
        ...target,
        image: target.imageUrl,
      }).subscribe({ error: () => {} });
      return;
    }

    this.saveToCollection.open(target);
  }
}

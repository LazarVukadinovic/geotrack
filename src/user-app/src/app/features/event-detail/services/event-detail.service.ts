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
import type { EventDetail } from '../models/event-detail.model';
import type { DetailLoadState } from '../../place-detail/services/place-detail.service';

interface ApiEventDetail {
  id: number;
  title: string;
  location?: string;
  locationId?: number | null;
  locationName?: string | null;
  placeId?: number | null;
  placeName?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  heroImage?: string;
  images?: ApiEntityImage[] | null;
  tags: { label: string }[];
  distance?: string;
  about?: string;
  info: { date?: string; timeRange?: string; venue?: string; ticket?: string; duration?: string; hostedBy?: string };
  venueInfo: { description?: string; arrivalTips?: string; accessibility?: string };
  similar: { id: number; title: string; subtitle?: string; image?: string; isSaved: boolean }[];
  isSaved: boolean;
  externalTicketLink?: string | null;
}

@Injectable({ providedIn: 'root' })
export class EventDetailService {
  private readonly http = inject(HttpClient);
  private readonly favoriteState = inject(FavoriteStateService);
  private readonly analytics = inject(AnalyticsService);
  private readonly saveToCollection = inject(SaveToCollectionService);
  private readonly collectionsService = inject(CollectionsService);
  private readonly locationSvc = inject(LocationService);
  private readonly destinationSvc = inject(DestinationService);
  private readonly _cache = signal<Record<string, EventDetail>>({});
  private readonly _loadState = signal<Record<string, DetailLoadState>>({});
  private readonly lastRequestKeyById = new Map<string, string>();

  getEventDetailPageData(id: string): EventDetail | null {
    const cached = this._cache()[id];
    if (cached) {
      return {
        ...cached,
        isSaved: this.favoriteState.resolve('event', id, cached.isSaved),
      };
    }
    return null;
  }

  getLoadState(id: string): DetailLoadState {
    return this._loadState()[id] ?? 'idle';
  }

  loadEventDetail(id: string): void {
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

    this.http.get<ApiEventDetail>(`/api/events/${numId}${locationQuery}`).subscribe({
      next: (dto) => {
        if (this.lastRequestKeyById.get(id) !== requestKey) {
          return;
        }

        const images = normalizeEntityImages(dto.images);
        const detail: EventDetail = {
          id: String(dto.id),
          title: dto.title,
          location: dto.location ?? '',
          locationId: dto.locationId != null ? String(dto.locationId) : null,
          locationName: dto.locationName ?? null,
          placeId: dto.placeId != null ? String(dto.placeId) : null,
          placeName: dto.placeName ?? null,
          latitude: dto.latitude ?? null,
          longitude: dto.longitude ?? null,
          heroImage: getCoverImageUrl(images, dto.heroImage),
          images: getGalleryImages(images),
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
          ticketUrl: dto.externalTicketLink ?? null,
        };
        this._cache.update(c => ({ ...c, [String(dto.id)]: detail }));
        this.setLoadState(id, 'loaded');
        this.lastRequestKeyById.delete(id);
        this.analytics.track('detail_view', { entityType: 'event', entityId: dto.id });
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
    const current = this.getEventDetailPageData(id);
    if (!current) return;

    const numId = parseInt(id, 10);
    if (isNaN(numId)) return;

    const target = {
      entityType: 'event',
      entityId: numId,
      title: current.title,
      imageUrl: current.heroImage,
      surface: 'event_detail',
      analyticsEntityType: 'event',
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

import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { LocationService } from '../../../../core/services/location.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { PhotoGalleryComponent } from '../../../../shared/components/photo-gallery/photo-gallery.component';
import type { EntityImage } from '../../../../core/models/entity-image.model';
import { EventDetailService } from '../../services/event-detail.service';
import type { EventDetail } from '../../models/event-detail.model';
import type { MapEntityMarker } from '../../../../core/services/map-api.service';

@Component({
  selector: 'app-event-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent, ImageFallbackDirective, RouterLink, PhotoGalleryComponent],
  templateUrl: './event-detail-page.component.html',
  styleUrl: './event-detail-page.component.scss',
})
export class EventDetailPageComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly locationSvc = inject(LocationService);
  private readonly analytics = inject(AnalyticsService);
  private readonly service  = inject(EventDetailService);

  protected readonly eventId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );

  protected readonly event = computed(() =>
    this.service.getEventDetailPageData(this.eventId()),
  );
  protected readonly loadState = computed(() =>
    this.service.getLoadState(this.eventId()),
  );

  /** 0 = transparent top bar, 1 = full navy + visible title. Driven by ed__scroll. */
  protected readonly scrollProgress = signal(0);
  protected readonly galleryOpen = signal(false);
  protected readonly galleryStartIndex = signal(0);
  protected readonly heroImage = computed(() => this.event()?.heroImage ?? '');

  protected readonly allGalleryImages = computed((): EntityImage[] => {
    const ev = this.event();
    if (!ev) return [];
    const heroImg: EntityImage = {
      id: '__cover__',
      url: ev.heroImage,
      altText: ev.title,
      caption: '',
      isMain: true,
      imageRole: 'cover',
      sortOrder: -1,
      imagePosition: '50% 50%',
      imageSize: 'cover',
    };
    return ev.heroImage ? [heroImg, ...ev.images] : [...ev.images];
  });

  protected readonly directionsUrl = computed(() => this.buildDirectionsUrl(
    this.event()?.latitude ?? null,
    this.event()?.longitude ?? null,
  ));

  protected readonly hasMapCoordinates = computed(() => this.hasUsableCoordinates(
    this.event()?.latitude ?? null,
    this.event()?.longitude ?? null,
  ));

  protected readonly ticketUrl = computed(() => this.event()?.ticketUrl ?? null);

  protected readonly ticketMainText = computed(() => {
    const ticket = this.event()?.info.ticket ?? '';
    const idx = ticket.indexOf('(');
    return idx !== -1 ? ticket.slice(0, idx).trim() : ticket;
  });

  protected readonly ticketNote = computed(() => {
    const ticket = this.event()?.info.ticket ?? '';
    const match = ticket.match(/\(.*\)/);
    return match ? match[0] : null;
  });

  private readonly locationRequestKey = computed(() => {
    const loc = this.locationSvc.location();
    if (!loc) return 'none';
    return `${loc.latitude}:${loc.longitude}:${loc.source}`;
  });

  private readonly edScroll = viewChild<ElementRef<HTMLElement>>('edScroll');

  constructor() {
    effect(() => {
      this.eventId();
      this.scrollProgress.set(0);
      this.galleryOpen.set(false);
      const el = this.edScroll()?.nativeElement;
      if (el) el.scrollTop = 0;
    });

    effect(() => {
      const id = this.eventId();
      this.locationRequestKey();
      if (id) this.service.loadEventDetail(id);
    });
  }

  protected onScroll(event: Event): void {
    const scrollTop = (event.currentTarget as Element).scrollTop;
    this.scrollProgress.set(Math.min(scrollTop / 200, 1));
  }

  protected goBack(): void {
    globalThis.history.back();
  }

  protected toggleSaved(): void {
    const id = this.eventId();
    if (id) this.service.toggleSaved(id, this.targetCollectionId());
  }

  protected share(): void {
    const title = this.event()?.title ?? '';
    const url = globalThis.location.href;
    if (navigator.share) {
      navigator.share({ title, text: title, url }).catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return;
      });
    }
  }

  protected navigateToSimilar(id: string): void {
    void this.router.navigate(['/events', id], { queryParams: this.collectionQueryParams() });
  }

  protected openGallery(startIndex = 0): void {
    this.galleryStartIndex.set(startIndex);
    this.galleryOpen.set(true);
  }

  protected closeGallery(): void {
    this.galleryOpen.set(false);
  }

  protected viewOnMap(): void {
    const event = this.event();
    if (!event) return;

    const mapFocus = this.buildMapFocus(event);
    if (!mapFocus) return;

    void this.router.navigate(['/map'], {
      state: { mapFocus },
    });
  }

  protected onDirectionsClick(): void {
    const numId = parseInt(this.eventId(), 10);
    if (isNaN(numId)) return;
    this.analytics.track('directions_started', {
      entityType: 'event',
      entityId: numId,
      metadata: { provider: 'google_maps', context: 'event_detail' },
    });
  }

  private buildDirectionsUrl(latitude: number | null, longitude: number | null): string | null {
    if (!this.hasUsableCoordinates(latitude, longitude)) return null;
    return `https://www.google.com/maps/dir/?api=1&destination=${latitude},${longitude}`;
  }

  private hasUsableCoordinates(latitude: number | null, longitude: number | null): boolean {
    return latitude != null &&
      longitude != null &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      (latitude !== 0 || longitude !== 0);
  }

  private buildMapFocus(event: EventDetail): MapEntityMarker | null {
    const entityId = parseInt(event.id, 10);
    const latitude = event.latitude ?? null;
    const longitude = event.longitude ?? null;
    if (isNaN(entityId) || !this.hasUsableCoordinates(latitude, longitude)) return null;

    return {
      entityType: 'event',
      entityId,
      title: event.title,
      subtitle: event.info.venue || event.location || null,
      categoryLabel: event.tags[0]?.label ?? 'Event',
      imageUrl: event.heroImage || null,
      detailRoute: `/events/${event.id}`,
      latitude: latitude as number,
      longitude: longitude as number,
      distance: event.distance || null,
      isSaved: event.isSaved,
    };
  }

  private targetCollectionId(): number | null {
    const collectionId = Number(this.route.snapshot.queryParamMap.get('collectionId'));
    return Number.isFinite(collectionId) && collectionId > 0 ? collectionId : null;
  }

  private collectionQueryParams(): { collectionId?: number } {
    const collectionId = this.targetCollectionId();
    return collectionId != null ? { collectionId } : {};
  }
}

import { CommonModule, Location as BrowserLocation } from '@angular/common';
import { Component, DestroyRef, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { combineLatest, Observable, throwError } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { PhotoGalleryComponent } from '../../../../shared/components/photo-gallery/photo-gallery.component';
import { ReviewEntryComponent, type ReviewDraft } from '../../../../shared/components/review-entry/review-entry.component';
import { DescriptionRichHtmlPipe } from '../../../../shared/pipes/description-rich-html.pipe';
import { ReviewInteractionsService } from '../../../../shared/services/review-interactions.service';
import { ExploreService } from '../../../explore/services/explore.service';
import { ExploreSectionListingService } from '../../../explore/services/explore-section-listing.service';
import { ReviewsService, type ReviewReportTargetType } from '../../../reviews/services/reviews.service';
import { normalizeEntityImages, getCoverImageUrl, type ApiEntityImage, type EntityImage } from '../../../../core/models/entity-image.model';

interface DetailParentLink {
  label: string;
  name: string;
  route: string;
}

interface DetailItem {
  id: string;
  name: string;
  description?: string;
  route: string;
  image?: string;
  meta?: string;
  rating?: number | null;
  categoryLabel?: string;
  locationName?: string;
  statusBadge?: { label: string; tone: string };
  tags?: string[];
}

interface DetailSectionPreview {
  sectionKey?: string;
  sectionTitle?: string;
  title?: string;
  name?: string;
  places?: Record<string, unknown>[];
  events?: Record<string, unknown>[];
  locations?: Record<string, unknown>[];
}

interface ReviewItem {
  id: string;
  authorUserId: number;
  authorName: string;
  avatarUrl: string;
  rating: number;
  body: string;
  timeAgo: string;
  reportedByCurrentUser: boolean;
  isOwnReview: boolean;
}

interface HierarchyDetail {
  id: string;
  title: string;
  type: EntityType;
  typeLabel: string;
  description?: string;
  rating?: number | null;
  reviewsCount?: number | null;
  parent?: DetailParentLink | null;
  parentTerritoryId?: string | null;
  images: EntityImage[];
  tags: string[];
  sections: DetailSectionPreview[];
  relatedEvents: DetailItem[];
  topPlaces: DetailItem[];
  similarDestinations: DetailItem[];
  similarLocations: DetailItem[];
  similarRegions: DetailItem[];
  similarCountries: DetailItem[];
  reviews: ReviewItem[];
  hasMoreReviews: boolean;
  totalReviewsCount: number;
}

type EntityType = 'location' | 'destination' | 'region' | 'territory';
type LoadState = 'loading' | 'loaded' | 'notFound' | 'error';

@Component({
  selector: 'app-entity-hierarchy-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ImageFallbackDirective,
    PhotoGalleryComponent,
    ReviewEntryComponent,
    DescriptionRichHtmlPipe,
  ],
  providers: [ReviewInteractionsService],
  templateUrl: './entity-hierarchy-detail-page.component.html',
  styleUrl: './entity-hierarchy-detail-page.component.scss',
})
export class EntityHierarchyDetailPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly browserLocation = inject(BrowserLocation);
  private readonly router = inject(Router);
  private readonly analytics = inject(AnalyticsService);
  private readonly auth = inject(AuthService);
  private readonly reviewsService = inject(ReviewsService);
  private readonly exploreService = inject(ExploreService);
  private readonly listingService = inject(ExploreSectionListingService);

  protected readonly ri = inject(ReviewInteractionsService);
  protected readonly detail = signal<HierarchyDetail | null>(null);
  protected readonly loadState = signal<LoadState>('loading');
  protected readonly descriptionExpanded = signal(false);
  protected readonly galleryOpen = signal(false);
  protected readonly galleryStartIndex = signal(0);
  protected readonly scrollProgress = signal(0);
  protected readonly currentType = signal<EntityType | null>(null);
  protected readonly visibleReviewsCount = signal(3);
  protected readonly expandedReviewIds = signal<Set<string>>(new Set());
  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());

  private readonly ehdScroll = viewChild<ElementRef<HTMLElement>>('ehdScroll');

  protected readonly canReview = computed(() => {
    const d = this.detail();
    return !!d && this.isReviewableType(d.type) && !d.reviews.some(r => r.isOwnReview);
  });

  protected readonly heroImage = computed(() => {
    const d = this.detail();
    if (!d) return '';
    return getCoverImageUrl(d.images, d.images[0]?.url ?? '');
  });

  protected readonly displayTags = computed<string[]>(() => {
    const d = this.detail();
    if (!d) return [];
    return d.tags.length > 0 ? d.tags : [d.typeLabel];
  });

  // Regions and territories frequently have no stored description. Fall back to
  // a friendly placeholder so the "About" section always reads well.
  protected readonly displayDescription = computed<string>(() => {
    const d = this.detail();
    if (!d) return '';
    const text = (d.description ?? '').trim();
    if (text.length > 0) return text;

    switch (d.type) {
      case 'region':
        return `Explore the destinations, highlights and hidden gems across ${d.title}.`;
      case 'territory':
        return `Discover the regions, destinations and places that make up ${d.title}.`;
      case 'destination':
        return `Explore what ${d.title} has to offer.`;
      default:
        return `Discover ${d.title}.`;
    }
  });

  protected readonly galleryImages = computed(() => {
    const d = this.detail();
    if (!d) return [];

    const seenUrls = new Set<string>();
    return d.images.filter(image => {
      const key = image.url.trim().toLowerCase();
      if (!key || seenUrls.has(key)) return false;
      seenUrls.add(key);
      return true;
    });
  });

  protected readonly showcaseImages = computed(() => this.galleryImages());

  protected readonly territoryRegions = computed(() => {
    const d = this.detail();
    if (!d || d.type !== 'territory') return [];
    return this.uniqueItems(d.sections.flatMap(section => this.sectionItems(section)));
  });

  protected readonly locationPlaces = computed(() => {
    const d = this.detail();
    if (!d || d.type !== 'location') return [];
    return this.uniqueItems(
      d.sections.flatMap(section => (section.places ?? []).map(item => this.mapItem(item, 'place'))),
    );
  });

  protected readonly visibleReviews = computed(() => {
    const d = this.detail();
    if (!d) return [];
    const reviews = d.reviews;
    const limit = this.visibleReviewsCount();
    const ownReview = reviews.find(review => review.isOwnReview);
    const ordered = ownReview
      ? [ownReview, ...reviews.filter(review => review !== ownReview)]
      : reviews;
    return ordered.slice(0, limit);
  });

  protected readonly hiddenReviewsCount = computed(() => {
    const d = this.detail();
    if (!d) return 0;
    return Math.max(0, d.totalReviewsCount - this.visibleReviews().length);
  });

  constructor() {
    this.ri.configure({
      targetType: () => this.resolveReviewTargetType(),
      onEditSuccess: () => {
        this.reloadCurrentDetail();
        this.exploreService.invalidate();
        this.listingService.invalidate();
      },
      onDeleteSuccess: () => {
        this.reloadCurrentDetail();
        this.exploreService.invalidate();
        this.listingService.invalidate();
      },
      onReportSuccess: (reviewId) => this.markReviewReported(reviewId),
    });

    combineLatest([this.route.paramMap, this.route.data])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([params, data]) => {
        const id = params.get('id') ?? '';
        const type = data['entityType'] as EntityType | undefined;
        this.currentType.set(type ?? null);
        this.ri.clearReportNotice();
        this.galleryOpen.set(false);
        this.scrollProgress.set(0);
        this.descriptionExpanded.set(false);
        this.visibleReviewsCount.set(3);
        this.expandedReviewIds.set(new Set());

        const el = this.ehdScroll()?.nativeElement;
        if (el) el.scrollTop = 0;

        if (!type || !id || Number.isNaN(Number(id))) {
          this.loadState.set('notFound');
          this.detail.set(null);
          return;
        }

        this.load(type, id);
      });
  }

  protected onScroll(event: Event): void {
    const scrollTop = (event.currentTarget as Element).scrollTop;
    this.scrollProgress.set(Math.min(scrollTop / 200, 1));
  }

  protected goBack(): void {
    this.browserLocation.back();
  }

  protected share(): void {
    const title = this.detail()?.title ?? '';
    const url = globalThis.location.href;
    if (navigator.share) {
      navigator.share({ title, text: title, url }).catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return;
      });
    }
  }

  protected openGallery(startIndex = 0): void {
    this.galleryStartIndex.set(startIndex);
    this.galleryOpen.set(true);
  }

  protected closeGallery(): void {
    this.galleryOpen.set(false);
  }

  protected hasLongAbout(text: string | null | undefined): boolean {
    const value = (text ?? '').trim();
    return value.length > 200 || value.split(/\r\n|\r|\n/).length > 4;
  }

  protected submitReview = (draft: ReviewDraft): Observable<unknown> => {
    const d = this.detail();
    if (!d || !this.isReviewableType(d.type)) {
      return throwError(() => new Error('Reviews are available only for destinations and locations.'));
    }

    const numId = Number(d.id);
    if (!Number.isFinite(numId)) return throwError(() => new Error('Invalid id.'));

    return this.reviewsService.createReview(d.type, numId, draft.rating, draft.body);
  };

  protected onReviewSubmitted(_draft: ReviewDraft): void {
    const d = this.detail();
    if (!d) return;

    if (this.isReviewableType(d.type)) {
      this.analytics.track('review_created', {
        entityType: d.type,
        entityId: Number(d.id),
      });
    }

    this.load(d.type, d.id);
    this.exploreService.invalidate();
    this.listingService.invalidate();
  }

  protected goToLogin(): void {
    void this.router.navigate(['/profile']);
  }

  protected exploreCurrentDestination(): void {
    const d = this.detail();
    if (!d || d.type !== 'destination') return;
    void this.router.navigate(['/explore'], { queryParams: { destinationId: d.id } });
  }

  protected showMoreReviews(): void {
    this.visibleReviewsCount.update(current => current + 3);
  }

  protected showLessReviews(): void {
    this.visibleReviewsCount.set(3);
    this.expandedReviewIds.set(new Set());
  }

  protected hasLongReview(body: string | null | undefined): boolean {
    const value = (body ?? '').trim();
    return value.length > 180 || value.split(/\r\n|\r|\n/).length > 3;
  }

  protected isReviewExpanded(reviewId: string): boolean {
    return this.expandedReviewIds().has(reviewId);
  }

  protected toggleReviewExpanded(reviewId: string): void {
    this.expandedReviewIds.update(current => {
      const next = new Set(current);
      if (next.has(reviewId)) next.delete(reviewId);
      else next.add(reviewId);
      return next;
    });
  }

  protected navigateToAuthorReviews(review: ReviewItem): void {
    this.ri.goToAuthorReviews(review);
  }

  /**
   * Returns a duplicated array to enable infinite loop scroll.
   * The scroll handler (onTrackScroll) resets position seamlessly
   * when the user reaches the boundary of the duplicated set.
   * Single-item lists are not duplicated since loop makes no sense.
   */
  protected loopItems<T>(items: T[]): T[] {
    return items.length > 1 ? [...items, ...items] : items;
  }

  /**
   * Handles the scroll event on a loop track element.
   * When the user scrolls past the midpoint (end of first set),
   * the position is snapped back to the equivalent position in the
   * first half so the loop feels seamless. Same logic applies in reverse.
   */
  protected onTrackScroll(el: HTMLElement): void {
    if (!el) return;
    const half = Math.floor(el.scrollWidth / 2);
    if (half <= 0) return;

    if (el.scrollLeft >= half) {
      el.scrollLeft = el.scrollLeft - half;
    } else if (el.scrollLeft <= 0 && el.scrollWidth > el.clientWidth) {
      el.scrollLeft = half + el.scrollLeft;
    }
  }

  protected sectionItems(section: DetailSectionPreview): DetailItem[] {
    const locations = (section.locations ?? []).map(item => this.mapItem(item, 'location'));
    const places = (section.places ?? []).map(item => this.mapItem(item, 'place'));
    const events = (section.events ?? []).map(item => this.mapItem(item, 'event'));
    return [...locations, ...places, ...events];
  }

  private load(type: EntityType, id: string): void {
    this.loadState.set('loading');
    this.detail.set(null);

    this.http.get<Record<string, unknown>>(this.endpoint(type, id)).subscribe({
      next: (dto) => {
        const mapped = this.mapDto(type, dto);
        this.detail.set(mapped);
        this.loadReviews(type, id);
        this.loadRelatedEntities(mapped);
        this.trackDetailView(type, dto);
        this.loadState.set('loaded');
      },
      error: (err) => {
        this.detail.set(null);
        this.loadState.set(err?.status === 404 ? 'notFound' : 'error');
      },
    });
  }

  /**
   * Loads "similar regions" (sibling regions in the same territory) and
   * "similar countries" (other territories) via existing endpoints. These are
   * not part of the primary detail payload, so they are fetched lazily and
   * merged into the detail signal once available.
   */
  private loadRelatedEntities(detail: HierarchyDetail): void {
    if (detail.type === 'region' && detail.parentTerritoryId) {
      const territoryId = detail.parentTerritoryId;
      this.http.get<Record<string, unknown>>(`/api/Territory/${territoryId}`)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (dto) => {
            const regions = Array.isArray(dto['regions']) ? dto['regions'] as Record<string, unknown>[] : [];
            const items = regions
              .filter(r => String(r['id']) !== detail.id)
              .map(r => this.mapItem({ ...r, route: `/regions/${r['id']}` }, 'location'));
            this.patchDetail(detail, current => ({ ...current, similarRegions: items }));
          },
          error: () => {},
        });
    }

    if (detail.type === 'territory') {
      this.http.get<Record<string, unknown>>('/api/Territory')
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (dto) => {
            const territories = Array.isArray(dto['territories']) ? dto['territories'] as Record<string, unknown>[] : [];
            const items = territories
              .filter(t => String(t['id']) !== detail.id)
              .map(t => this.mapItem({ ...t, route: `/territories/${t['id']}` }, 'location'));
            this.patchDetail(detail, current => ({ ...current, similarCountries: items }));
          },
          error: () => {},
        });
    }
  }

  private patchDetail(reference: HierarchyDetail, update: (current: HierarchyDetail) => HierarchyDetail): void {
    this.detail.update(current =>
      current && current.type === reference.type && current.id === reference.id
        ? update(current)
        : current);
  }

  private loadReviews(type: EntityType, id: string): void {
    if (!this.isReviewableType(type)) return;

    const entityId = Number(id);
    if (!Number.isFinite(entityId)) return;

    this.reviewsService.getReviews(type, entityId, 0, 50)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          const reviews = result.reviews.map(review => ({
            id: review.id,
            authorUserId: review.authorUserId,
            authorName: review.authorName || 'GeoTrack traveler',
            avatarUrl: review.avatarUrl ?? '',
            rating: review.rating,
            body: review.body ?? '',
            timeAgo: review.timeAgo ?? '',
            reportedByCurrentUser: review.reportedByCurrentUser,
            isOwnReview: review.isOwnReview,
          }));

          this.detail.update(current => current && current.type === type && current.id === id
            ? {
                ...current,
                reviews,
                rating: result.averageRating,
                reviewsCount: result.reviewsCount,
                totalReviewsCount: result.reviewsCount,
                hasMoreReviews: result.hasMore,
              }
            : current);
        },
        error: () => {},
      });
  }

  private endpoint(type: EntityType, id: string): string {
    switch (type) {
      case 'location': return `/api/locations/${id}`;
      case 'destination': return `/api/destinations/${id}`;
      case 'region': return `/api/regions/${id}`;
      case 'territory': return `/api/Territory/${id}`;
    }
  }

  private trackDetailView(type: EntityType, dto: Record<string, unknown>): void {
    if (!this.isReviewableType(type)) return;

    const entityId = this.toNullableNumber(dto['id']);
    if (entityId == null || entityId <= 0) return;

    this.analytics.track('detail_view', {
      entityType: type,
      entityId,
      destinationId: type === 'destination' ? entityId : this.toNullableNumber(dto['destinationId']) ?? undefined,
    });
  }

  private mapDto(type: EntityType, dto: Record<string, unknown>): HierarchyDetail {
    const id = this.toStringValue(dto['id']);
    const title = this.toStringValue(dto['name']) || 'Untitled';
    const images = normalizeEntityImages(dto['images'] as ApiEntityImage[] | null | undefined);
    const tags = this.mapTags(dto['tags']) ?? [];
    const base = {
      id,
      title,
      description: this.toNullableString(dto['description']) ?? undefined,
      rating: this.toNullableNumber(dto['averageRating']),
      reviewsCount: this.toNullableNumber(dto['reviewsCount']),
      images,
      tags,
      reviews: [],
      hasMoreReviews: false,
      totalReviewsCount: this.toNullableNumber(dto['reviewsCount']) ?? 0,
      topPlaces: [] as DetailItem[],
      similarDestinations: [] as DetailItem[],
      similarLocations: [] as DetailItem[],
      similarRegions: [] as DetailItem[],
      similarCountries: [] as DetailItem[],
      relatedEvents: [] as DetailItem[],
      parentTerritoryId: null as string | null,
    };

    if (type === 'location') {
      const destinationId = this.toNullableString(dto['destinationId']);
      const destinationName = this.toNullableString(dto['destinationName']);
      return {
        ...base,
        type,
        typeLabel: 'Location',
        parent: destinationId ? { label: 'Destination', name: destinationName ?? `Destination #${destinationId}`, route: `/destinations/${destinationId}` } : null,
        sections: this.mapLocationSections(dto),
        relatedEvents: this.mapRelated(dto['relatedEvents'], 'event'),
        similarLocations: this.mapRelated(dto['similarLocations'], 'location'),
      };
    }

    if (type === 'destination') {
      const regionId = this.toNullableString(dto['regionId']);
      const regionName = this.toNullableString(dto['regionName']);
      return {
        ...base,
        type,
        typeLabel: 'Destination',
        parent: regionId ? { label: 'Region', name: regionName ?? `Region #${regionId}`, route: `/regions/${regionId}` } : null,
        sections: this.mapSectionsWithRoutes(dto, false),
        topPlaces: this.mapTopPlaces(dto['topPlaces']),
        similarDestinations: this.mapRelated(dto['similarDestinations'], 'destination'),
      };
    }

    if (type === 'region') {
      const territoryId = this.toNullableString(dto['territoryId']);
      const territoryName = this.toNullableString(dto['territoryName']);
      const destinations = Array.isArray(dto['destinations']) ? dto['destinations'] as Record<string, unknown>[] : [];
      return {
        ...base,
        type,
        typeLabel: 'Region',
        parent: territoryId ? { label: 'Territory', name: territoryName ?? `Territory #${territoryId}`, route: `/territories/${territoryId}` } : null,
        parentTerritoryId: territoryId,
        sections: destinations.length ? [{
          sectionTitle: 'Destinations',
          locations: destinations.map(d => ({ ...d, route: `/destinations/${d['id']}` })),
        }] : [],
      };
    }

    const regions = Array.isArray(dto['regions']) ? dto['regions'] as Record<string, unknown>[] : [];
    return {
      ...base,
      type,
      typeLabel: 'Territory',
      parent: null,
      sections: regions.length ? [{
        sectionTitle: 'Regions',
        locations: regions.map(r => ({ ...r, route: `/regions/${r['id']}` })),
      }] : [],
    };
  }

  private mapTopPlaces(value: unknown): DetailItem[] {
    if (!Array.isArray(value)) return [];
    return (value as Record<string, unknown>[]).map(item =>
      this.mapItem({ ...item, route: `/places/${item['id']}` }, 'place'));
  }

  private mapSectionsWithRoutes(dto: Record<string, unknown>, includePlaces = true): DetailSectionPreview[] {
    const sections = Array.isArray(dto['sections']) ? dto['sections'] as Record<string, unknown>[] : [];
    return sections
      .map(section => ({
        sectionKey: this.toStringValue(section['key']),
        sectionTitle: this.toStringValue(section['title']),
        locations: (Array.isArray(section['locations']) ? section['locations'] as Record<string, unknown>[] : [])
          .map(l => ({ ...l, route: `/locations/${l['id']}` })),
        places: includePlaces
          ? (Array.isArray(section['places']) ? section['places'] as Record<string, unknown>[] : [])
              .map(p => ({ ...p, route: `/places/${p['id']}` }))
          : [],
        events: (Array.isArray(section['events']) ? section['events'] as Record<string, unknown>[] : [])
          .map(e => ({ ...e, route: `/events/${e['id']}` })),
      }))
      .filter(section => this.sectionCount(section) > 0);
  }

  private mapLocationSections(dto: Record<string, unknown>): DetailSectionPreview[] {
    return this.mapSectionsWithRoutes(dto, true).map(section => ({
      ...section,
      sectionTitle: (section.sectionKey ?? '').toLowerCase() === 'places'
        ? 'Places in this location'
        : section.sectionTitle,
    }));
  }

  private sectionCount(section: DetailSectionPreview): number {
    return (section.places?.length ?? 0) + (section.events?.length ?? 0) + (section.locations?.length ?? 0);
  }

  private mapItem(item: Record<string, unknown>, fallbackType: 'location' | 'place' | 'event' | 'destination'): DetailItem {
    const id = this.toStringValue(item['id']);
    const route = this.toNullableString(item['route'])
      ?? `/${fallbackType === 'event' ? 'events' : `${fallbackType}s`}/${id}`;

    return {
      id,
      name: this.toStringValue(item['name'] ?? item['title']) || 'Item',
      description: this.toNullableString(item['description']) ?? undefined,
      image: this.toNullableString(item['image'] ?? item['imageUrl']) ?? undefined,
      route,
      rating: this.toNullableNumber(item['averageRating'] ?? item['rating']),
      meta: fallbackType === 'event' ? this.formatEventDate(item['startDate']) : undefined,
      categoryLabel: this.toNullableString(item['categoryLabel']) ?? undefined,
      locationName: this.toNullableString(item['locationName']) ?? undefined,
      statusBadge: this.mapStatusBadge(item['statusBadge']),
      tags: this.mapTags(item['tags']),
    };
  }

  private mapStatusBadge(value: unknown): { label: string; tone: string } | undefined {
    if (!value || typeof value !== 'object') return undefined;
    const obj = value as Record<string, unknown>;
    const label = this.toNullableString(obj['label']);
    if (!label) return undefined;
    return { label, tone: this.toNullableString(obj['tone']) ?? 'neutral' };
  }

  private mapTags(value: unknown): string[] | undefined {
    if (!Array.isArray(value) || value.length === 0) return undefined;
    const labels = (value as unknown[])
      .map(t => {
        if (typeof t === 'string') return t.trim();
        if (t && typeof t === 'object') {
          const obj = t as Record<string, unknown>;
          return this.toNullableString(obj['name'] ?? obj['label'] ?? obj['tag']) ?? '';
        }
        return '';
      })
      .filter(Boolean);
    return labels.length > 0 ? labels : undefined;
  }

  private mapRelated(value: unknown, type: 'destination' | 'location' | 'event'): DetailItem[] {
    if (!Array.isArray(value)) return [];
    return (value as Record<string, unknown>[]).map(item => this.mapItem(item, type));
  }

  private uniqueItems(items: DetailItem[]): DetailItem[] {
    const seen = new Set<string>();
    const result: DetailItem[] = [];
    for (const item of items) {
      const key = item.route || item.id;
      if (!key || seen.has(key)) continue;
      seen.add(key);
      result.push(item);
    }
    return result;
  }

  private reloadCurrentDetail(): void {
    const d = this.detail();
    if (!d) return;
    this.load(d.type, d.id);
  }

  private markReviewReported(reviewId: string): void {
    this.detail.update(current => current
      ? {
          ...current,
          reviews: current.reviews.map(review => review.id === reviewId
            ? { ...review, reportedByCurrentUser: true }
            : review),
        }
      : current);
  }

  private resolveReviewTargetType(): ReviewReportTargetType | null {
    const type = this.currentType();
    return type === 'destination' || type === 'location' ? type : null;
  }

  private isReviewableType(type: EntityType): type is 'destination' | 'location' {
    return type === 'destination' || type === 'location';
  }

  private formatEventDate(value: unknown): string | undefined {
    const date = this.formatDate(value);
    return date || undefined;
  }

  private formatDate(value: unknown): string {
    if (!value) return '';
    const date = new Date(String(value));
    if (Number.isNaN(date.getTime())) return '';
    return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
  }

  private toStringValue(value: unknown): string {
    return value == null ? '' : String(value);
  }

  private toNullableString(value: unknown): string | null {
    if (value == null) return null;
    const text = String(value).trim();
    return text.length > 0 ? text : null;
  }

  private toNullableNumber(value: unknown): number | null {
    if (typeof value === 'number' && Number.isFinite(value)) return value;
    if (typeof value === 'string' && value.trim().length > 0) {
      const parsed = Number(value);
      return Number.isFinite(parsed) ? parsed : null;
    }
    return null;
  }
}

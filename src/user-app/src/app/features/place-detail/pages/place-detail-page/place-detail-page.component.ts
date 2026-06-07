import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { Observable, throwError } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { LocationService } from '../../../../core/services/location.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { ReviewEntryComponent } from '../../../../shared/components/review-entry/review-entry.component';
import { UserAvatarComponent } from '../../../../shared/components/user-avatar/user-avatar.component';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { HttpClient } from '@angular/common/http';
import { PlaceDetailService } from '../../services/place-detail.service';
import { ReviewInteractionsService } from '../../../../shared/services/review-interactions.service';
import { ExploreService } from '../../../explore/services/explore.service';
import { ExploreSectionListingService } from '../../../explore/services/explore-section-listing.service';
import { reviewsMoreActionLabel } from '../../../reviews/utils/review-section-ui';
import type { ReviewDraft } from '../../../../shared/components/review-entry/review-entry.component';
import type { PlaceDetail } from '../../../../core/models/place.model';
import type { MapEntityMarker, MapEntityType } from '../../../../core/services/map-api.service';

@Component({
  selector: 'app-place-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent, ReviewEntryComponent, UserAvatarComponent, ImageFallbackDirective, RouterLink],
  providers: [ReviewInteractionsService],
  templateUrl: './place-detail-page.component.html',
  styleUrl: './place-detail-page.component.scss',
})
export class PlaceDetailPageComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly service  = inject(PlaceDetailService);
  private readonly auth     = inject(AuthService);
  private readonly locationSvc = inject(LocationService);
  private readonly analytics = inject(AnalyticsService);
  private readonly http     = inject(HttpClient);
  private readonly exploreService = inject(ExploreService);
  private readonly listingService = inject(ExploreSectionListingService);

  protected readonly ri = inject(ReviewInteractionsService);
  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());
  protected readonly reviewsMoreActionLabel = reviewsMoreActionLabel;

  protected readonly placeId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );

  protected readonly place = computed(() =>
    this.service.getPlaceDetailPageData(this.placeId()),
  );
  protected readonly loadState = computed(() =>
    this.service.getLoadState(this.placeId()),
  );

  protected readonly scrollProgress = signal(0);
  protected readonly hoursExpanded = signal(false);
  protected readonly galleryOpen = signal(false);
  protected readonly selectedImageUrl = signal<string | null>(null);
  protected readonly ownershipClaimOpen = signal(false);
  protected readonly ownershipProofFile = signal<File | null>(null);
  protected readonly ownershipProofFileName = signal('');
  protected readonly ownershipClaimError = signal('');
  protected readonly ownershipClaimMessage = signal('');
  protected readonly ownershipClaimSubmitting = signal(false);
  protected readonly ownershipClaimSubmitted = signal(false);

  protected readonly heroImage = computed(() => this.selectedImageUrl() ?? this.place()?.heroImage ?? '');
  protected readonly heroImagePosition = computed(() => {
    const p = this.place();
    const selected = this.selectedImageUrl();
    if (!p) return '50% 50%';
    if (!selected || selected === p.heroImage) return p.heroImagePosition || '50% 50%';
    return p.images.find(image => image.url === selected)?.imagePosition || '50% 50%';
  });
  protected readonly heroImageSize = computed(() => {
    const p = this.place();
    const selected = this.selectedImageUrl();
    if (!p) return 'cover';
    if (!selected || selected === p.heroImage) return p.heroImageSize || 'cover';
    return p.images.find(image => image.url === selected)?.imageSize || 'cover';
  });
  protected readonly heroImageTransform = computed(() => {
    const size = this.heroImageSize();
    if (size === 'cover') return 'none';

    const numeric = Number.parseFloat(size);
    if (!Number.isFinite(numeric) || numeric <= 100) return 'none';

    return `scale(${Math.min(numeric, 200) / 100})`;
  });

  protected readonly directionsUrl = computed(() => this.buildDirectionsUrl(
    this.place()?.latitude ?? null,
    this.place()?.longitude ?? null,
  ));

  protected readonly hasMapCoordinates = computed(() => this.hasUsableCoordinates(
    this.place()?.latitude ?? null,
    this.place()?.longitude ?? null,
  ));

  protected readonly is24Hours = computed(() => {
    const p = this.place();
    if (!p) return false;
    if (p.openingHoursSchedule && p.openingHoursSchedule.length > 0) return false;
    const hours = (p.openingHours ?? '').toLowerCase().trim();
    return hours.includes('24');
  });

  protected readonly computedOpenStatus = computed(() => {
    const p = this.place();
    if (!p) return '';
    if (this.is24Hours()) return 'Open now';
    const schedule = p.openingHoursSchedule;
    if (!schedule || schedule.length === 0) return p.openStatus ?? '';
    return this.calculateCurrentOpenStatus(schedule, p.openStatus ?? '');
  });

  private readonly locationRequestKey = computed(() => {
    const loc = this.locationSvc.location();
    if (!loc) return 'none';
    return `${loc.latitude}:${loc.longitude}:${loc.source}`;
  });

  private readonly pdScroll = viewChild<ElementRef<HTMLElement>>('pdScroll');

  constructor() {
    this.ri.configure({
      targetType: 'place',
      onEditSuccess: () => {
        const id = this.placeId();
        if (id) this.service.loadPlaceDetail(id);
      },
      onDeleteSuccess: () => {
        const id = this.placeId();
        if (id) this.service.loadPlaceDetail(id);
      },
    });

    effect(() => {
      this.placeId();
      this.scrollProgress.set(0);
      this.hoursExpanded.set(false);
      this.galleryOpen.set(false);
      this.selectedImageUrl.set(null);
      const el = this.pdScroll()?.nativeElement;
      if (el) el.scrollTop = 0;
    });

    effect(() => {
      const id = this.placeId();
      this.locationRequestKey();
      if (id) this.service.loadPlaceDetail(id);
    });
  }

  protected onScroll(event: Event): void {
    const scrollTop = (event.currentTarget as Element).scrollTop;
    this.scrollProgress.set(Math.min(scrollTop / 200, 1));
  }

  protected goBack(): void {
    const detailBackUrl = this.detailBackUrl();
    if (detailBackUrl) {
      void this.router.navigateByUrl(detailBackUrl, {
        replaceUrl: true,
        state: this.detailBackState(),
      });
      return;
    }

    globalThis.history.back();
  }

  protected goToLogin(): void {
    this.router.navigate(['/profile']);
  }

  protected toggleSaved(): void {
    const id = this.placeId();
    if (id) this.service.toggleSaved(id, this.targetCollectionId());
  }

  protected share(): void {
    const title = this.place()?.title ?? '';
    const url = globalThis.location.href;
    if (navigator.share) {
      navigator.share({ title, text: title, url }).catch((err: unknown) => {
        if (err instanceof DOMException && err.name === 'AbortError') return;
      });
    }
  }

  protected navigateToSimilar(id: string): void {
    void this.router.navigate(['/places', id], { queryParams: this.collectionQueryParams() });
  }

  protected navigateToEvent(id: string): void {
    void this.router.navigate(['/events', id], { queryParams: this.collectionQueryParams() });
  }

  protected selectGalleryImage(url: string): void {
    this.selectedImageUrl.set(url);
  }

  protected isGalleryImageActive(url: string): boolean {
    return (this.selectedImageUrl() ?? this.place()?.heroImage ?? '') === url;
  }

  protected openGallery(): void {
    this.galleryOpen.set(true);
  }

  protected closeGallery(): void {
    this.galleryOpen.set(false);
  }

  protected openOwnershipClaim(): void {
    if (!this.isLoggedIn()) {
      this.goToLogin();
      return;
    }

    this.ownershipClaimError.set('');
    this.ownershipClaimMessage.set('');
    this.ownershipProofFile.set(null);
    this.ownershipProofFileName.set('');
    this.ownershipClaimOpen.set(true);
  }

  protected closeOwnershipClaim(): void {
    if (this.ownershipClaimSubmitting()) return;
    this.ownershipClaimOpen.set(false);
  }

  protected onOwnershipProofSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    this.ownershipClaimError.set('');

    if (!file) return;

    const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
    if (!isPdf) {
      this.ownershipClaimError.set('Upload a PDF document.');
      return;
    }

    if (file.size > 10 * 1024 * 1024) {
      this.ownershipClaimError.set('Document must be 10 MB or smaller.');
      return;
    }

    this.ownershipProofFile.set(file);
    this.ownershipProofFileName.set(file.name);
  }

  protected submitOwnershipClaim(): void {
    const place = this.place();
    const file = this.ownershipProofFile();
    if (!place || this.ownershipClaimSubmitting()) return;

    if (!file) {
      this.ownershipClaimError.set('Ownership proof document is required.');
      return;
    }

    const numericPlaceId = parseInt(place.id, 10);
    if (isNaN(numericPlaceId)) {
      this.ownershipClaimError.set('Invalid place.');
      return;
    }

    const formData = new FormData();
    formData.append('payload', JSON.stringify({
      type: 'ClaimOwnership',
      placeId: numericPlaceId,
      placeType: place.category ?? 'Place',
      placeTypeId: null,
      name: place.title,
      description: place.about ?? '',
    }));
    formData.append('ownershipProofDocument', file);

    this.ownershipClaimSubmitting.set(true);
    this.ownershipClaimError.set('');
    this.ownershipClaimMessage.set('');

    this.http.post('/api/contributions/places', formData).subscribe({
      next: () => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimSubmitted.set(true);
        this.ownershipClaimOpen.set(false);
        this.ownershipClaimMessage.set('Ownership request submitted. A regional manager will review it.');
      },
      error: (err) => {
        this.ownershipClaimSubmitting.set(false);
        this.ownershipClaimError.set(err?.error?.message || 'Could not submit ownership request.');
      },
    });
  }

  protected navigateToOwnerEdit(): void {
    const p = this.place();
    if (!p) return;

    const sharedState = {
      locationLocked: true,
      returnToDetail: true,
      detailBackUrl: this.detailBackUrl(),
      detailBackState: this.detailBackState(),
    };

    if (p.ownerContributionId) {
      void this.router.navigate(['/contributions', p.ownerContributionId, 'edit'], {
        replaceUrl: true,
        state: sharedState,
      });
      return;
    }

    if (p.isOwnedByCurrentUser) {
      const numericId = parseInt(p.id, 10);
      void this.router.navigate(['/contributions/submit'], {
        replaceUrl: true,
        state: {
          ...sharedState,
          updateForPlaceId: numericId,
          placeTitle: p.title,
          placeCoverImageUrl: p.heroImage ?? '',
          placeAbout: p.about ?? '',
          placeLat: p.latitude ?? null,
          placeLng: p.longitude ?? null,
        },
      });
    }
  }

  protected openReviews(): void {
    const place = this.place();
    if (!place) return;

    void this.router.navigate(['/reviews', this.getReviewEntityType(place), place.id], {
      queryParams: { title: place.title },
      state: { entityTitle: place.title },
    });
  }

  protected viewOnMap(): void {
    const place = this.place();
    if (!place) return;

    const mapFocus = this.buildMapFocus(place);
    if (!mapFocus) return;

    void this.router.navigate(['/map'], { state: { mapFocus } });
  }

  protected readonly submitReview = (draft: ReviewDraft): Observable<unknown> => {
    const numId = parseInt(this.placeId(), 10);
    if (isNaN(numId)) return throwError(() => new Error('Invalid place id.'));
    return this.http.post(`/api/places/${numId}/reviews`, {
      rating:  draft.rating,
      comment: draft.body,
    });
  };

  protected onReviewSubmitted(_draft: ReviewDraft): void {
    const id = this.placeId();
    if (!id) return;
    const numId = parseInt(id, 10);
    if (!isNaN(numId)) {
      this.analytics.track('review_created', { entityType: 'place', entityId: numId });
    }
    this.service.loadPlaceDetail(id);
    this.exploreService.invalidate();
    this.listingService.invalidate();
  }

  protected onDirectionsClick(): void {
    const numId = parseInt(this.placeId(), 10);
    if (isNaN(numId)) return;
    this.analytics.track('directions_started', {
      entityType: 'place',
      entityId: numId,
      metadata: { provider: 'google_maps', context: 'place_detail' },
    });
  }

  protected amenityIconPath(icon: string | null | undefined): string {
    switch ((icon ?? '').trim().toLowerCase()) {
      case 'pool':
        return 'M2 20c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2M2 16c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2M10 8.5a2.5 2.5 0 0 1 5 0V12H10V8.5zM14 3a2 2 0 1 1-4 0 2 2 0 0 1 4 0z';
      case 'wifi':
        return 'M5 12.55a11 11 0 0 1 14.08 0M1.42 9a16 16 0 0 1 21.16 0M8.53 16.11a6 6 0 0 1 6.95 0M12 20h.01';
      case 'breakfast':
        return 'M18 8h1a4 4 0 0 1 0 8h-1M2 8h16v9a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8zM6 1v3M10 1v3M14 1v3';
      case 'parking':
        return 'M13 5H9v14M9 5h4a4 4 0 0 1 0 8H9';
      case 'spa':
        return 'M12 22s-8-4.5-8-11.8A8 8 0 0 1 12 2a8 8 0 0 1 8 8.2c0 7.3-8 11.8-8 11.8zM12 2C6.5 2 2 6.5 2 12';
      case 'gym':
        return 'M6.5 6.5 17.5 17.5M6.5 17.5 17.5 6.5M3 12h3M18 12h3M12 3v3M12 18v3';
      case 'restaurant':
        return 'M3 2v7c0 1.1.9 2 2 2h4a2 2 0 0 0 2-2V2M7 2v20M21 15V2a5 5 0 0 0-5 5v6c0 1.1.9 2 2 2h3zm0 0v7';
      case 'ac':
        return 'M12 3v18M3 12h18M5.6 5.6l12.8 12.8M18.4 5.6 5.6 18.4M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z';
      case 'outdoor':
        return 'M3 21h18M3 21l9-18 9 18M9 21v-6h6v6';
      case 'sea':
        return 'M2 17c1.5 0 1.5-1 3-1s1.5 1 3 1 1.5-1 3-1 1.5 1 3 1 1.5-1 3-1 1.5 1 3 1M2 12c1.5 0 1.5-1 3-1s1.5 1 3 1 1.5-1 3-1 1.5 1 3 1 1.5-1 3-1 1.5 1 3 1M12 4l3 4H9l3-4z';
      case 'family':
        return 'M9 5m-1.5 0a1.5 1.5 0 1 0 3 0 1.5 1.5 0 1 0-3 0M16 5m-1.5 0a1.5 1.5 0 1 0 3 0 1.5 1.5 0 1 0-3 0M6 21v-5H4l2-6h6l2 6h-2v5M13 21v-4h2l1-5h4l1 5h-2v4';
      default:
        return 'M5 12.5 9.5 17 19 7';
    }
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

  private buildMapFocus(place: PlaceDetail): MapEntityMarker | null {
    const entityId = parseInt(place.id, 10);
    const latitude = place.latitude ?? null;
    const longitude = place.longitude ?? null;
    if (isNaN(entityId) || !this.hasUsableCoordinates(latitude, longitude)) return null;

    const entityType = this.getMapEntityType(place);
    return {
      entityType,
      entityId,
      title: place.title,
      subtitle: place.location || null,
      categoryLabel: place.tags[0]?.label ?? (entityType === 'stay' ? 'Stay' : 'Place'),
      imageUrl: place.heroImage || null,
      detailRoute: entityType === 'stay' ? `/stays/${place.id}` : `/places/${place.id}`,
      latitude: latitude as number,
      longitude: longitude as number,
      distance: place.distance || null,
      isSaved: place.isSaved,
    };
  }

  private getMapEntityType(place: PlaceDetail): MapEntityType {
    return place.contentType === 'hotel' ? 'stay' : 'place';
  }

  private getReviewEntityType(place: PlaceDetail): 'place' | 'stay' {
    return place.contentType === 'hotel' ? 'stay' : 'place';
  }

  private targetCollectionId(): number | null {
    const collectionId = Number(this.route.snapshot.queryParamMap.get('collectionId'));
    return Number.isFinite(collectionId) && collectionId > 0 ? collectionId : null;
  }

  private collectionQueryParams(): { collectionId?: number } {
    const collectionId = this.targetCollectionId();
    return collectionId != null ? { collectionId } : {};
  }

  private detailBackUrl(): string | null {
    const value = (globalThis.history?.state ?? {})['detailBackUrl'];
    return typeof value === 'string' && value.trim() ? value : null;
  }

  private detailBackState(): Record<string, unknown> | undefined {
    const value = (globalThis.history?.state ?? {})['detailBackState'];
    return value && typeof value === 'object' && !Array.isArray(value)
      ? value as Record<string, unknown>
      : undefined;
  }

  private calculateCurrentOpenStatus(schedule: { day: string; hours: string }[], fallback: string): string {
    const now = new Date();
    const dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
    const todayName = dayNames[now.getDay()];

    const todayEntry = schedule.find(d =>
      d.day.toLowerCase() === todayName.toLowerCase() ||
      d.day.toLowerCase().startsWith(todayName.toLowerCase().slice(0, 3)),
    );

    if (!todayEntry) return fallback;

    const hoursStr = todayEntry.hours.toLowerCase();
    if (hoursStr.includes('closed')) return 'Closed now';
    if (hoursStr.includes('24') || hoursStr === 'always open') return 'Open now';

    const timeMatch = hoursStr.match(/(\d{1,2}):(\d{2})\s*[-–]\s*(\d{1,2}):(\d{2})/);
    if (!timeMatch) return fallback;

    const openH = parseInt(timeMatch[1], 10);
    const openM = parseInt(timeMatch[2], 10);
    const closeH = parseInt(timeMatch[3], 10);
    const closeM = parseInt(timeMatch[4], 10);

    const currentMins = now.getHours() * 60 + now.getMinutes();
    const openMins = openH * 60 + openM;
    const closeMins = closeH * 60 + closeM;

    const isOpen = closeMins === 0
      ? currentMins >= openMins
      : currentMins >= openMins && currentMins < closeMins;

    return isOpen ? 'Open now' : 'Closed now';
  }
}

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
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { HttpClient } from '@angular/common/http';
import { PlaceDetailService } from '../../services/place-detail.service';
import { ReviewInteractionsService } from '../../../../shared/services/review-interactions.service';
import { ExploreService } from '../../../explore/services/explore.service';
import { ExploreSectionListingService } from '../../../explore/services/explore-section-listing.service';
import type { ReviewDraft } from '../../../../shared/components/review-entry/review-entry.component';
import type { PlaceDetail } from '../../../../core/models/place.model';
import type { MapEntityMarker, MapEntityType } from '../../../../core/services/map-api.service';

@Component({
  selector: 'app-place-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent, ReviewEntryComponent, ImageFallbackDirective, RouterLink],
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

  protected readonly directionsUrl = computed(() => this.buildDirectionsUrl(
    this.place()?.latitude ?? null,
    this.place()?.longitude ?? null,
  ));

  protected readonly hasMapCoordinates = computed(() => this.hasUsableCoordinates(
    this.place()?.latitude ?? null,
    this.place()?.longitude ?? null,
  ));

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
    globalThis.history.back();
  }

  protected goToLogin(): void {
    this.router.navigate(['/profile']);
  }

  protected toggleSaved(): void {
    const id = this.placeId();
    if (id) this.service.toggleSaved(id);
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
    this.router.navigate(['/places', id]);
  }

  protected navigateToEvent(id: string): void {
    this.router.navigate(['/events', id]);
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
}

import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { StepBasicsComponent, BasicsData } from './steps/step-basics/step-basics.component';
import { StepFeaturesComponent, FeaturesData } from './steps/step-features/step-features.component';
import { StepDetailsComponent, DetailsData } from './steps/step-details/step-details.component';
import { StepPreviewComponent } from './steps/step-preview/step-preview.component';
import { StepConfirmationComponent } from './steps/step-confirmation/step-confirmation.component';
import { ContributionStatus, ContributionType, ContributionsService } from '../../services/contributions.service';
import { ExploreService } from '../../../explore/services/explore.service';
import { ExploreSectionListingService } from '../../../explore/services/explore-section-listing.service';
import { PlaceDetailService } from '../../../place-detail/services/place-detail.service';
import { HotelDetailService } from '../../../hotel-detail/services/hotel-detail.service';
import { normalizeContributionAmenityIds } from '../../services/contribution-amenities';

export interface ContributionDraft {
  basics: BasicsData | null;
  features: FeaturesData | null;
  details: DetailsData | null;
}

@Component({
  selector: 'app-submit-contribution',
  standalone: true,
  imports: [CommonModule, StepBasicsComponent, StepFeaturesComponent, StepDetailsComponent, StepPreviewComponent, StepConfirmationComponent],
  templateUrl: './submit-contribution.component.html',
  styleUrl: './submit-contribution.component.css',
})
export class SubmitContributionComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly contributions = inject(ContributionsService);
  private readonly exploreService = inject(ExploreService);
  private readonly listingService = inject(ExploreSectionListingService);
  private readonly placeDetailService = inject(PlaceDetailService);
  private readonly hotelDetailService = inject(HotelDetailService);

  protected readonly entityType = signal('Place');
  protected readonly editId = signal<number | null>(null);
  protected readonly editType = signal<ContributionType | null>(null);
  protected readonly editStatus = signal<ContributionStatus | null>(null);
  protected readonly editPlaceId = signal<number | null>(null);
  protected readonly locationLocked = signal(false);
  protected readonly returnToDetail = signal(false);
  protected readonly detailBackUrl = signal<string | null>(null);
  protected readonly detailBackState = signal<Record<string, unknown> | undefined>(undefined);
  protected readonly reviewNote = signal<string | null>(null);
  protected readonly currentStep = signal(1);
  protected readonly isLoading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly pageTitle = computed(() => {
    if (this.currentStep() === 5) return 'Submitted for Review';
    return this.editId() ? 'Edit Place' : 'Add New Place';
  });
  protected readonly savesDirectly = computed(() => this.editStatus() === 'Approved' && this.editPlaceId() != null);
  protected readonly ownershipProofLocked = computed(() =>
    this.savesDirectly() && !!this.draft().basics?.ownershipProofDocumentUrl);
  protected readonly submitLabel = computed(() => this.savesDirectly() ? 'Save' : 'Submit');
  protected readonly submittingLabel = computed(() => this.savesDirectly() ? 'Saving...' : 'Submitting...');
  protected readonly isAccommodationDraft = computed(() => this.draft().basics?.placeType === 'Stay');
  protected readonly totalVisibleSteps = computed(() => 4);
  protected readonly visibleStepNumber = computed(() => {
    return this.currentStep();
  });
  protected readonly desktopSteps = computed(() => {
    return [
      { number: 1, actualStep: 1, label: 'Basic Info' },
      { number: 2, actualStep: 2, label: 'Amenities' },
      { number: 3, actualStep: 3, label: 'Details' },
      { number: 4, actualStep: 4, label: 'Review' },
    ];
  });

  protected readonly draft = signal<ContributionDraft>({
    basics: null,
    features: { tags: [], amenities: [] },
    details: { description: '', phone: '', contactEmail: '', website: '', openingHoursEnabled: false, openingHours: '', bookingPartners: [] },
  });

  ngOnInit(): void {
    const navState = (globalThis.history?.state ?? {}) as Record<string, unknown>;
    if (navState['locationLocked'] === true) {
      this.locationLocked.set(true);
    }
    if (navState['returnToDetail'] === true) {
      this.returnToDetail.set(true);
    }
    if (typeof navState['detailBackUrl'] === 'string' && navState['detailBackUrl'].trim()) {
      this.detailBackUrl.set(navState['detailBackUrl']);
    }
    if (navState['detailBackState'] && typeof navState['detailBackState'] === 'object' && !Array.isArray(navState['detailBackState'])) {
      this.detailBackState.set(navState['detailBackState'] as Record<string, unknown>);
    }

    // Support "edit owned place" mode when navigated from place-detail without a contribution id
    const updateForPlaceId = typeof navState['updateForPlaceId'] === 'number' ? navState['updateForPlaceId'] as number : null;
    if (updateForPlaceId != null) {
      this.editType.set('UpdatePlace');
      this.editStatus.set('Approved');
      this.editPlaceId.set(updateForPlaceId);
      this.locationLocked.set(true);
      this.isLoading.set(true);
      this.loadError.set(null);
      this.loadPlaceDataForEdit(updateForPlaceId, navState);
      return;
    }

    const rawId = this.route.snapshot.paramMap.get('id');
    const id = rawId ? Number(rawId) : null;

    if (!id) return;

    this.editId.set(id);
    this.isLoading.set(true);
    this.loadError.set(null);

    this.contributions.getDetail(id).subscribe({
      next: (detail) => {
        // When editing an approved ClaimOwnership, treat it as editing the published place
        if (detail.type === 'ClaimOwnership' && detail.status === 'Approved' && detail.publishedPlaceId != null) {
          this.draft.set(this.contributions.detailToDraft(detail));
          this.entityType.set(detail.entityType);
          this.editType.set('UpdatePlace');
          this.editStatus.set('Approved');
          this.editPlaceId.set(detail.publishedPlaceId);
          this.reviewNote.set(detail.reviewNote ?? null);
          this.locationLocked.set(true);
          this.isLoading.set(false);
          return;
        }

        const draft = this.contributions.detailToDraft(detail);

        this.draft.set(draft);
        this.entityType.set(detail.entityType);
        this.editType.set(detail.type);
        this.editStatus.set(detail.status);
        this.editPlaceId.set(detail.publishedPlaceId);
        this.reviewNote.set(detail.reviewNote ?? null);
        if (this.shouldLockLocationForEdit(detail.status, detail.publishedPlaceId)) {
          this.locationLocked.set(true);
        }
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set("We couldn't load this contribution. Please try again.");
        this.isLoading.set(false);
      },
    });
  }

  private loadPlaceDataForEdit(placeId: number, navState: Record<string, unknown>): void {
    this.http.get<{
      id: number;
      title?: string;
      about?: string;
      heroImage?: string;
      latitude?: number | null;
      longitude?: number | null;
      locationId?: number | null;
      locationName?: string | null;
      website?: string | null;
      openingHours?: string | null;
      contactEmail?: string | null;
      phoneNumber?: string | null;
      category?: string | null;
      images?: { url?: string; imageRole?: string; imagePosition?: string; imageSize?: string }[] | null;
      tags?: { label: string }[] | null;
      amenities?: { label: string }[] | null;
    }>(`/api/places/${placeId}`).subscribe({
      next: (place) => {
        const images = place.images ?? [];
        const cover = images.find(i => i.imageRole === 'cover') ?? null;
        const card = images.find(i => i.imageRole === 'card') ?? null;
        const gallery = images.filter(i => i.imageRole === 'gallery' && !!i.url).map(i => i.url as string);
        const coverUrl = cover?.url
          || place.heroImage
          || (typeof navState['placeCoverImageUrl'] === 'string' ? navState['placeCoverImageUrl'] : '');
        const cardUrl = card?.url || coverUrl;
        const galleryUrls = gallery.filter(url => url !== coverUrl && url !== cardUrl);
        const placeType = toTitleCase(place.category ?? '') || 'Place';

        this.draft.update(d => ({
          ...d,
          basics: {
            name: place.title ?? (typeof navState['placeTitle'] === 'string' ? navState['placeTitle'] : ''),
            placeType,
            placeTypeId: null,
            stayType: 'Hotel',
            address: '',
            city: place.locationName ?? '',
            region: '',
            country: '',
            locationId: place.locationId ?? null,
            imageUrl: coverUrl,
            cardImageUrl: cardUrl,
            cardImageFile: null,
            coverImageFile: null,
            galleryImageUrls: galleryUrls,
            galleryImageFiles: galleryUrls.map(() => null),
            ownershipProofFile: null,
            ownershipProofDocumentUrl: null,
            ownershipProofOriginalFileName: null,
            lat: place.latitude ?? (typeof navState['placeLat'] === 'number' ? navState['placeLat'] : null),
            lng: place.longitude ?? (typeof navState['placeLng'] === 'number' ? navState['placeLng'] : null),
            imgBgPosition: cover?.imagePosition || '50% 50%',
            imgBgSize: cover?.imageSize || 'cover',
            cardImgBgPosition: card?.imagePosition || '50% 50%',
            cardImgBgSize: card?.imageSize || 'cover',
          },
          features: {
            tags: (place.tags ?? []).map(tag => tag.label).filter(Boolean),
            amenities: normalizeContributionAmenityIds((place.amenities ?? []).map(amenity => amenity.label)),
          },
          details: {
            description: place.about ?? (typeof navState['placeAbout'] === 'string' ? navState['placeAbout'] : ''),
            openingHoursEnabled: !!place.openingHours?.trim(),
            openingHours: place.openingHours ?? '',
            phone: place.phoneNumber ?? '',
            contactEmail: place.contactEmail ?? '',
            website: place.website ?? '',
            bookingPartners: [],
          },
        }));
        this.isLoading.set(false);
      },
      error: () => {
        // Fall back to empty form if place data can't be loaded
        this.isLoading.set(false);
      },
    });
  }

  protected onBasicsSaved(data: BasicsData): void {
    this.draft.update(d => ({
      ...d,
      basics: data,
      features: d.features ?? { tags: [], amenities: [] },
    }));
    this.currentStep.set(2);
  }

  protected onFeaturesSaved(data: FeaturesData): void {
    this.draft.update(d => ({ ...d, features: data }));
    this.currentStep.set(3);
  }

  protected onDetailsSaved(data: DetailsData): void {
    this.draft.update(d => ({ ...d, details: data }));
    this.currentStep.set(4);
  }

  protected onSubmit(): void {
    const basics = this.draft().basics;
    if (!basics) {
      this.submitError.set('Complete the place basics before submitting.');
      this.currentStep.set(1);
      return;
    }

    if (!this.locationLocked() && !hasValidContributionLocation(basics)) {
      this.submitError.set('Please choose a valid location before submitting.');
      this.currentStep.set(1);
      return;
    }

    this.isSubmitting.set(true);
    this.submitError.set(null);

    const editId = this.editId();
    const shouldUpdatePublishedPlace = this.savesDirectly();

    if (shouldUpdatePublishedPlace && this.editPlaceId() != null) {
      const placeId = this.editPlaceId() as number;
      this.contributions.ownerUpdatePlace(placeId, this.draft()).subscribe({
        next: () => {
          this.isSubmitting.set(false);
          const detailId = String(placeId);
          this.exploreService.invalidate();
          this.listingService.invalidate();
          this.placeDetailService.invalidate(detailId);
          this.hotelDetailService.invalidate(detailId);
          if (this.returnToDetail()) {
            void this.router.navigate(
              [this.isAccommodationDraft() ? '/stays' : '/places', detailId],
              { replaceUrl: true, state: { detailBackUrl: this.detailBackUrl(), detailBackState: this.detailBackState() } },
            );
            return;
          }

          void this.router.navigate(['/contributions']);
        },
        error: (err) => {
          this.submitError.set(err?.error?.message ?? 'Something went wrong. Please try again.');
          this.isSubmitting.set(false);
        },
      });
      return;
    }

    let request;
    if (editId) {
      const editOptions = {
        type: this.editType() ?? 'CreatePlace',
        placeId: this.editPlaceId(),
      };
      request = this.contributions.updatePlace(editId, this.draft(), editOptions);
    } else if (this.editType() === 'UpdatePlace' && this.editPlaceId() != null) {
      request = this.contributions.createPlace(this.draft(), { type: 'UpdatePlace', placeId: this.editPlaceId() });
    } else {
      request = this.contributions.createPlace(this.draft());
    }

    request.subscribe({
      next: (detail) => {
        this.editId.set(detail.id);
        this.editStatus.set(detail.status);
        this.reviewNote.set(detail.reviewNote ?? null);
        this.isSubmitting.set(false);
        this.currentStep.set(5);
      },
      error: (err) => {
        this.submitError.set(err?.error?.message ?? 'Something went wrong. Please try again.');
        this.isSubmitting.set(false);
      },
    });
  }

  protected prevStep(): void {
    const step = this.currentStep();
    if (step <= 1) return;
    this.currentStep.update(s => s - 1);
  }

  protected handleBack(): void {
    if (this.currentStep() === 1) {
      const detailId = this.editPlaceId();
      if (this.returnToDetail() && detailId != null) {
        this.router.navigate(
          [this.isAccommodationDraft() ? '/stays' : '/places', String(detailId)],
          { replaceUrl: true, state: { detailBackUrl: this.detailBackUrl(), detailBackState: this.detailBackState() } },
        );
        return;
      }

      this.router.navigate(['/contributions']);
    }
    else this.prevStep();
  }

  protected resetAndStart(): void {
    this.draft.set({
      basics: null,
      features: { tags: [], amenities: [] },
      details: { description: '', phone: '', contactEmail: '', website: '', openingHoursEnabled: false, openingHours: '', bookingPartners: [] },
    });
    this.editId.set(null);
    this.entityType.set('Place');
    this.reviewNote.set(null);
    this.currentStep.set(1);
  }

  protected onFeaturesChanged(data: FeaturesData): void {
    this.draft.update(d => ({ ...d, features: data }));
  }

  protected onDetailsChanged(data: DetailsData): void {
    this.draft.update(d => ({ ...d, details: data }));
  }

  protected reloadEdit(): void {
    const id = this.editId();
    if (!id) return;

    this.isLoading.set(true);
    this.loadError.set(null);

    this.contributions.getDetail(id).subscribe({
      next: (detail) => {
        this.draft.set(this.contributions.detailToDraft(detail));
        this.entityType.set(detail.entityType);
        this.editType.set(detail.type);
        this.editStatus.set(detail.status);
        this.editPlaceId.set(detail.publishedPlaceId);
        this.reviewNote.set(detail.reviewNote ?? null);
        if (this.shouldLockLocationForEdit(detail.status, detail.publishedPlaceId)) {
          this.locationLocked.set(true);
        }
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set("We couldn't load this contribution. Please try again.");
        this.isLoading.set(false);
      },
    });
  }

  private shouldLockLocationForEdit(status: ContributionStatus | null, publishedPlaceId: number | null): boolean {
    return status === 'Approved' && publishedPlaceId != null;
  }
}

function hasValidContributionLocation(basics: BasicsData): boolean {
  return typeof basics.lat === 'number' &&
    typeof basics.lng === 'number' &&
    Number.isFinite(basics.lat) &&
    Number.isFinite(basics.lng) &&
    basics.lat >= -90 &&
    basics.lat <= 90 &&
    basics.lng >= -180 &&
    basics.lng <= 180 &&
    (basics.lat !== 0 || basics.lng !== 0);
}

function toTitleCase(value: string): string {
  return value
    .trim()
    .split(/[\s_-]+/)
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase() + part.slice(1).toLowerCase())
    .join(' ');
}

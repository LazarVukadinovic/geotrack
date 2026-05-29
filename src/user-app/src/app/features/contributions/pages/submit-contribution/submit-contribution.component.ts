import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
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
    details: { description: '', phone: '', contactEmail: '', website: '', bookingPartners: [] },
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

    const rawId = this.route.snapshot.paramMap.get('id');
    const id = rawId ? Number(rawId) : null;

    if (!id) return;

    this.editId.set(id);
    this.isLoading.set(true);
    this.loadError.set(null);

    this.contributions.getDetail(id).subscribe({
      next: (detail) => {
        const draft = this.contributions.detailToDraft(detail);

        this.draft.set(draft);
        this.entityType.set(detail.entityType);
        this.editType.set(detail.type);
        this.editStatus.set(detail.status);
        this.editPlaceId.set(detail.publishedPlaceId);
        if (detail.type === 'ClaimOwnership' && detail.status === 'Approved') {
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
    const request = editId
      ? this.contributions.updatePlace(
        editId,
        this.draft(),
        shouldUpdatePublishedPlace ? { type: 'UpdatePlace', placeId: this.editPlaceId() } : undefined,
      )
      : this.contributions.createPlace(this.draft());

    request.subscribe({
      next: (detail) => {
        this.editId.set(detail.id);
        this.isSubmitting.set(false);
        if (shouldUpdatePublishedPlace && detail.publishedPlaceId != null) {
          const detailId = String(detail.publishedPlaceId);
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
          return;
        }

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
      details: { description: '', phone: '', contactEmail: '', website: '', bookingPartners: [] },
    });
    this.editId.set(null);
    this.entityType.set('Place');
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
        if (detail.type === 'ClaimOwnership' && detail.status === 'Approved') {
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

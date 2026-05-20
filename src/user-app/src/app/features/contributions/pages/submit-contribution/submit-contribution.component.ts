import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { StepBasicsComponent, BasicsData } from './steps/step-basics/step-basics.component';
import { StepFeaturesComponent, FeaturesData } from './steps/step-features/step-features.component';
import { StepDetailsComponent, DetailsData } from './steps/step-details/step-details.component';
import { StepPreviewComponent } from './steps/step-preview/step-preview.component';
import { StepConfirmationComponent } from './steps/step-confirmation/step-confirmation.component';
import { ContributionsService } from '../../services/contributions.service';

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

  protected readonly entityType = signal('Place');
  protected readonly editId = signal<number | null>(null);
  protected readonly currentStep = signal(1);
  protected readonly isLoading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly isSubmitting = signal(false);
  protected readonly submitError = signal<string | null>(null);

  protected readonly pageTitle = computed(() => {
    if (this.currentStep() === 5) return 'Submitted for Review';
    return this.editId() ? 'Edit Place' : 'Add New Place';
  });
  protected readonly isAccommodationDraft = computed(() => this.draft().basics?.placeType === 'Stay');
  protected readonly totalVisibleSteps = computed(() => this.isAccommodationDraft() ? 4 : 3);
  protected readonly visibleStepNumber = computed(() => {
    const step = this.currentStep();
    if (this.isAccommodationDraft() || step <= 1 || step >= 5) return step;
    if (step === 3) return 2;
    if (step === 4) return 3;
    return step;
  });
  protected readonly desktopSteps = computed(() => {
    if (this.isAccommodationDraft()) {
      return [
        { number: 1, actualStep: 1, label: 'Basic Info' },
        { number: 2, actualStep: 2, label: 'Amenities' },
        { number: 3, actualStep: 3, label: 'Details' },
        { number: 4, actualStep: 4, label: 'Review' },
      ];
    }

    return [
      { number: 1, actualStep: 1, label: 'Basic Info' },
      { number: 2, actualStep: 3, label: 'Details' },
      { number: 3, actualStep: 4, label: 'Review' },
    ];
  });

  protected readonly draft = signal<ContributionDraft>({
    basics: null,
    features: { tags: [], amenities: [] },
    details: { description: '', phone: '', contactEmail: '', website: '', bookingPartners: [] },
  });

  ngOnInit(): void {
    const rawId = this.route.snapshot.paramMap.get('id');
    const id = rawId ? Number(rawId) : null;

    if (!id) return;

    this.editId.set(id);
    this.isLoading.set(true);
    this.loadError.set(null);

    this.contributions.getDetail(id).subscribe({
      next: (detail) => {
        this.draft.set(this.contributions.detailToDraft(detail));
        this.entityType.set(detail.entityType);
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set("We couldn't load this contribution. Please try again.");
        this.isLoading.set(false);
      },
    });
  }

  protected onBasicsSaved(data: BasicsData): void {
    const isStay = data.placeType === 'Stay';
    this.draft.update(d => ({
      ...d,
      basics: data,
      features: isStay ? (d.features ?? { tags: [], amenities: [] }) : { tags: d.features?.tags ?? [], amenities: [] },
    }));
    this.currentStep.set(isStay ? 2 : 3);
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

    if (!hasValidContributionLocation(basics)) {
      this.submitError.set('Please choose a valid location before submitting.');
      this.currentStep.set(1);
      return;
    }

    this.isSubmitting.set(true);
    this.submitError.set(null);

    const editId = this.editId();
    const request = editId
      ? this.contributions.updatePlace(editId, this.draft())
      : this.contributions.createPlace(this.draft());

    request.subscribe({
      next: (detail) => {
        this.editId.set(detail.id);
        this.currentStep.set(5);
        this.isSubmitting.set(false);
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
    if (!this.isAccommodationDraft() && step === 3) {
      this.currentStep.set(1);
      return;
    }
    if (!this.isAccommodationDraft() && step === 4) {
      this.currentStep.set(3);
      return;
    }

    this.currentStep.update(s => s - 1);
  }

  protected handleBack(): void {
    if (this.currentStep() === 1) this.router.navigate(['/contributions']);
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

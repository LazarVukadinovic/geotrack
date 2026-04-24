import { Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { StepBasicsComponent, BasicsData } from './steps/step-basics/step-basics.component';
import { StepFeaturesComponent, FeaturesData } from './steps/step-features/step-features.component';
import { StepDetailsComponent, DetailsData } from './steps/step-details/step-details.component';
import { StepPreviewComponent } from './steps/step-preview/step-preview.component';
import { StepConfirmationComponent } from './steps/step-confirmation/step-confirmation.component';

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
})
export class SubmitContributionComponent {
  private router = inject(Router);

  entityType = 'Stay';
  editId: string | null = null;
  currentStep = signal(1);

  draft = signal<ContributionDraft>({ basics: null, features: { tags: [], amenities: [] }, details: { description: '', phone: '', website: '', bookingPartners: [] } });

  constructor() {
    const nav = this.router.getCurrentNavigation();
    const state = (nav?.extras?.state ?? history.state) as any;
    if (state?.entityType) this.entityType = state.entityType;
    if (state?.editId) this.editId = state.editId;
    if (state?.editDraft) this.draft.set(state.editDraft);
  }

  onBasicsSaved(data: BasicsData): void {
    this.draft.update(d => ({ ...d, basics: data }));
    this.currentStep.set(2);
  }

  onFeaturesSaved(data: FeaturesData): void {
    this.draft.update(d => ({ ...d, features: data }));
    this.currentStep.set(3);
  }

  onDetailsSaved(data: DetailsData): void {
    this.draft.update(d => ({ ...d, details: data }));
    this.currentStep.set(4);
  }
  isSubmitting = signal(false);
  submitError = signal<string | null>(null);
  onSubmit(): void {
    this.isSubmitting.set(true);
    this.submitError.set(null);

    // Simulacija dok API nije spreman
    setTimeout(() => {
      this.currentStep.set(5);
      this.isSubmitting.set(false);
    }, 800);

    // Kada API bude spreman:
    // const payload = this.draft();
    // const url = this.editId ? `/api/contributions/${this.editId}` : '/api/contributions';
    // const method = this.editId ? 'PUT' : 'POST';
    // fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
    //   .then(r => { if (!r.ok) throw new Error('Failed'); return r.json(); })
    //   .then(() => this.currentStep.set(5))
    //   .catch(() => this.submitError.set('Something went wrong. Please try again.'))
    //   .finally(() => this.isSubmitting.set(false));
  }

  prevStep(): void {
    if (this.currentStep() > 1) this.currentStep.update(s => s - 1);
  }

  handleBack(): void {
    if (this.currentStep() === 1) this.router.navigate(['/contributions']);
    else this.prevStep();
  }
  resetAndStart(): void {
    this.draft.set({ basics: null, features: null, details: null });
    this.editId = null;
    this.currentStep.set(1);
  }
  onFeaturesChanged(data: FeaturesData): void {
    this.draft.update(d => ({ ...d, features: data }));
  }
  onDetailsChanged(data: DetailsData): void {
    this.draft.update(d => ({ ...d, details: data }));
  }
}
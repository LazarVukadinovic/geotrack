import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { UserService } from '../../../core/services/user.service';

export interface ReviewDraft {
  rating: number;
  body: string;
}

let nextReviewBodyId = 0;

@Component({
  selector: 'app-review-entry',
  standalone: true,
  templateUrl: './review-entry.component.html',
  styleUrl: './review-entry.component.scss',
})
export class ReviewEntryComponent {
  readonly isLoggedIn  = input.required<boolean>();
  readonly entityName  = input.required<string>();

  /**
   * Factory for the submit HTTP call. Returning an `Observable` lets this
   * presentational component own popup / form state while the parent page
   * owns endpoint shape and post-success refresh.
   */
  readonly submitRequest = input.required<(draft: ReviewDraft) => Observable<unknown>>();

  /** Emitted when the logged-out "Log In" button is clicked. */
  readonly loginRequested   = output<void>();
  /** Emitted only after a successful submit. */
  readonly reviewSubmitted  = output<ReviewDraft>();
  protected readonly avatarUrl = inject(UserService).avatarUrl;

  // ─── Popup state ────────────────────────────────────────────────────────────
  protected readonly popupOpen   = signal(false);
  protected readonly draftRating = signal(0);
  protected readonly hoverRating = signal(0);
  protected readonly draftBody   = signal('');

  protected readonly isSubmitting = signal(false);
  protected readonly submitError  = signal('');
  protected readonly reviewBodyId = `review-body-${++nextReviewBodyId}`;

  protected readonly effectiveRating = computed(() =>
    this.hoverRating() || this.draftRating(),
  );

  protected readonly canSubmit = computed(() =>
    this.draftRating() > 0 && !this.isSubmitting(),
  );

  protected starFilled(index: number): boolean {
    return index <= this.effectiveRating();
  }

  protected openPopup(): void {
    this.popupOpen.set(true);
  }

  protected closePopup(): void {
    if (this.isSubmitting()) return;
    this.popupOpen.set(false);
    this.draftRating.set(0);
    this.hoverRating.set(0);
    this.draftBody.set('');
    this.submitError.set('');
  }

  protected onStarClick(n: number): void {
    this.draftRating.set(n);
  }

  protected onStarEnter(n: number): void {
    this.hoverRating.set(n);
  }

  protected onStarLeave(): void {
    this.hoverRating.set(0);
  }

  protected onBodyInput(event: Event): void {
    this.draftBody.set((event.target as HTMLTextAreaElement).value);
  }

  protected onSubmit(): void {
    if (!this.canSubmit()) return;
    const draft: ReviewDraft = {
      rating: this.draftRating(),
      body:   this.draftBody(),
    };
    this.submitError.set('');
    this.isSubmitting.set(true);
    this.submitRequest()(draft).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.reviewSubmitted.emit(draft);
        this.popupOpen.set(false);
        this.draftRating.set(0);
        this.hoverRating.set(0);
        this.draftBody.set('');
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.submitError.set(err?.error?.message || 'Could not submit your review. Please try again.');
      },
    });
  }
}

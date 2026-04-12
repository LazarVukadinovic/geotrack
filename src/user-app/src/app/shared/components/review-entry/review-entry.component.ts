import { Component, computed, input, output, signal } from '@angular/core';

export interface ReviewDraft {
  rating: number;
  body: string;
}

@Component({
  selector: 'app-review-entry',
  standalone: true,
  templateUrl: './review-entry.component.html',
  styleUrl: './review-entry.component.scss',
})
export class ReviewEntryComponent {
  readonly isLoggedIn  = input.required<boolean>();
  readonly entityName  = input.required<string>();

  /** Emitted when the logged-out "Log In" button is clicked. */
  readonly loginRequested   = output<void>();
  /**
   * Emitted when the user submits a review.
   * Currently frontend-only — wire to your review API when ready.
   */
  readonly reviewSubmitted  = output<ReviewDraft>();

  // ─── Popup state ────────────────────────────────────────────────────────────
  protected readonly popupOpen   = signal(false);
  protected readonly draftRating = signal(0);
  protected readonly hoverRating = signal(0);
  protected readonly draftBody   = signal('');

  protected readonly effectiveRating = computed(() =>
    this.hoverRating() || this.draftRating(),
  );

  protected starFilled(index: number): boolean {
    return index <= this.effectiveRating();
  }

  protected openPopup(): void {
    this.popupOpen.set(true);
  }

  protected closePopup(): void {
    this.popupOpen.set(false);
    this.draftRating.set(0);
    this.hoverRating.set(0);
    this.draftBody.set('');
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
    this.reviewSubmitted.emit({
      rating: this.draftRating(),
      body:   this.draftBody(),
    });
    this.closePopup();
  }
}

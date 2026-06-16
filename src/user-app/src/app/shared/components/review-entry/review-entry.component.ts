import { Component, computed, inject, input, output, signal, NgZone, OnDestroy } from '@angular/core';
import { Observable } from 'rxjs';
import { UserService } from '../../../core/services/user.service';
import { UserAvatarComponent } from '../user-avatar/user-avatar.component';

export interface ReviewDraft {
  rating: number;
  body: string;
}

let nextReviewBodyId = 0;

const DISMISS_THRESHOLD = 120;

@Component({
  selector: 'app-review-entry',
  standalone: true,
  imports: [UserAvatarComponent],
  templateUrl: './review-entry.component.html',
  styleUrl: './review-entry.component.scss',
})
export class ReviewEntryComponent implements OnDestroy {
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
  private readonly userService = inject(UserService);
  protected readonly avatarUrl = this.userService.avatarUrl;
  protected readonly fullName = this.userService.fullName;
  protected readonly email = this.userService.email;
  private  readonly ngZone    = inject(NgZone);

  // ─── Popup state ────────────────────────────────────────────────────────────
  protected readonly popupOpen   = signal(false);
  protected readonly draftRating = signal(0);
  protected readonly hoverRating = signal(0);
  protected readonly draftBody   = signal('');

  protected readonly isSubmitting = signal(false);
  protected readonly submitError  = signal('');
  protected readonly reviewBodyId = `review-body-${++nextReviewBodyId}`;

  // ─── Swipe-to-dismiss ───────────────────────────────────────────────────────
  protected readonly dragOffset = signal(0);
  protected readonly isDragging = signal(false);
  private touchStartY = 0;
  private readonly boundMove = this.onDocTouchMove.bind(this);
  private readonly boundEnd  = this.onDocTouchEnd.bind(this);

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

  ngOnDestroy(): void {
    this.detachDocListeners();
  }

  protected closePopup(): void {
    if (this.isSubmitting()) return;
    this.dragOffset.set(0);
    this.popupOpen.set(false);
    this.draftRating.set(0);
    this.hoverRating.set(0);
    this.draftBody.set('');
    this.submitError.set('');
  }

  protected onModalTouchStart(e: TouchEvent): void {
    this.touchStartY = e.touches[0].clientY;
    this.isDragging.set(true);
    this.ngZone.runOutsideAngular(() => {
      document.addEventListener('touchmove', this.boundMove, { passive: false });
      document.addEventListener('touchend',  this.boundEnd,  { passive: false });
    });
  }

  private onDocTouchMove(e: TouchEvent): void {
    const delta = e.touches[0].clientY - this.touchStartY;
    if (delta <= 0) return;
    e.preventDefault();
    this.ngZone.run(() => this.dragOffset.set(delta));
  }

  private onDocTouchEnd(): void {
    this.detachDocListeners();
    this.ngZone.run(() => {
      this.isDragging.set(false);
      if (this.dragOffset() >= DISMISS_THRESHOLD) {
        this.closePopup();
      } else {
        this.dragOffset.set(0);
      }
    });
  }

  private detachDocListeners(): void {
    document.removeEventListener('touchmove', this.boundMove);
    document.removeEventListener('touchend',  this.boundEnd);
  }

  protected onStarClick(n: number): void {
    this.draftRating.set(n);
    this.hoverRating.set(0);
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
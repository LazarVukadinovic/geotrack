import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { Observable, throwError } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { ReviewEntryComponent } from '../../../../shared/components/review-entry/review-entry.component';
import { HttpClient } from '@angular/common/http';
import { PlaceDetailService } from '../../services/place-detail.service';
import type { ReviewDraft } from '../../../../shared/components/review-entry/review-entry.component';

@Component({
  selector: 'app-place-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent, ReviewEntryComponent],
  templateUrl: './place-detail-page.component.html',
  styleUrl: './place-detail-page.component.scss',
})
export class PlaceDetailPageComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly location = inject(Location);
  private readonly service  = inject(PlaceDetailService);
  private readonly auth     = inject(AuthService);
  private readonly http     = inject(HttpClient);

  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());

  protected readonly placeId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );

  /** Re-derives whenever placeId or saved-state changes in the service. */
  protected readonly place = computed(() =>
    this.service.getPlaceDetailPageData(this.placeId()),
  );

  /** 0 = transparent top bar, 1 = full navy + visible title. Driven by pd__scroll. */
  protected readonly scrollProgress = signal(0);

  private readonly pdScroll = viewChild<ElementRef<HTMLElement>>('pdScroll');

  constructor() {
    // When the route param changes (Similar Places card clicked), the same
    // component instance is reused by Angular's RouteReuseStrategy. The scroll
    // container keeps its current scrollTop unless we explicitly reset it.
    effect(() => {
      const id = this.placeId(); // subscribe — runs on every id change
      this.scrollProgress.set(0);
      const el = this.pdScroll()?.nativeElement;
      if (el) el.scrollTop = 0;
      if (id) this.service.loadPlaceDetail(id);
    });
  }

  /** Local saved state for similar-place cards (transient, not persisted). */
  private readonly _similarSaved = signal<Record<string, boolean>>({});

  /** Called by (scroll) on .pd__scroll — the page's own scroll container. */
  protected onScroll(event: Event): void {
    const scrollTop = (event.currentTarget as Element).scrollTop;
    this.scrollProgress.set(Math.min(scrollTop / 200, 1));
  }

  protected isSimilarSaved(id: string): boolean {
    return this._similarSaved()[id] ?? false;
  }

  protected toggleSimilarSaved(event: Event, id: string): void {
    event.stopPropagation();
    this._similarSaved.update((m) => ({ ...m, [id]: !m[id] }));
  }

  protected goBack(): void {
    this.location.back();
  }

  protected goToLogin(): void {
    this.router.navigate(['/profile']);
  }

  protected toggleSaved(): void {
    const id = this.placeId();
    if (id) this.service.toggleSaved(id);
  }

  protected navigateToSimilar(id: string): void {
    this.router.navigate(['/places', id]);
  }

  protected onHeroImgError(event: Event): void {
    (event.target as HTMLImageElement).src = '/images/explore-image-fallback.svg';
  }

  protected onGetDirections(): void {
    /* future: open maps / directions integration */
  }

  protected onViewOnMap(): void {
    /* future: navigate to /map with this place highlighted */
  }

  protected readonly submitReview = (draft: ReviewDraft): Observable<unknown> => {
    const numId = parseInt(this.placeId(), 10);
    if (isNaN(numId)) return throwError(() => new Error('Invalid place id.'));
    return this.http.post(`/api/reviews/places/${numId}`, {
      rating:  draft.rating,
      comment: draft.body,
    });
  };

  protected onReviewSubmitted(_draft: ReviewDraft): void {
    const id = this.placeId();
    if (id) this.service.loadPlaceDetail(id);
  }

  protected onSeeAllReviews(): void {
    /* future: navigate to full reviews page */
  }
}

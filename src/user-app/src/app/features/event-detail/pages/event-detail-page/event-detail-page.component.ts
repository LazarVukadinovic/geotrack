import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { EventDetailService } from '../../services/event-detail.service';

@Component({
  selector: 'app-event-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent],
  templateUrl: './event-detail-page.component.html',
  styleUrl: './event-detail-page.component.scss',
})
export class EventDetailPageComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly location = inject(Location);
  private readonly service  = inject(EventDetailService);

  protected readonly eventId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );

  protected readonly event = computed(() =>
    this.service.getEventDetailPageData(this.eventId()),
  );

  /** 0 = transparent top bar, 1 = full navy + visible title. Driven by ed__scroll. */
  protected readonly scrollProgress = signal(0);

  private readonly edScroll = viewChild<ElementRef<HTMLElement>>('edScroll');

  constructor() {
    // Reset scroll when the route param changes (same component instance reused).
    effect(() => {
      this.eventId();
      this.scrollProgress.set(0);
      const el = this.edScroll()?.nativeElement;
      if (el) el.scrollTop = 0;
    });
  }

  /** Local saved state for similar-event cards (transient, not persisted). */
  private readonly _similarSaved = signal<Record<string, boolean>>({});

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

  protected toggleSaved(): void {
    const id = this.eventId();
    if (id) this.service.toggleSaved(id);
  }

  protected navigateToSimilar(id: string): void {
    this.router.navigate(['/events', id]);
  }

  protected onHeroImgError(event: Event): void {
    (event.target as HTMLImageElement).src = '/images/explore-image-fallback.svg';
  }

  protected onGetDirections(): void {
    /* future: open maps / directions integration */
  }

  protected onViewOnMap(): void {
    /* future: navigate to /map with this event highlighted */
  }
}

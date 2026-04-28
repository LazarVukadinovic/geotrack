import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { LocationService } from '../../../../core/services/location.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { EventDetailService } from '../../services/event-detail.service';

@Component({
  selector: 'app-event-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent, ImageFallbackDirective],
  templateUrl: './event-detail-page.component.html',
  styleUrl: './event-detail-page.component.scss',
})
export class EventDetailPageComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly location = inject(Location);
  private readonly locationSvc = inject(LocationService);
  private readonly analytics = inject(AnalyticsService);
  private readonly service  = inject(EventDetailService);

  protected readonly eventId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );

  protected readonly event = computed(() =>
    this.service.getEventDetailPageData(this.eventId()),
  );
  protected readonly loadState = computed(() =>
    this.service.getLoadState(this.eventId()),
  );

  /** 0 = transparent top bar, 1 = full navy + visible title. Driven by ed__scroll. */
  protected readonly scrollProgress = signal(0);

  protected readonly directionsUrl = computed(() => this.buildDirectionsUrl(
    this.event()?.latitude ?? null,
    this.event()?.longitude ?? null,
  ));

  protected readonly mapUrl = computed(() => this.buildMapUrl(
    this.event()?.latitude ?? null,
    this.event()?.longitude ?? null,
  ));

  private readonly locationRequestKey = computed(() => {
    const loc = this.locationSvc.location();
    if (!loc) return 'none';
    return `${loc.latitude}:${loc.longitude}:${loc.source}`;
  });

  private readonly edScroll = viewChild<ElementRef<HTMLElement>>('edScroll');

  constructor() {
    effect(() => {
      this.eventId();
      this.scrollProgress.set(0);
      const el = this.edScroll()?.nativeElement;
      if (el) el.scrollTop = 0;
    });

    effect(() => {
      const id = this.eventId();
      this.locationRequestKey();
      if (id) this.service.loadEventDetail(id);
    });
  }

  protected onScroll(event: Event): void {
    const scrollTop = (event.currentTarget as Element).scrollTop;
    this.scrollProgress.set(Math.min(scrollTop / 200, 1));
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

  protected onDirectionsClick(): void {
    const numId = parseInt(this.eventId(), 10);
    if (isNaN(numId)) return;
    this.analytics.track('directions_started', {
      entityType: 'event',
      entityId: numId,
      metadata: { provider: 'google_maps', context: 'event_detail' },
    });
  }

  private buildDirectionsUrl(latitude: number | null, longitude: number | null): string | null {
    if (!this.hasUsableCoordinates(latitude, longitude)) return null;
    return `https://www.google.com/maps/dir/?api=1&destination=${latitude},${longitude}`;
  }

  private buildMapUrl(latitude: number | null, longitude: number | null): string | null {
    if (!this.hasUsableCoordinates(latitude, longitude)) return null;
    return `https://www.google.com/maps/search/?api=1&query=${latitude},${longitude}`;
  }

  private hasUsableCoordinates(latitude: number | null, longitude: number | null): boolean {
    return latitude != null &&
      longitude != null &&
      Number.isFinite(latitude) &&
      Number.isFinite(longitude) &&
      (latitude !== 0 || longitude !== 0);
  }
}

import { Component, computed, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs/operators';
import { Observable, throwError } from 'rxjs';
import { AuthService } from '../../../../core/services/auth.service';
import { LocationService } from '../../../../core/services/location.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { SectionHeaderComponent } from '../../../../shared/components/section-header/section-header.component';
import { ReviewEntryComponent } from '../../../../shared/components/review-entry/review-entry.component';
import { ImageFallbackDirective } from '../../../../shared/directives/image-fallback.directive';
import { HttpClient } from '@angular/common/http';
import { HotelDetailService } from '../../services/hotel-detail.service';
import type { ReviewDraft } from '../../../../shared/components/review-entry/review-entry.component';
import type { HotelAmenityIcon, HotelBookingPartner } from '../../models/hotel-detail.model';

@Component({
  selector: 'app-hotel-detail-page',
  standalone: true,
  imports: [SectionHeaderComponent, ReviewEntryComponent, ImageFallbackDirective],
  templateUrl: './hotel-detail-page.component.html',
  styleUrl: './hotel-detail-page.component.scss',
})
export class HotelDetailPageComponent {
  private readonly route    = inject(ActivatedRoute);
  private readonly router   = inject(Router);
  private readonly location = inject(Location);
  private readonly service  = inject(HotelDetailService);
  private readonly auth     = inject(AuthService);
  private readonly locationSvc = inject(LocationService);
  private readonly analytics = inject(AnalyticsService);
  private readonly http     = inject(HttpClient);

  protected readonly isLoggedIn = computed(() => this.auth.isLoggedIn());

  protected readonly hotelId = toSignal(
    this.route.paramMap.pipe(map((p) => p.get('id') ?? '')),
    { initialValue: '' },
  );

  protected readonly hotel = computed(() =>
    this.service.getHotelDetailPageData(this.hotelId()),
  );
  protected readonly loadState = computed(() =>
    this.service.getLoadState(this.hotelId()),
  );

  protected readonly scrollProgress = signal(0);

  protected readonly directionsUrl = computed(() => this.buildDirectionsUrl(
    this.hotel()?.latitude ?? null,
    this.hotel()?.longitude ?? null,
  ));

  protected readonly mapUrl = computed(() => this.buildMapUrl(
    this.hotel()?.latitude ?? null,
    this.hotel()?.longitude ?? null,
  ));

  protected readonly primaryBookingPartners = computed(() =>
    (this.hotel()?.bookingPartners ?? []).filter((partner) => !!partner.url && partner.variant !== 'muted'),
  );

  protected readonly secondaryBookingPartners = computed(() =>
    (this.hotel()?.bookingPartners ?? []).filter((partner) => !!partner.url && partner.variant === 'muted'),
  );

  protected readonly hasBookingPartners = computed(() =>
    this.primaryBookingPartners().length > 0 || this.secondaryBookingPartners().length > 0,
  );

  private readonly locationRequestKey = computed(() => {
    const loc = this.locationSvc.location();
    if (!loc) return 'none';
    return `${loc.latitude}:${loc.longitude}:${loc.source}`;
  });

  private readonly hdScroll = viewChild<ElementRef<HTMLElement>>('hdScroll');

  constructor() {
    effect(() => {
      this.hotelId();
      this.scrollProgress.set(0);
      const el = this.hdScroll()?.nativeElement;
      if (el) el.scrollTop = 0;
    });

    effect(() => {
      const id = this.hotelId();
      this.locationRequestKey();
      if (id) this.service.loadHotelDetail(id);
    });
  }

  protected onScroll(event: Event): void {
    const scrollTop = (event.currentTarget as Element).scrollTop;
    this.scrollProgress.set(Math.min(scrollTop / 200, 1));
  }

  protected goBack(): void {
    this.location.back();
  }

  protected goToLogin(): void {
    this.router.navigate(['/profile']);
  }

  protected toggleSaved(): void {
    const id = this.hotelId();
    if (id) this.service.toggleSaved(id);
  }

  protected navigateToSimilar(id: string): void {
    this.router.navigate(['/stays', id]);
  }

  protected readonly submitReview = (draft: ReviewDraft): Observable<unknown> => {
    const numId = parseInt(this.hotelId(), 10);
    if (isNaN(numId)) return throwError(() => new Error('Invalid hotel id.'));
    return this.http.post(`/api/places/${numId}/reviews`, {
      rating:  draft.rating,
      comment: draft.body,
    });
  };

  protected onReviewSubmitted(_draft: ReviewDraft): void {
    const id = this.hotelId();
    if (!id) return;
    const numId = parseInt(id, 10);
    if (!isNaN(numId)) {
      this.analytics.track('review_created', { entityType: 'hotel', entityId: numId });
    }
    this.service.loadHotelDetail(id);
  }

  protected onDirectionsClick(): void {
    const numId = parseInt(this.hotelId(), 10);
    if (isNaN(numId)) return;
    this.analytics.track('directions_started', {
      entityType: 'hotel',
      entityId: numId,
      metadata: { provider: 'google_maps', context: 'hotel_detail' },
    });
  }

  protected onPartnerClick(partner: HotelBookingPartner): void {
    const numId = parseInt(this.hotelId(), 10);
    if (isNaN(numId)) return;
    this.analytics.trackOutboundLink(partner.name, partner.url, {
      entityType: 'hotel',
      entityId: numId,
      metadata: { context: 'hotel_detail' },
    });
  }

  protected amenityIconPath(icon: HotelAmenityIcon): string {
    switch (icon) {
      case 'pool':
        return 'M2 20c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2M2 16c2 0 2-2 4-2s2 2 4 2 2-2 4-2 2 2 4 2M10 8.5a2.5 2.5 0 0 1 5 0V12H10V8.5zM14 3a2 2 0 1 1-4 0 2 2 0 0 1 4 0z';
      case 'wifi':
        return 'M5 12.55a11 11 0 0 1 14.08 0M1.42 9a16 16 0 0 1 21.16 0M8.53 16.11a6 6 0 0 1 6.95 0M12 20h.01';
      case 'breakfast':
        return 'M18 8h1a4 4 0 0 1 0 8h-1M2 8h16v9a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4V8zM6 1v3M10 1v3M14 1v3';
      case 'parking':
        return 'M13 5H9v14M9 5h4a4 4 0 0 1 0 8H9';
      case 'spa':
        return 'M12 22s-8-4.5-8-11.8A8 8 0 0 1 12 2a8 8 0 0 1 8 8.2c0 7.3-8 11.8-8 11.8zM12 2C6.5 2 2 6.5 2 12';
      case 'gym':
        return 'M6.5 6.5 17.5 17.5M6.5 17.5 17.5 6.5M3 12h3M18 12h3M12 3v3M12 18v3';
      case 'restaurant':
        return 'M3 2v7c0 1.1.9 2 2 2h4a2 2 0 0 0 2-2V2M7 2v20M21 15V2a5 5 0 0 0-5 5v6c0 1.1.9 2 2 2h3zm0 0v7';
      case 'ac':
        return 'M12 3v18M3 12h18M5.6 5.6l12.8 12.8M18.4 5.6 5.6 18.4M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8z';
    }
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

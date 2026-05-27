import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable, of } from 'rxjs';
import { vi } from 'vitest';

import { ContentModeratorEntityReviewComponent } from './entity-review.component';
import {
  ContentModeratorEntityReviewDetail,
  ContentModeratorEntityReviewListResponse,
  ContentModeratorEntityType,
} from '../../../../core/models/content-moderator/content-moderator-entity-review.model';
import { ContentModeratorEntityReviewService } from '../../../../core/services/content-moderator/content-moderator-entity-review.service';
import { ContentModeratorDashboardService } from '../../../../core/services/content-moderator/content-moderator-dashboard.service';
import { USER_APP_BASE_URL } from '../../../../core/services/user-app-link.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  template: '',
})
class SidebarStubComponent {
  @Input() reportedReviewsCount: number | null = null;
}

@Component({
  selector: 'app-topbar',
  standalone: true,
  template: '<header>{{ title }}</header>',
})
class TopbarStubComponent {
  @Input() title = '';
  @Input() showBack = false;
  @Input() backRoute: string | null = null;
}

describe('ContentModeratorEntityReviewComponent user-app preview link', () => {
  it.each([
    { entityType: 'destination' as const, route: '/destinations/7' },
    { entityType: 'location' as const, route: '/locations/8' },
    { entityType: 'place' as const, route: '/places/9' },
    { entityType: 'place' as const, route: '/stays/10' },
    { entityType: 'event' as const, route: '/events/11' },
  ])('shows a new-tab user-app link for $route', async ({ entityType, route }) => {
    const detail = makeDetail(entityType, Number(route.split('/').pop()), route, true);
    const fixture = await createFixture(entityType, detail);

    fixture.componentInstance.openDetail(detail);
    fixture.detectChanges();

    const link = fixture.nativeElement.querySelector('a[href^="https://user.example"]') as HTMLAnchorElement | null;
    expect(link).not.toBeNull();
    expect(link?.textContent).toContain('View in user-app');
    expect(link?.getAttribute('href')).toBe(`https://user.example${route}`);
    expect(link?.getAttribute('target')).toBe('_blank');
    expect(link?.getAttribute('rel')).toBe('noopener noreferrer');
    expect(link?.getAttribute('aria-label')).toContain('Open');

    expect(buttonText()).toContain('Close');
    expect(buttonText()).toContain('Suspend');
    expect(buttonText()).toContain('Delete');
  });

  it('hides the user-app link when the backend marks the route unavailable', async () => {
    const detail = makeDetail('event', 44, null, false, 'Deleted');
    const fixture = await createFixture('event', detail);

    fixture.componentInstance.openDetail(detail);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('View in user-app');
    expect(fixture.nativeElement.querySelector('a[href^="https://user.example"]')).toBeNull();
  });

  async function createFixture(
    entityType: ContentModeratorEntityType,
    detail: ContentModeratorEntityReviewDetail,
  ): Promise<ComponentFixture<ContentModeratorEntityReviewComponent>> {
    await TestBed.configureTestingModule({
      imports: [ContentModeratorEntityReviewComponent],
      providers: [
        { provide: ActivatedRoute, useValue: { data: of({ entityType }) } },
        { provide: USER_APP_BASE_URL, useValue: 'https://user.example/' },
        { provide: ContentModeratorEntityReviewService, useValue: entityReviewService(detail) },
        { provide: ContentModeratorDashboardService, useValue: dashboardService() },
      ],
    })
      .overrideComponent(ContentModeratorEntityReviewComponent, {
        set: { imports: [CommonModule, FormsModule, SidebarStubComponent, TopbarStubComponent] },
      })
      .compileComponents();

    const fixture = TestBed.createComponent(ContentModeratorEntityReviewComponent);
    fixture.detectChanges();
    return fixture;
  }

  function entityReviewService(detail: ContentModeratorEntityReviewDetail): Partial<ContentModeratorEntityReviewService> {
    return {
      getPage$: vi.fn((): Observable<ContentModeratorEntityReviewListResponse> => of({
        items: [],
        totalCount: 0,
        skip: 0,
        take: 25,
        nextSkip: null,
        hasMore: false,
        counts: {
          allCount: 0,
          activeCount: 0,
          suspendedCount: 0,
          deletedCount: 0,
        },
      })),
      getDetail$: vi.fn(() => of(detail)),
      suspend$: vi.fn(() => of({ ...detail, status: 'Suspended', isSuspended: true })),
      unsuspend$: vi.fn(() => of({ ...detail, status: 'Active', isSuspended: false })),
      delete$: vi.fn(() => of({ ...detail, status: 'Deleted', isDeleted: true })),
      formatDate: vi.fn((value?: string | null) => value ?? 'Not available'),
      formatDatetime: vi.fn((value?: string | null) => value ?? 'Not available'),
    };
  }

  function dashboardService(): Partial<ContentModeratorDashboardService> {
    return {
      loadDashboard: vi.fn(() => of({
        currentUser: {
          id: 1,
          displayName: 'Moderator',
          email: 'moderator@example.com',
          avatarUrl: null,
          roleDisplayName: 'Content Moderator',
        },
        stats: {
          destinationsCount: 0,
          locationsCount: 0,
          placesCount: 0,
          eventsCount: 0,
          suspendedDestinationsCount: 0,
          suspendedLocationsCount: 0,
          suspendedPlacesCount: 0,
          suspendedEventsCount: 0,
          deletedDestinationsCount: 0,
          deletedLocationsCount: 0,
          deletedPlacesCount: 0,
          deletedEventsCount: 0,
          pendingReviewReportsCount: 0,
          reportedReviewsCount: 0,
          activeReviewsCount: 0,
          resolvedReportsCount: 0,
          removedReviewsCount: 0,
        },
        quickLinks: {
          destinationsReviewRoute: '/content-moderator/destinations',
          locationsReviewRoute: '/content-moderator/locations',
          placesReviewRoute: '/content-moderator/places',
          eventsReviewRoute: '/content-moderator/events',
          reviewManagementRoute: '/content-moderator/review-management',
          pendingReportsRoute: '/content-moderator/review-management?status=reported',
        },
        badges: { pendingReviewReportsCount: 0 },
      })),
    };
  }

  function makeDetail(
    entityType: ContentModeratorEntityType,
    entityId: number,
    publicDetailRoute: string | null,
    canOpenInUserApp: boolean,
    status = 'Active',
  ): ContentModeratorEntityReviewDetail {
    const isDeleted = status === 'Deleted';
    const isSuspended = status === 'Suspended';

    return {
      entityId,
      entityType,
      name: `${entityType} ${entityId}`,
      summary: null,
      descriptionPreview: 'Preview',
      thumbnailUrl: null,
      status,
      rawStatus: status === 'Active' ? 'Published' : status,
      isPublished: status === 'Active',
      isSuspended,
      isDeleted,
      createdAt: '2026-05-27T09:00:00Z',
      updatedAt: null,
      publishedAt: status === 'Active' ? '2026-05-27T09:00:00Z' : null,
      regionId: 1,
      regionName: 'Region',
      territoryName: 'Territory',
      destinationId: entityType === 'destination' ? null : 2,
      destinationName: entityType === 'destination' ? null : 'Destination',
      locationId: entityType === 'place' || entityType === 'event' ? 3 : null,
      locationName: entityType === 'place' || entityType === 'event' ? 'Location' : null,
      placeId: entityType === 'event' ? 4 : null,
      placeName: entityType === 'event' ? 'Place' : null,
      typeName: 'Type',
      createdByUserId: null,
      startsAt: entityType === 'event' ? '2026-05-28T09:00:00Z' : null,
      endsAt: entityType === 'event' ? '2026-05-28T11:00:00Z' : null,
      publicDetailRoute,
      canOpenInUserApp,
      permissions: {
        canView: true,
        canSuspend: !isDeleted && !isSuspended,
        canUnsuspend: isSuspended,
        canDelete: !isDeleted,
        canRestore: false,
      },
      description: 'Full description',
      images: [],
      tags: [],
      metadata: {
        latitude: null,
        longitude: null,
        address: null,
        boundaryNote: null,
        boundaryShapeType: null,
        parentEntityType: null,
        venueName: null,
        ticketPrice: null,
        isFree: null,
        isAllDay: null,
        isRecurring: null,
        contactEmail: null,
        phoneNumber: null,
        website: null,
        contentCounts: {},
      },
    };
  }

  function buttonText(): string {
    const buttons = Array.from(document.querySelectorAll('button')) as HTMLButtonElement[];
    return buttons.map(button => button.textContent?.trim()).join(' ');
  }
});

import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { ContentModeratorDashboardComponent } from './content-moderator-dashboard.component';
import { ContentModeratorDashboard } from '../../../../core/models/content-moderator/content-moderator-dashboard.model';
import { ContentModeratorDashboardService } from '../../../../core/services/content-moderator/content-moderator-dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <aside>
      Review Management
      @if ((reportedReviewsCount ?? 0) > 0) {
        <span class="sidebar-badge">{{ reportedReviewsCount }}</span>
      }
    </aside>
  `,
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
}

describe('ContentModeratorDashboardComponent', () => {
  let fixture: ComponentFixture<ContentModeratorDashboardComponent>;
  let dashboardService: { loadDashboard: ReturnType<typeof vi.fn> };
  let router: Router;
  let dashboardResult$: Observable<ContentModeratorDashboard>;

  const authService = {
    fullName: 'Moderator User',
    getUser: vi.fn(() => ({ role: 'Content Moderator' })),
  };

  beforeEach(async () => {
    dashboardResult$ = of(dashboardResponse());
    dashboardService = {
      loadDashboard: vi.fn(() => dashboardResult$),
    };

    await TestBed.configureTestingModule({
      imports: [ContentModeratorDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: ContentModeratorDashboardService, useValue: dashboardService },
      ],
    })
      .overrideComponent(ContentModeratorDashboardComponent, {
        set: { imports: [CommonModule, SidebarStubComponent, TopbarStubComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(ContentModeratorDashboardComponent);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  it('renders moderation metrics and passes the review badge to the sidebar', () => {
    fixture.detectChanges();

    expect(dashboardService.loadDashboard).toHaveBeenCalledOnce();
    const text = normalizedText();
    expect(text).toContain('Welcome back, Content Moderator');
    expect(text).toContain('Review Destinations');
    expect(text).toContain('Total destinations');
    expect(text).toContain('Review Locations');
    expect(text).toContain('Review Places');
    expect(text).toContain('Review Events');
    expect(text).toContain('Review Management');
    expect(text).toContain('User-flagged reviews awaiting moderation');
    expect(text).not.toContain('Content Review');
    expect(text).not.toContain('Review destinations, locations, places, and events.');
    expect(text).not.toContain('Reported Reviews');
    expect(text).not.toContain('Active Reviews');
    expect(text).not.toContain('Resolved Reports');

    const badge = fixture.nativeElement.querySelector('.sidebar-badge') as HTMLElement | null;
    expect(badge?.textContent?.trim()).toBe('3');
  });

  it('navigates entity cards to their dedicated review pages and keeps report access', () => {
    fixture.detectChanges();

    clickButtonContaining('Review Destinations');
    clickButtonContaining('Review Management');
    clickButtonContaining('Review Reports');

    expect(router.navigateByUrl).toHaveBeenCalledWith('/content-moderator/destinations');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/content-moderator/review-management');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/content-moderator/review-management?status=reported');
  });

  it('renders loading and error states', () => {
    const pending = new Subject<ContentModeratorDashboard>();
    dashboardResult$ = pending.asObservable();

    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Loading dashboard"]')).not.toBeNull();

    pending.next(dashboardResponse());
    pending.complete();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Loading dashboard"]')).toBeNull();

    dashboardResult$ = throwError(() => new Error('backend unavailable'));
    fixture.componentInstance.loadDashboard();
    fixture.detectChanges();
    expect(normalizedText()).toContain('Unable to load content moderator dashboard data.');
  });

  function clickButtonContaining(label: string): void {
    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>);
    const button = buttons.find(item => item.textContent?.includes(label));
    expect(button).toBeTruthy();
    button!.click();
    fixture.detectChanges();
  }

  function normalizedText(): string {
    return (fixture.nativeElement.textContent as string).replace(/\s+/g, ' ').trim();
  }

  function dashboardResponse(): ContentModeratorDashboard {
    return {
      currentUser: {
        id: 48,
        displayName: 'Content Moderator',
        email: 'moderator@gmail.com',
        avatarUrl: null,
        roleDisplayName: 'Content Moderator',
      },
      stats: {
        destinationsCount: 12,
        locationsCount: 24,
        placesCount: 36,
        eventsCount: 48,
        suspendedDestinationsCount: 1,
        suspendedLocationsCount: 2,
        suspendedPlacesCount: 3,
        suspendedEventsCount: 4,
        deletedDestinationsCount: 0,
        deletedLocationsCount: 1,
        deletedPlacesCount: 2,
        deletedEventsCount: 3,
        pendingReviewReportsCount: 3,
        reportedReviewsCount: 2,
        activeReviewsCount: 20,
        resolvedReportsCount: 5,
        removedReviewsCount: 4,
      },
      quickLinks: {
        destinationsReviewRoute: '/content-moderator/destinations',
        locationsReviewRoute: '/content-moderator/locations',
        placesReviewRoute: '/content-moderator/places',
        eventsReviewRoute: '/content-moderator/events',
        reviewManagementRoute: '/content-moderator/review-management',
        pendingReportsRoute: '/content-moderator/review-management?status=reported',
      },
      badges: {
        pendingReviewReportsCount: 3,
      },
    };
  }
});

import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { AdminDashboardComponent } from './admin-dashboard.component';
import { AdminDashboard } from '../../../../core/models/administrator/admin-dashboard.model';
import { AdminDashboardService } from '../../../../core/services/administrator/admin-dashboard.service';
import { AuthService } from '../../../../core/services/auth.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule],
  template: `
    <aside>
      Support Reports
      @if ((supportReportsCount ?? 0) > 0) {
        <span class="sidebar-badge">{{ supportReportsCount }}</span>
      }
    </aside>
  `,
})
class SidebarStubComponent {
  @Input() supportReportsCount: number | null = null;
}

@Component({
  selector: 'app-topbar',
  standalone: true,
  template: '<header>{{ title }}</header>',
})
class TopbarStubComponent {
  @Input() title = '';
}

describe('AdminDashboardComponent', () => {
  let fixture: ComponentFixture<AdminDashboardComponent>;
  let dashboardService: { loadDashboard: ReturnType<typeof vi.fn> };
  let router: Router;
  let dashboardResult$: Observable<AdminDashboard>;

  const authService = {
    fullName: 'Auth User',
    getUser: vi.fn(() => ({ role: 'Administrator' })),
  };

  beforeEach(async () => {
    dashboardResult$ = of(dashboardResponse());
    dashboardService = {
      loadDashboard: vi.fn(() => dashboardResult$),
    };

    await TestBed.configureTestingModule({
      imports: [AdminDashboardComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: AdminDashboardService, useValue: dashboardService },
      ],
    })
      .overrideComponent(AdminDashboardComponent, {
        set: { imports: [CommonModule, SidebarStubComponent, TopbarStubComponent] },
      })
      .compileComponents();

    fixture = TestBed.createComponent(AdminDashboardComponent);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
  });

  it('calls the backend dashboard service and renders backend metric values', () => {
    fixture.detectChanges();

    expect(dashboardService.loadDashboard).toHaveBeenCalledOnce();
    const text = normalizedText();
    expect(text).toContain('Welcome back, Zika Admin');
    expect(text).toContain('Territories');
    expect(text).toContain('12');
    expect(text).toContain('5 assigned');
    expect(text).toContain('App Users');
    expect(text).toContain('34');
    expect(text).toContain('2 suspended');
    expect(text).toContain('Support Reports');
    expect(text).toContain('5');
    expect(text).toContain('1 high priority');
    expect(text).toContain('Review Reports');
    expect(text).toContain('7');
    expect(text).toContain('Need moderation');
    expect(text).not.toContain('20');
    expect(text).not.toContain('8 reported');
  });

  it('passes the backend support reports count to the sidebar badge', () => {
    fixture.detectChanges();

    const badge = fixture.nativeElement.querySelector('.sidebar-badge') as HTMLElement | null;
    expect(badge?.textContent?.trim()).toBe('5');
  });

  it('navigates the Support Reports button to the support tickets route', () => {
    fixture.detectChanges();

    clickButtonContaining('Support Reports');

    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin-support-tickets');
  });

  it('navigates quick action cards to existing administrator pages', () => {
    fixture.detectChanges();

    clickButtonContaining('Manage Territories');
    clickButtonContaining('Manage Users');
    clickButtonContaining('Manage Support Reports');

    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin-territories');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin-users');
    expect(router.navigateByUrl).toHaveBeenCalledWith('/admin-support-tickets');
  });

  it('renders a loading state while dashboard data is pending', () => {
    const pending = new Subject<AdminDashboard>();
    dashboardResult$ = pending.asObservable();

    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-label="Loading dashboard"]')).not.toBeNull();

    pending.next(dashboardResponse());
    pending.complete();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-label="Loading dashboard"]')).toBeNull();
  });

  it('renders an error state with retry', () => {
    dashboardResult$ = throwError(() => new Error('backend unavailable'));

    fixture.detectChanges();

    expect(normalizedText()).toContain('Unable to load administrator dashboard data.');
    clickButtonContaining('Retry');
    expect(dashboardService.loadDashboard).toHaveBeenCalledTimes(2);
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

  function dashboardResponse(): AdminDashboard {
    return {
      currentUser: {
        id: 46,
        displayName: 'Zika Admin',
        email: 'zika@gmail.com',
        avatarUrl: null,
        roleDisplayName: 'Administrator',
      },
      stats: {
        territoriesCount: 12,
        assignedTerritoriesCount: 5,
        appUsersCount: 34,
        suspendedUsersCount: 2,
        reviewsCount: 56,
        reportedReviewsCount: 9,
        pendingReportsCount: 7,
        supportTicketsCount: 13,
        openSupportTicketsCount: 3,
        inProgressSupportTicketsCount: 2,
        resolvedSupportTicketsCount: 6,
        dismissedSupportTicketsCount: 2,
        unresolvedSupportTicketsCount: 5,
        highPrioritySupportTicketsCount: 1,
      },
      quickLinks: {
        territoryManagementRoute: '/admin-territories',
        userManagementRoute: '/admin-users',
        reviewManagementRoute: '/admin-reviews',
        supportTicketsRoute: '/admin-support-tickets',
        pendingReportsRoute: '/admin-reviews?status=reported',
      },
      badges: {
        pendingReportsCount: 7,
        supportReportsCount: 5,
      },
    };
  }
});

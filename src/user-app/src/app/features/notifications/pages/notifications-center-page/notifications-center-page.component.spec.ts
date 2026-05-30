import { ComponentFixture, TestBed } from '@angular/core/testing';
import { computed, signal, Signal, WritableSignal } from '@angular/core';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { AppNotification, AppNotificationsService } from '../../../../core/services/app-notifications.service';
import { NotificationsCenterPageComponent } from './notifications-center-page.component';

describe('NotificationsCenterPageComponent', () => {
  let fixture: ComponentFixture<NotificationsCenterPageComponent>;
  let items: WritableSignal<AppNotification[]>;
  let loading: WritableSignal<boolean>;
  let loadError: WritableSignal<string | null>;
  let unreadCount: WritableSignal<number>;
  let notifications: {
    items: Signal<AppNotification[]>;
    loading: Signal<boolean>;
    loadingMore: Signal<boolean>;
    loadError: Signal<string | null>;
    hasMore: Signal<boolean>;
    unreadCount: Signal<number>;
    loadInitial: ReturnType<typeof vi.fn>;
    loadMore: ReturnType<typeof vi.fn>;
    markRead: ReturnType<typeof vi.fn>;
    markAllRead: ReturnType<typeof vi.fn>;
  };
  let router: { navigateByUrl: ReturnType<typeof vi.fn> };

  beforeEach(async () => {
    items = signal<AppNotification[]>([]);
    loading = signal(false);
    loadError = signal<string | null>(null);
    unreadCount = signal(0);
    router = { navigateByUrl: vi.fn() };
    notifications = {
      items: computed(() => items()),
      loading: computed(() => loading()),
      loadingMore: computed(() => false),
      loadError: computed(() => loadError()),
      hasMore: computed(() => false),
      unreadCount: computed(() => unreadCount()),
      loadInitial: vi.fn(),
      loadMore: vi.fn(),
      markRead: vi.fn(),
      markAllRead: vi.fn(),
    };

    await TestBed.configureTestingModule({
      imports: [NotificationsCenterPageComponent],
      providers: [
        { provide: AppNotificationsService, useValue: notifications },
        { provide: Router, useValue: router },
      ],
    }).compileComponents();
  });

  it('loads notifications on entry and renders the empty state', () => {
    fixture = TestBed.createComponent(NotificationsCenterPageComponent);
    fixture.detectChanges();

    expect(notifications.loadInitial).toHaveBeenCalledWith(true);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No notifications yet');
  });

  it('renders a notification item and opens its target route', () => {
    items.set([
      {
        id: 4,
        type: 'contribution_approved',
        title: 'Contribution approved',
        message: 'Your contribution has been approved and published.',
        relatedEntityType: 'contribution',
        relatedEntityId: 4,
        actionUrl: '/places/44',
        createdAt: '2026-05-30T10:00:00Z',
        readAt: null,
        isRead: false,
      },
    ]);
    unreadCount.set(1);

    fixture = TestBed.createComponent(NotificationsCenterPageComponent);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.textContent).toContain('Contribution approved');
    expect(nativeElement.textContent).toContain('Your contribution has been approved and published.');

    nativeElement.querySelector<HTMLElement>('.notification-card')?.click();

    expect(notifications.markRead).toHaveBeenCalledWith(4);
    expect(router.navigateByUrl).toHaveBeenCalledWith('/places/44');
  });

  it('shows an error state with retry', () => {
    loadError.set("We couldn't load your notifications. Please try again.");

    fixture = TestBed.createComponent(NotificationsCenterPageComponent);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    expect(nativeElement.textContent).toContain('Something went wrong');
    nativeElement.querySelector<HTMLButtonElement>('.notifications-page__retry')?.click();

    expect(notifications.loadInitial).toHaveBeenCalledWith(true);
    expect(notifications.loadInitial).toHaveBeenCalledTimes(2);
  });
});

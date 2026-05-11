import { ApplicationRef, computed, signal, type Signal, type WritableSignal } from '@angular/core';
import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, Subject } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../../../../core/services/auth.service';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { TagService, type UserInterestsResponse } from '../../../../core/services/tag.service';
import { UserService } from '../../../../core/services/user.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AppNotificationsService } from '../../../../core/services/app-notifications.service';
import { ExploreScrollStateService } from '../../services/explore-scroll-state.service';
import { ExploreService } from '../../services/explore.service';
import { ExplorePageComponent } from './explore-page.component';

describe('ExplorePageComponent curated section', () => {
  let fixture: ComponentFixture<ExplorePageComponent>;
  let host: HTMLElement;
  let loggedIn: WritableSignal<boolean>;
  let userProfile: WritableSignal<{ id: number; needsInterestsOnboarding: boolean } | null>;
  let interestRevision: WritableSignal<number>;
  let exploreLoading: WritableSignal<boolean>;
  let exploreLoaded: WritableSignal<boolean>;
  let curatedItems: WritableSignal<Array<{
    id: string;
    title: string;
    subtitle: string;
    categoryLabel: string;
    image: string;
    isSaved: boolean;
  }>>;
  let interestsResponse: UserInterestsResponse | Subject<UserInterestsResponse>;
  let tagService: { loadUserInterests: ReturnType<typeof vi.fn>; interestRevision: Signal<number> };

  const emptyInterests: UserInterestsResponse = {
    tags: [],
    explicitTags: [],
    inferredTags: [],
  };

  const selectedInterests: UserInterestsResponse = {
    tags: [{ tagId: 1, name: 'Nature', slug: 'nature' }],
    explicitTags: [{ tagId: 1, name: 'Nature', slug: 'nature' }],
    inferredTags: [],
  };

  beforeEach(async () => {
    loggedIn = signal(true);
    userProfile = signal({
      id: 7,
      needsInterestsOnboarding: false,
    });
    interestRevision = signal(0);
    exploreLoading = signal(false);
    exploreLoaded = signal(true);
    curatedItems = signal([]);
    interestsResponse = emptyInterests;

    tagService = {
      interestRevision: computed(() => interestRevision()),
      loadUserInterests: vi.fn(() => {
        if (interestsResponse instanceof Subject) {
          return interestsResponse.asObservable();
        }
        return of(interestsResponse);
      }),
    };

    await TestBed.configureTestingModule({
      imports: [ExplorePageComponent],
      providers: [
        {
          provide: AuthService,
          useValue: { isLoggedIn: computed(() => loggedIn()) },
        },
        {
          provide: UserService,
          useValue: { profile: computed(() => userProfile()) },
        },
        { provide: TagService, useValue: tagService },
        {
          provide: ExploreService,
          useValue: {
            loadError: computed(() => null),
            loading: computed(() => exploreLoading()),
            loaded: computed(() => exploreLoaded()),
            promo: computed(() => ({
              title: 'Personalize your stay',
              message: 'Sign in to save places.',
              ctaLabel: 'Sign In',
            })),
            curatedForYou: computed(() => curatedItems()),
            eventsThisWeek: computed(() => []),
            popularNearby: computed(() => []),
            topStays: computed(() => []),
            mustSeeAttractions: computed(() => []),
            landmarkCategories: computed(() => []),
            localFavorites: computed(() => []),
            useCurrentLocation: vi.fn(),
            toggleSaved: vi.fn(),
          },
        },
        {
          provide: DestinationService,
          useValue: {
            mode: computed(() => 'location'),
            id: computed(() => null),
            name: computed(() => 'Belgrade'),
            isDestinationMode: computed(() => false),
            setDestinationOverride: vi.fn(),
          },
        },
        {
          provide: LocationService,
          useValue: {
            location: computed(() => ({
              latitude: 44.8125,
              longitude: 20.4612,
              city: 'Belgrade',
              region: '',
              country: 'Serbia',
              displayLabel: 'Belgrade, Serbia',
              source: 'approximate',
            })),
            loading: computed(() => false),
            displayLabel: computed(() => 'Belgrade, Serbia'),
            resolveLocationForProductFlow: vi.fn(() => Promise.resolve()),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: { get: vi.fn(() => null) },
              data: {},
            },
          },
        },
        { provide: Router, useValue: { url: '/explore', navigate: vi.fn() } },
        { provide: Location, useValue: { back: vi.fn() } },
        {
          provide: ExploreScrollStateService,
          useValue: {
            pop: vi.fn(() => null),
            save: vi.fn(),
            markClickedItem: vi.fn(),
          },
        },
        { provide: AnalyticsService, useValue: { track: vi.fn() } },
        { provide: AppNotificationsService, useValue: { unreadCount: computed(() => 0) } },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    fixture?.destroy();
    TestBed.resetTestingModule();
  });

  it('does not render Curated for You when the user has no explicit or inferred interests', async () => {
    interestsResponse = emptyInterests;
    createComponent();

    await settle();

    expect(host.querySelector('app-curated-for-you')).toBeNull();
    expect(host.textContent).not.toContain('Curated for You');
    expect(host.textContent).not.toContain('Nothing personalized nearby yet');
  });

  it('renders Curated for You for selected interests', async () => {
    interestsResponse = selectedInterests;
    curatedItems.set([{
      id: '42',
      title: 'Kalemegdan',
      subtitle: 'Matches your saved interest: Nature',
      categoryLabel: 'Nature',
      image: '/media/kalemegdan.jpg',
      isSaved: false,
    }]);
    createComponent();

    await settle();

    expect(host.querySelector('app-curated-for-you')).not.toBeNull();
    expect(host.textContent).toContain('Curated for You');
    expect(host.textContent).toContain('Kalemegdan');
  });

  it('keeps the curated section in loading state before recommendations settle', async () => {
    interestsResponse = selectedInterests;
    exploreLoaded.set(false);
    exploreLoading.set(true);
    createComponent();

    await settle();

    expect(host.querySelector('app-curated-for-you')).not.toBeNull();
    expect(host.textContent).toContain('Curated for You');
    expect(host.textContent).not.toContain('Nothing personalized nearby yet');
    expect(host.querySelector('[aria-busy="true"]')).not.toBeNull();
  });

  it('shows the existing empty state when interests exist but no recommendations match', async () => {
    interestsResponse = selectedInterests;
    curatedItems.set([]);
    exploreLoaded.set(true);
    exploreLoading.set(false);
    createComponent();

    await settle();

    expect(host.querySelector('app-curated-for-you')).not.toBeNull();
    expect(host.textContent).toContain('Nothing personalized nearby yet');
  });

  it('does not show the no-results empty state while interests are still loading', async () => {
    interestsResponse = new Subject<UserInterestsResponse>();
    createComponent();

    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    fixture.detectChanges();

    expect(host.querySelector('app-curated-for-you')).toBeNull();
    expect(host.textContent).not.toContain('Nothing personalized nearby yet');
  });

  function createComponent(): void {
    fixture = TestBed.createComponent(ExplorePageComponent);
    host = fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    TestBed.inject(ApplicationRef).tick();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }
});

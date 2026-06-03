import { ApplicationRef, computed, signal, type WritableSignal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { AuthService } from '../../../core/services/auth.service';
import { DestinationService } from '../../../core/services/destination.service';
import { FavoriteStateService } from '../../../core/services/favorite-state.service';
import { LocationService } from '../../../core/services/location.service';
import { SaveToCollectionService } from '../../../core/services/save-to-collection.service';
import { TagService } from '../../../core/services/tag.service';
import { UserService, type UserProfile } from '../../../core/services/user.service';
import { CollectionsService } from '../../profile/services/collections';
import { ExploreService } from './explore.service';

describe('ExploreService curation context', () => {
  let service: ExploreService;
  let http: HttpTestingController;
  let appRef: ApplicationRef;
  let loggedIn: WritableSignal<boolean>;
  let profile: WritableSignal<UserProfile | null>;
  let interestRevision: WritableSignal<number>;
  let mode: WritableSignal<'location' | 'destination'>;
  let destinationId: WritableSignal<number | null>;
  let collectionsService: { addItem: ReturnType<typeof vi.fn> };
  let activeLocation: WritableSignal<{
    latitude: number;
    longitude: number;
    source: 'gps' | 'approximate';
  } | null>;

  beforeEach(() => {
    loggedIn = signal(false);
    profile = signal(null);
    interestRevision = signal(0);
    mode = signal('location');
    destinationId = signal(null);
    activeLocation = signal({
      latitude: 44.8125,
      longitude: 20.4612,
      source: 'approximate',
    });
    collectionsService = { addItem: vi.fn(() => of({})) };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: computed(() => loggedIn()) } },
        { provide: UserService, useValue: { profile: computed(() => profile()) } },
        { provide: TagService, useValue: { interestRevision: computed(() => interestRevision()) } },
        {
          provide: LocationService,
          useValue: {
            location: computed(() => activeLocation()),
            ensureIpFallback: vi.fn(),
          },
        },
        {
          provide: DestinationService,
          useValue: {
            mode: computed(() => mode()),
            id: computed(() => destinationId()),
            set: vi.fn(),
            useLocationMode: vi.fn(() => mode.set('location')),
          },
        },
        {
          provide: FavoriteStateService,
          useValue: { resolve: (_entityType: string, _id: string, fallback: boolean) => fallback },
        },
        { provide: SaveToCollectionService, useValue: { open: vi.fn() } },
        { provide: CollectionsService, useValue: collectionsService },
      ],
    });

    service = TestBed.inject(ExploreService);
    http = TestBed.inject(HttpTestingController);
    appRef = TestBed.inject(ApplicationRef);
  });

  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });

  it('reloads same-location Explore data after login and ignores the stale guest response', () => {
    appRef.tick();
    const guestRequest = http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612');

    loggedIn.set(true);
    profile.set(makeProfile(7));
    appRef.tick();
    const authenticatedRequest = http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612');

    authenticatedRequest.flush(makeExploreResponse([
      {
        id: 42,
        title: 'Kalemegdan',
        subtitle: 'Matches your saved interest: Nature',
        categoryLabel: 'Nature',
      },
    ]));

    expect(service.curatedForYou().map(item => item.title)).toEqual(['Kalemegdan']);

    guestRequest.flush(makeExploreResponse([]));

    expect(service.curatedForYou().map(item => item.title)).toEqual(['Kalemegdan']);
  });

  it('waits for the authenticated profile before requesting personalized Explore data', () => {
    loggedIn.set(true);
    appRef.tick();

    http.expectNone('/api/explore?latitude=44.8125&longitude=20.4612');

    profile.set(makeProfile(7));
    appRef.tick();

    http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612')
      .flush(makeExploreResponse([]));
  });

  it('invalidates the loaded Explore request when interest revision changes', () => {
    loggedIn.set(true);
    profile.set(makeProfile(7));
    appRef.tick();

    http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612')
      .flush(makeExploreResponse([
        {
          id: 10,
          title: 'Old Match',
          subtitle: 'Matches your saved interest: Food',
          categoryLabel: 'Food',
        },
      ]));

    expect(service.curatedForYou().map(item => item.title)).toEqual(['Old Match']);

    interestRevision.update(value => value + 1);
    appRef.tick();

    http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612')
      .flush(makeExploreResponse([
        {
          id: 11,
          title: 'Fresh Match',
          subtitle: 'Matches your saved interest: History',
          categoryLabel: 'History',
        },
      ]));

    expect(service.curatedForYou().map(item => item.title)).toEqual(['Fresh Match']);
  });

  it('clears user-specific curated items on logout before the guest reload completes', () => {
    loggedIn.set(true);
    profile.set(makeProfile(7));
    appRef.tick();

    http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612')
      .flush(makeExploreResponse([
        {
          id: 42,
          title: 'Private Match',
          subtitle: 'Matches your saved interest: Nature',
          categoryLabel: 'Nature',
        },
      ]));

    expect(service.curatedForYou()).toHaveLength(1);

    loggedIn.set(false);
    profile.set(null);
    appRef.tick();

    expect(service.curatedForYou()).toEqual([]);
    http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612')
      .flush(makeExploreResponse([]));
  });

  it('reloads consumers when the active prioritized location changes from approximate to GPS', () => {
    appRef.tick();
    http.expectOne('/api/explore?latitude=44.8125&longitude=20.4612')
      .flush(makeExploreResponse([]));

    activeLocation.set({
      latitude: 44.0128,
      longitude: 20.9114,
      source: 'gps',
    });
    appRef.tick();

    http.expectOne('/api/explore?latitude=44.0128&longitude=20.9114')
      .flush(makeExploreResponse([]));
  });

  it('saves directly to the provided collection instead of opening the chooser', () => {
    service.toggleSaved({
      id: '42',
      entityType: 'place',
      detailRoute: '/places/42',
      title: 'Kalemegdan',
      image: '/media/kalemegdan.jpg',
      isSaved: false,
      tags: [],
    }, 'curated-for-you', 9);

    expect(collectionsService.addItem).toHaveBeenCalledWith(9, expect.objectContaining({
      entityType: 'place',
      entityId: 42,
      title: 'Kalemegdan',
      image: '/media/kalemegdan.jpg',
      surface: 'explore_curated-for-you',
      analyticsEntityType: 'place',
    }));
  });
});

function makeProfile(id: number): UserProfile {
  return {
    id,
    username: `user-${id}`,
    email: `user-${id}@example.com`,
    firstName: 'Test',
    lastName: 'User',
    needsInterestsOnboarding: false,
  };
}

function makeExploreResponse(curatedForYou: Array<{
  id: number;
  title: string;
  subtitle?: string;
  categoryLabel?: string;
}>): Record<string, unknown> {
  return {
    selectedDestination: {
      id: 1,
      name: 'Belgrade',
      latitude: 44.8125,
      longitude: 20.4612,
    },
    eventsThisWeek: [],
    topStays: [],
    popularNearby: [],
    mustSeeAttractions: [],
    landmarkCategories: [],
    curatedForYou: curatedForYou.map(item => ({
      ...item,
      image: '/media/place.jpg',
      isSaved: false,
    })),
    localFavorites: [],
  };
}

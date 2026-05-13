import { ApplicationRef, computed, signal, type WritableSignal } from '@angular/core';
import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { vi } from 'vitest';
import { AuthService } from '../../../../core/services/auth.service';
import { DestinationService } from '../../../../core/services/destination.service';
import { LocationService } from '../../../../core/services/location.service';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { AppNotificationsService } from '../../../../core/services/app-notifications.service';
import { ExploreScrollStateService } from '../../services/explore-scroll-state.service';
import { ExploreService } from '../../services/explore.service';
import type { ExploreSection } from '../../models/explore-extras.model';
import { ExplorePageComponent } from './explore-page.component';

describe('ExplorePageComponent dynamic sections', () => {
  let fixture: ComponentFixture<ExplorePageComponent>;
  let host: HTMLElement;
  let loggedIn: WritableSignal<boolean>;
  let exploreLoading: WritableSignal<boolean>;
  let exploreLoaded: WritableSignal<boolean>;
  let sections: WritableSignal<ExploreSection[]>;

  beforeEach(async () => {
    loggedIn = signal(true);
    exploreLoading = signal(false);
    exploreLoaded = signal(true);
    sections = signal([]);

    await TestBed.configureTestingModule({
      imports: [ExplorePageComponent],
      providers: [
        {
          provide: AuthService,
          useValue: { isLoggedIn: computed(() => loggedIn()) },
        },
        {
          provide: ExploreService,
          useValue: {
            sections: computed(() => sections()),
            loadError: computed(() => null),
            loading: computed(() => exploreLoading()),
            loaded: computed(() => exploreLoaded()),
            promo: computed(() => ({
              title: 'Personalize your stay',
              message: 'Sign in to save places.',
              ctaLabel: 'Sign In',
            })),
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
        { provide: Router, useValue: { url: '/explore', navigate: vi.fn(), navigateByUrl: vi.fn() } },
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

  it('renders the empty state when the dynamic Explore feed has no sections', async () => {
    createComponent();

    await settle();

    expect(host.querySelector('app-explore-section')).toBeNull();
    expect(host.textContent).toContain('No Explore sections yet');
  });

  it('renders dynamic sections supplied by the Explore service', async () => {
    sections.set([makeSection()]);
    createComponent();

    await settle();

    expect(host.querySelector('app-explore-section')).not.toBeNull();
    expect(host.textContent).toContain('Curated for You');
    expect(host.textContent).toContain('Kalemegdan');
  });

  it('keeps the empty state hidden while sections are still loading', async () => {
    exploreLoaded.set(false);
    exploreLoading.set(true);
    createComponent();

    await settle();

    expect(host.textContent).not.toContain('No Explore sections yet');
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

function makeSection(): ExploreSection {
  return {
    key: 'curated-for-you',
    title: 'Curated for You',
    subtitle: 'Based on your tags',
    layout: 'horizontal-cards',
    cardType: 'place-card',
    moreRoute: '/recommendations',
    priority: 10,
    items: [{
      id: '42',
      entityType: 'place',
      detailRoute: '/places/42',
      title: 'Kalemegdan',
      subtitle: 'Matches your saved interest: Nature',
      categoryLabel: 'Nature',
      image: '/media/kalemegdan.jpg',
      isSaved: false,
      tags: [{ tagId: '1', name: 'Nature', slug: 'nature' }],
    }],
  };
}

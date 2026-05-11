import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Location as AngularLocation } from '@angular/common';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { MapPageComponent } from './map-page.component';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { LocationService } from '../../../../core/services/location.service';
import { MapApiService, MapEntityMarker } from '../../../../core/services/map-api.service';

describe('MapPage', () => {
  let component: MapPageComponent;
  let fixture: ComponentFixture<MapPageComponent>;
  let componentInternals: {
    onMarkerPointerUp(event: PointerEvent, key: string, marker: MapEntityMarker): void;
    onMarkerDomClick(event: MouseEvent, key: string, marker: MapEntityMarker): void;
    onMapLibreMarkerClick(event: { originalEvent?: MouseEvent }, key: string, marker: MapEntityMarker): void;
    onMapClick(event: { originalEvent: { target: EventTarget | null } }): void;
    rebuildAvailableMoreFilters(markers: MapEntityMarker[]): void;
    filterMarkersByMoreFilters(markers: MapEntityMarker[]): MapEntityMarker[];
    selectedCategoryLabelFilters: string[];
  };

  const router = {
    getCurrentNavigation: vi.fn(() => null),
    navigate: vi.fn(),
    navigateByUrl: vi.fn(),
    url: '/map',
  };

  const analytics = {
    track: vi.fn(),
  };

  const locationService = {
    location: vi.fn(() => null),
    stopMapLocationUpdates: vi.fn(),
  };

  const angularLocation = {
    replaceState: vi.fn(),
  };

  const mapApi = {
    search: vi.fn(),
    getMarkersInViewport: vi.fn(),
    getWalkingRoute: vi.fn(),
  };

  const placeMarker: MapEntityMarker = {
    entityType: 'place',
    entityId: 42,
    title: 'City Walls',
    subtitle: 'Old Town',
    categoryLabel: 'Landmark',
    imageUrl: '/media/city-walls.jpg',
    detailRoute: '/places/42',
    latitude: 42.641,
    longitude: 18.11,
    distance: '250 m',
    isSaved: false,
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-06T10:00:00.000Z'));
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [MapPageComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: MapApiService, useValue: mapApi },
        { provide: AnalyticsService, useValue: analytics },
        { provide: LocationService, useValue: locationService },
        { provide: AngularLocation, useValue: angularLocation },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MapPageComponent);
    component = fixture.componentInstance;
    componentInternals = component as unknown as typeof componentInternals;
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('activates entity markers from a desktop mouse pointerup', () => {
    const { event, preventDefault, stopPropagation } = createPointerEvent('mouse', 0);

    componentInternals.onMarkerPointerUp(event, markerKey(placeMarker), placeMarker);

    expect(component.selectedPlace).toBe(placeMarker);
    expect(preventDefault).toHaveBeenCalled();
    expect(stopPropagation).toHaveBeenCalled();
    expect(analytics.track).toHaveBeenCalledWith('map_marker_opened', {
      entityType: 'place',
      entityId: placeMarker.entityId,
      metadata: {
        surface: 'map',
        category: placeMarker.categoryLabel,
      },
    });
  });

  it('keeps the mobile tap click path unchanged', () => {
    const touchPointer = createPointerEvent('touch', 0);
    const click = createMouseEvent();

    componentInternals.onMarkerPointerUp(touchPointer.event, markerKey(placeMarker), placeMarker);

    expect(component.selectedPlace).toBeNull();
    expect(touchPointer.preventDefault).not.toHaveBeenCalled();
    expect(touchPointer.stopPropagation).not.toHaveBeenCalled();

    componentInternals.onMarkerDomClick(click.event, markerKey(placeMarker), placeMarker);

    expect(component.selectedPlace).toBe(placeMarker);
    expect(click.preventDefault).toHaveBeenCalled();
    expect(click.stopPropagation).toHaveBeenCalled();
  });

  it('deduplicates desktop pointer, DOM click, and MapLibre marker click activation', () => {
    const pointer = createPointerEvent('mouse', 0);
    const click = createMouseEvent();

    componentInternals.onMarkerPointerUp(pointer.event, markerKey(placeMarker), placeMarker);
    componentInternals.onMarkerDomClick(click.event, markerKey(placeMarker), placeMarker);
    componentInternals.onMapLibreMarkerClick({ originalEvent: click.event }, markerKey(placeMarker), placeMarker);

    expect(component.selectedPlace).toBe(placeMarker);
    expect(analytics.track).toHaveBeenCalledTimes(1);
  });

  it('keeps the selected marker when an immediate map background click follows activation', () => {
    const pointer = createPointerEvent('mouse', 0);

    componentInternals.onMarkerPointerUp(pointer.event, markerKey(placeMarker), placeMarker);
    componentInternals.onMapClick({ originalEvent: { target: document.createElement('canvas') } });

    expect(component.selectedPlace).toBe(placeMarker);
  });

  it('renders the selected marker preview from the shared template when marker state is set', () => {
    const click = createMouseEvent();

    componentInternals.onMarkerDomClick(click.event, markerKey(placeMarker), placeMarker);
    fixture.detectChanges();

    const preview = fixture.nativeElement.querySelector('.map-page__preview-card') as HTMLElement | null;
    expect(preview).not.toBeNull();
    expect(preview?.textContent).toContain(placeMarker.title);
    expect(preview?.textContent).toContain(placeMarker.categoryLabel);
  });

  it('opens More inline on desktop without navigating away from the map', () => {
    mockViewport(true);

    component.onMoreFiltersClick();

    expect(component.isMoreFiltersOpen).toBe(true);
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('keeps the mobile More flow routed to the Filters page', () => {
    mockViewport(false);

    component.onMoreFiltersClick();

    expect(component.isMoreFiltersOpen).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/filters']);
  });

  it('derives inline More filters from backend marker category labels', () => {
    const markers: MapEntityMarker[] = [
      placeMarker,
      { ...placeMarker, entityId: 43, title: 'Museum One', categoryLabel: 'Museum' },
      { ...placeMarker, entityId: 44, title: 'Museum Two', categoryLabel: 'Museum' },
      { ...placeMarker, entityType: 'event', entityId: 12, title: 'Jazz Night', categoryLabel: 'Live Music' },
      { ...placeMarker, entityType: 'destination', entityId: 9, title: 'Dubrovnik', categoryLabel: 'City' },
    ];

    componentInternals.rebuildAvailableMoreFilters(markers);

    expect(component.availableMoreFilters).toEqual([
      { id: 'museum', label: 'Museum', count: 2 },
      { id: 'landmark', label: 'Landmark', count: 1 },
      { id: 'live music', label: 'Live Music', count: 1 },
    ]);
  });

  it('filters visible entity markers by selected backend category labels', () => {
    const markers: MapEntityMarker[] = [
      placeMarker,
      { ...placeMarker, entityId: 43, title: 'Museum One', categoryLabel: 'Museum' },
      { ...placeMarker, entityType: 'event', entityId: 12, title: 'Jazz Night', categoryLabel: 'Live Music' },
    ];
    componentInternals.selectedCategoryLabelFilters = ['museum'];

    const filtered = componentInternals.filterMarkersByMoreFilters(markers);

    expect(filtered.map((marker) => marker.title)).toEqual(['Museum One']);
  });

  it('replaces the selected marker when another entity marker is clicked', () => {
    const stayMarker: MapEntityMarker = {
      ...placeMarker,
      entityType: 'stay',
      entityId: 7,
      title: 'Harbor Stay',
      detailRoute: '/stays/7',
    };
    const placeClick = createMouseEvent();
    const stayClick = createMouseEvent();

    componentInternals.onMarkerDomClick(placeClick.event, markerKey(placeMarker), placeMarker);
    componentInternals.onMarkerDomClick(stayClick.event, markerKey(stayMarker), stayMarker);

    expect(component.selectedPlace).toBe(stayMarker);
  });

  it('dismisses the selected marker card on a later map background click', () => {
    const click = createMouseEvent();

    componentInternals.onMarkerDomClick(click.event, markerKey(placeMarker), placeMarker);
    vi.setSystemTime(new Date('2026-05-06T10:00:00.500Z'));

    componentInternals.onMapClick({ originalEvent: { target: document.createElement('canvas') } });

    expect(component.selectedPlace).toBeNull();
  });

  it('ignores map clicks whose original event target is a marker element', () => {
    const markerButton = document.createElement('button');
    markerButton.classList.add('gt-marker');
    component.selectedPlace = placeMarker;

    componentInternals.onMapClick({ originalEvent: { target: markerButton } });

    expect(component.selectedPlace).toBe(placeMarker);
  });

  it('opens the selected entity detail route with map focus state', () => {
    const routes: Array<{ entityType: MapEntityMarker['entityType']; detailRoute: string }> = [
      { entityType: 'place', detailRoute: '/places/42' },
      { entityType: 'stay', detailRoute: '/stays/7' },
      { entityType: 'event', detailRoute: '/events/9' },
    ];

    for (const route of routes) {
      const marker: MapEntityMarker = {
        ...placeMarker,
        entityType: route.entityType,
        detailRoute: route.detailRoute,
      };
      component.selectedPlace = marker;
      router.navigateByUrl.mockClear();

      component.openDetails();

      expect(router.navigateByUrl).toHaveBeenCalledWith(route.detailRoute, {
        state: {
          fromMapPreview: true,
          mapFocus: marker,
        },
      });
    }
  });

  function markerKey(marker: MapEntityMarker): string {
    return `${marker.entityType}:${marker.entityId}`;
  }

  function createPointerEvent(pointerType: string, button: number): {
    event: PointerEvent;
    preventDefault: ReturnType<typeof vi.fn>;
    stopPropagation: ReturnType<typeof vi.fn>;
  } {
    const preventDefault = vi.fn();
    const stopPropagation = vi.fn();

    return {
      event: { pointerType, button, preventDefault, stopPropagation } as unknown as PointerEvent,
      preventDefault,
      stopPropagation,
    };
  }

  function createMouseEvent(): {
    event: MouseEvent;
    preventDefault: ReturnType<typeof vi.fn>;
    stopPropagation: ReturnType<typeof vi.fn>;
  } {
    const preventDefault = vi.fn();
    const stopPropagation = vi.fn();

    return {
      event: { preventDefault, stopPropagation } as unknown as MouseEvent,
      preventDefault,
      stopPropagation,
    };
  }

  function mockViewport(matches: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches,
        media: query,
        onchange: null,
        addListener: vi.fn(),
        removeListener: vi.fn(),
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        dispatchEvent: vi.fn(),
      })),
    });
  }
});

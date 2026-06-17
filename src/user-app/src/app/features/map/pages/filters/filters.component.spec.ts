import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { Filters } from './filters.component';
import { AnalyticsService } from '../../../../core/services/analytics.service';
import { MapEntityMarker } from '../../../../core/services/map-api.service';
import { MapFilterStateService } from '../../services/map-filter-state.service';

describe('Filters', () => {
  let component: Filters;
  let fixture: ComponentFixture<Filters>;

  const router = {
    navigate: vi.fn(),
  };

  const analytics = {
    track: vi.fn(),
  };

  const landmark: MapEntityMarker = {
    entityType: 'place',
    entityId: 42,
    title: 'City Walls',
    subtitle: 'Old Town',
    categoryLabel: 'Landmark',
    placeTypeId: 7,
    placeTypeLabel: 'Landmark',
    tagIds: [12],
    tagLabels: ['Historic'],
    imageUrl: null,
    detailRoute: '/places/42',
    latitude: 42.641,
    longitude: 18.11,
    distance: null,
    isSaved: false,
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    sessionStorage.clear();

    await TestBed.configureTestingModule({
      imports: [Filters],
      providers: [
        { provide: Router, useValue: router },
        { provide: AnalyticsService, useValue: analytics },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Filters);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders place types and recommended tags from shared map state', () => {
    const filterState = TestBed.inject(MapFilterStateService);
    filterState.rebuildAvailablePlaceTypes([
      landmark,
      { ...landmark, entityId: 43, title: 'Museum One', placeTypeId: 8, placeTypeLabel: 'Museum' },
      { ...landmark, entityId: 44, title: 'Museum Two', placeTypeId: 8, placeTypeLabel: 'Museum' },
    ], []);
    filterState.setRecommendedTagFilters([
      { id: 12, label: 'Historic', count: 3, score: 100 },
    ], []);

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Landmark');
    expect(text).toContain('Museum');
    expect(text).toContain('Historic');
  });

  it('allows only one primary place type at a time', () => {
    component.toggleCategory({ id: 'place-type:7', label: 'Landmark' });
    component.toggleCategory({ id: 'place-type:8', label: 'Museum' });

    expect(component.selectedCategories).toEqual(['place-type:8']);

    component.toggleCategory({ id: 'all', label: 'All' });
    expect(component.selectedCategories).toEqual([]);
  });

  it('persists a place type and selected recommended tags for the Map page', () => {
    const filterState = TestBed.inject(MapFilterStateService);
    filterState.rebuildAvailablePlaceTypes([landmark], []);
    const [historic] = filterState.setRecommendedTagFilters([
      { id: 12, label: 'Historic', count: 1, score: 100 },
    ], []);

    component.toggleCategory({ id: 'place-type:7', label: 'Landmark' });
    component.toggleMoreFilter(historic);
    component.applyFilters();

    expect(JSON.parse(sessionStorage.getItem('mapFilters') ?? '{}')).toEqual({
      categories: ['place-type:7'],
      categoryLabels: ['tag:12'],
    });
    expect(router.navigate).toHaveBeenCalledWith(['/map'], {
      state: { applyFilters: true, fromFilters: true },
    });
  });

  it('clears place type and recommended tag filters together', () => {
    const filterState = TestBed.inject(MapFilterStateService);
    const [historic] = filterState.setRecommendedTagFilters([
      { id: 12, label: 'Historic', count: 1, score: 100 },
    ], []);

    component.selectedCategories = ['place-type:7'];
    component.toggleMoreFilter(historic);
    component.applyFilters();
    component.clearAll();

    expect(sessionStorage.getItem('mapFilters')).toBeNull();
    expect(router.navigate).toHaveBeenLastCalledWith(['/map'], {
      state: { fromFilters: true, cleared: true },
    });
  });
});

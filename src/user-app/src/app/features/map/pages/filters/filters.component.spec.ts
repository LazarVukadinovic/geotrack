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

  const marker: MapEntityMarker = {
    entityType: 'place',
    entityId: 42,
    title: 'City Walls',
    subtitle: 'Old Town',
    categoryLabel: 'Landmark',
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

  it('renders dynamic backend map filters from shared map state', () => {
    const filterState = TestBed.inject(MapFilterStateService);
    filterState.rebuildAvailableMoreFilters([
      marker,
      { ...marker, entityId: 43, title: 'Museum One', categoryLabel: 'Museum' },
      { ...marker, entityId: 44, title: 'Museum Two', categoryLabel: 'Museum' },
    ], []);

    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Landmark');
    expect(text).toContain('Museum');
  });

  it('persists selected dynamic backend filters for the Map page', () => {
    const filterState = TestBed.inject(MapFilterStateService);
    const [landmark] = filterState.rebuildAvailableMoreFilters([marker], []);

    component.toggleMoreFilter(landmark);
    component.applyFilters();

    expect(JSON.parse(sessionStorage.getItem('mapFilters') ?? '{}')).toEqual({
      categories: [],
      categoryLabels: ['Landmark'],
    });
    expect(router.navigate).toHaveBeenCalledWith(['/map'], {
      state: { applyFilters: true, fromFilters: true },
    });
  });

  it('clears category and dynamic backend filters together', () => {
    const filterState = TestBed.inject(MapFilterStateService);
    const [landmark] = filterState.rebuildAvailableMoreFilters([marker], []);

    component.selectedCategories = ['places'];
    component.toggleMoreFilter(landmark);
    component.applyFilters();
    component.clearAll();

    expect(sessionStorage.getItem('mapFilters')).toBeNull();
    expect(router.navigate).toHaveBeenLastCalledWith(['/map'], {
      state: { fromFilters: true, cleared: true },
    });
  });
});

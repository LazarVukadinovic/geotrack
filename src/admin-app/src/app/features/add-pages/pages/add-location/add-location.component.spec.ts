import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';

import { NewLocationComponent } from './add-location.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { LocationsListService } from '../../../../core/services/locations-list.service';
import { AuthService } from '../../../../core/services/auth.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { ZoneDrawService } from '../../../../core/services/zone-draw.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import {
  RegionalLocationCreateRequest,
  RegionalLocationParentDestinationDto,
  RegionalLocationTagDto,
  RegionalLocationTypeDto,
} from '../../../../core/models/location.model';

@Component({
  standalone: true,
  template: '',
})
class EmptyRouteComponent {}

describe('NewLocationComponent', () => {
  let component: NewLocationComponent;
  let fixture: ComponentFixture<NewLocationComponent>;
  let locationsService: {
    getParentDestinations: ReturnType<typeof vi.fn>;
    getLocationTypes: ReturnType<typeof vi.fn>;
    getLocationTags: ReturnType<typeof vi.fn>;
    searchLocationTags: ReturnType<typeof vi.fn>;
    createLocation: ReturnType<typeof vi.fn>;
  };

  const regions: RegionalAssignedRegion[] = [
    { id: 7, name: 'North', displayName: 'North, Testland', territoryName: 'Testland' },
  ];

  const parentDestinations: RegionalLocationParentDestinationDto[] = [
    { id: 11, name: 'Backend Destination', displayName: 'Backend Destination', status: 'Published' },
  ];

  const locationTypes: RegionalLocationTypeDto[] = [
    { id: 3, name: 'District', description: 'District type' },
  ];

  const tags: RegionalLocationTagDto[] = [
    { id: 1, name: 'Backend Nature', slug: 'backend-nature' },
    { id: 2, name: 'Historic', slug: 'historic' },
    { id: 3, name: 'Walking Area', slug: 'walking-area' },
  ];

  const searchedTag: RegionalLocationTagDto = {
    id: 4,
    name: 'Search Result Tag',
    slug: 'search-result-tag',
  };

  const buildTags = (count: number): RegionalLocationTagDto[] =>
    Array.from({ length: count }, (_, index) => ({
      id: index + 1,
      name: `Backend Tag ${String(index + 1).padStart(2, '0')}`,
      slug: `backend-tag-${index + 1}`,
    }));

  beforeEach(async () => {
    locationsService = {
      getParentDestinations: vi.fn(),
      getLocationTypes: vi.fn(),
      getLocationTags: vi.fn(),
      searchLocationTags: vi.fn(),
      createLocation: vi.fn(),
    };
    locationsService.getParentDestinations.mockReturnValue(of(parentDestinations));
    locationsService.getLocationTypes.mockReturnValue(of(locationTypes));
    locationsService.getLocationTags.mockReturnValue(of(tags));
    locationsService.searchLocationTags.mockReturnValue(of([searchedTag]));
    locationsService.createLocation.mockReturnValue(of({
      id: 44,
      name: 'Created',
      status: 'Published',
      regionId: 7,
      regionDisplayName: 'North, Testland',
      destinationId: 11,
      destinationName: 'Backend Destination',
      locationTypeId: 3,
      createdAt: new Date().toISOString(),
      latitude: 43,
      longitude: 19,
    }));

    await TestBed.configureTestingModule({
      imports: [NewLocationComponent],
      providers: [
        provideRouter([{ path: 'locations', component: EmptyRouteComponent }]),
        {
          provide: DashboardService,
          useValue: {
            getAssignedRegions: () => of(regions),
          },
        },
        { provide: LocationsListService, useValue: locationsService },
        {
          provide: AuthService,
          useValue: {
            fullName: 'Manager User',
            getUser: () => ({ email: 'manager@example.com', role: 'RegionalManager' }),
            isNationalManager: () => false,
            logout: vi.fn(),
          },
        },
        {
          provide: LightboxService,
          useValue: {
            open: vi.fn(),
            close: vi.fn(),
          },
        },
        {
          provide: ZoneDrawService,
          useValue: {
            drawHints: {
              polygon: 'polygon',
              rectangle: 'rectangle',
              circle: 'circle',
            },
            calculateAreaKm2: () => 12,
            extractZoneResult: () => ({
              zone: [
                [43, 19],
                [43, 19.1],
                [43.1, 19.1],
              ],
              centerLat: 43.05,
              centerLng: 19.05,
            }),
            activateDrawTool: vi.fn(),
            cloneLayer: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NewLocationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads assigned region and region-scoped parent destinations', () => {
    expect(component.activeRegionId).toBe(7);
    expect(component.regionDisplayName).toBe('North, Testland');
    expect(locationsService.getParentDestinations).toHaveBeenCalledWith(7);

    const regionInput: HTMLInputElement | null = fixture.nativeElement.querySelector('input[readonly]');
    expect(regionInput?.value).toContain('North, Testland');
    expect(component.parentDestinations[0].id).toBe(11);
  });

  it('disables publish without active region or parent destination', () => {
    component.activeRegionId = null;
    component.parentDestinationId = null;
    fixture.detectChanges();

    const buttons = Array.from(fixture.nativeElement.querySelectorAll('button')) as HTMLButtonElement[];
    const publishButton = buttons.find(button => button.textContent?.includes('Publish'));
    expect(component.canPublish).toBe(false);
    expect(publishButton?.disabled).toBe(true);
  });

  it('renders internal reference id as read-only', () => {
    const readonlyInputs = Array.from(fixture.nativeElement.querySelectorAll('input[readonly]')) as HTMLInputElement[];
    expect(readonlyInputs.some(input => input.value === 'Generated automatically after publish')).toBe(true);
  });

  it('loads location types and location-compatible tags from backend services', () => {
    expect(component.locationTypes.map(type => type.name)).toEqual(['District']);
    expect(component.classItems.map(item => item.tag.name)).toEqual([
      'Backend Nature',
      'Historic',
      'Walking Area',
    ]);
  });

  it('supports additional location tag search', async () => {
    vi.useFakeTimers();

    component.tagInput = 'walk';
    component.onTagInputChange();
    vi.advanceTimersByTime(300);
    await Promise.resolve();

    expect(locationsService.searchLocationTags).toHaveBeenCalledWith('walk');
    expect(component.additionalTagResults[0].id).toBe(4);
    vi.useRealTimers();
  });

  it('uses five initial and ten expanded classification tags', () => {
    const manyTags = buildTags(12);
    component.classItems = manyTags.map(tag => ({ tag, checked: false }));

    expect(component.visibleClassItems.map(item => item.tag.id)).toEqual([1, 2, 3, 4, 5]);

    component.toggleClassificationExpanded();

    expect(component.visibleClassItems.map(item => item.tag.id)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  });

  it('does not render runtime hardcoded classification tags that were not returned by the backend', () => {
    const labels = component.classItems.map(item => item.tag.name);

    expect(labels).toContain('Backend Nature');
    expect(labels).not.toContain('Nature & Outdoors');
    expect(labels).not.toContain('Adventure & Sport');
    expect(labels).not.toContain('Family Friendly');
  });

  it('reloads parent destinations and clears invalid parent when active region changes', () => {
    component.parentDestinationId = 11;
    component.assignedRegions = [
      ...regions,
      { id: 8, name: 'South', displayName: 'South, Testland', territoryName: 'Testland' },
    ];
    locationsService.getParentDestinations.mockReturnValue(of([
      { id: 22, name: 'Other Destination', displayName: 'Other Destination', status: 'Published' },
    ]));

    component.onRegionSelected(8);

    expect(component.activeRegionId).toBe(8);
    expect(locationsService.getParentDestinations).toHaveBeenCalledWith(8);
    expect(component.parentDestinationId).toBeNull();
  });

  it('submits selected tags and parent destination without internal reference id', () => {
    component.locationName = 'Created location';
    component.summary = 'A short summary';
    component.parentDestinationId = 11;
    component.locationTypeId = 3;
    const backendTag = component.classItems.find(item => item.tag.id === 1);
    expect(backendTag).toBeTruthy();
    backendTag!.checked = true;
    component.onClassItemChange(backendTag!);
    component.selectAdditionalTag(searchedTag);
    component.zoneConfirmed = true;
    (component as any).confirmedLayer = {};
    (component as any).confirmedShapeType = 'Polygon';
    component.editorRef.nativeElement.innerHTML = '<p>Description</p>';

    component.publish();

    expect(locationsService.createLocation).toHaveBeenCalled();
    const payload = locationsService.createLocation.mock.calls.at(-1)?.[0] as RegionalLocationCreateRequest & {
      internalReferenceId?: string;
    };
    expect(payload.regionId).toBe(7);
    expect(payload.destinationId).toBe(11);
    expect(payload.locationTypeId).toBe(3);
    expect(payload.tagIds.sort()).toEqual([1, 4]);
    expect(payload.internalReferenceId).toBeUndefined();
  });

  it('clears loading and shows safe error when create fails', () => {
    locationsService.createLocation.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: { message: 'Validation failed.' },
    })));

    component.locationName = 'Created location';
    component.parentDestinationId = 11;
    component.zoneConfirmed = true;
    (component as any).confirmedLayer = {};
    (component as any).confirmedShapeType = 'Polygon';

    component.publish();

    expect(component.isSaving).toBe(false);
    expect(component.errorMessage).toBe('Validation failed.');
  });
});

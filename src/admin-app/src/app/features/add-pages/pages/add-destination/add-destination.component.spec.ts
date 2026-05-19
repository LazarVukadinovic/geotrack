import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { vi } from 'vitest';

import { AddDestinationComponent } from './add-destination.component';
import { DashboardService } from '../../../../core/services/dashboard.service';
import { DestinationsListService } from '../../../../core/services/destinations-list.service';
import { AuthService } from '../../../../core/services/auth.service';
import { LightboxService } from '../../../../core/services/lightbox.service';
import { ZoneDrawService } from '../../../../core/services/zone-draw.service';
import { RegionalAssignedRegion } from '../../../../core/models/dashboard.model';
import { RegionalDestinationCreateRequest, RegionalDestinationTagDto } from '../../../../core/models/destination.model';

@Component({
  standalone: true,
  template: '',
})
class EmptyRouteComponent {}

describe('AddDestinationComponent', () => {
  let component: AddDestinationComponent;
  let fixture: ComponentFixture<AddDestinationComponent>;
  let destinationsService: {
    getDestinationTags: ReturnType<typeof vi.fn>;
    searchDestinationTags: ReturnType<typeof vi.fn>;
    createDestination: ReturnType<typeof vi.fn>;
  };

  const regions: RegionalAssignedRegion[] = [
    { id: 7, name: 'North', displayName: 'North, Testland', territoryName: 'Testland' },
  ];

  const tags: RegionalDestinationTagDto[] = [
    { id: 1, name: 'Backend Coast', slug: 'backend-coast' },
    { id: 2, name: 'Culture & Heritage', slug: 'culture-heritage' },
    { id: 3, name: 'Historic', slug: 'historic' },
  ];
  const searchedTag: RegionalDestinationTagDto = {
    id: 4,
    name: 'Search Result Tag',
    slug: 'search-result-tag',
  };

  const buildTags = (count: number): RegionalDestinationTagDto[] =>
    Array.from({ length: count }, (_, index) => ({
      id: index + 1,
      name: `Backend Tag ${String(index + 1).padStart(2, '0')}`,
      slug: `backend-tag-${index + 1}`,
    }));

  beforeEach(async () => {
    destinationsService = {
      getDestinationTags: vi.fn(),
      searchDestinationTags: vi.fn(),
      createDestination: vi.fn(),
    };
    destinationsService.getDestinationTags.mockReturnValue(of(tags));
    destinationsService.searchDestinationTags.mockReturnValue(of([searchedTag]));
    destinationsService.createDestination.mockReturnValue(of({
      id: 44,
      name: 'Created',
      status: 'Published',
      regionId: 7,
      regionDisplayName: 'North, Testland',
      destinationTypeId: 1,
      createdAt: new Date().toISOString(),
      latitude: 43,
      longitude: 19,
    }));

    await TestBed.configureTestingModule({
      imports: [AddDestinationComponent],
      providers: [
        provideRouter([{ path: 'destinations', component: EmptyRouteComponent }]),
        {
          provide: DashboardService,
          useValue: {
            getAssignedRegions: () => of(regions),
          },
        },
        { provide: DestinationsListService, useValue: destinationsService },
        {
          provide: AuthService,
          useValue: {
            fullName: 'Manager User',
            getUser: () => ({ email: 'manager@example.com', role: 'RegionalManager' }),
            isTerritoryManager: () => false,
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

    fixture = TestBed.createComponent(AddDestinationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads assigned regions and locks the displayed region field', () => {
    expect(component.activeRegionId).toBe(7);
    expect(component.regionDisplayName).toBe('North, Testland');

    const regionInput: HTMLInputElement | null = fixture.nativeElement.querySelector('input[readonly]');
    expect(regionInput?.value).toContain('North, Testland');
  });

  it('disables publish when no assigned region is available', () => {
    component.activeRegionId = null;
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

  it('loads destination tags and supports additional tag search', async () => {
    vi.useFakeTimers();
    expect(component.classItems.map(item => item.tag.name)).toEqual([
      'Backend Coast',
      'Culture & Heritage',
      'Historic',
    ]);

    component.tagInput = 'hist';
    component.onTagInputChange();
    vi.advanceTimersByTime(300);
    await Promise.resolve();

    expect(destinationsService.searchDestinationTags).toHaveBeenCalledWith('hist');
    expect(component.additionalTagResults[0].id).toBe(4);
    vi.useRealTimers();
  });

  it('initially renders only five classification tags when more are available', () => {
    const manyTags = buildTags(12);
    component.classItems = manyTags.map(tag => ({ tag, checked: false }));
    fixture.detectChanges();

    const pageText = fixture.nativeElement.textContent as string;
    expect(component.visibleClassItems.map(item => item.tag.id)).toEqual([1, 2, 3, 4, 5]);
    expect(pageText).toContain('Backend Tag 01');
    expect(pageText).toContain('Backend Tag 05');
    expect(pageText).not.toContain('Backend Tag 06');
    expect(pageText).toContain('Show more tags');
    expect(pageText).toContain('Showing 5 of 12 tags');
  });

  it('expands classification tags up to ten and collapses back to five', () => {
    const manyTags = buildTags(12);
    component.classItems = manyTags.map(tag => ({ tag, checked: false }));

    component.toggleClassificationExpanded();
    fixture.detectChanges();

    let pageText = fixture.nativeElement.textContent as string;
    expect(component.visibleClassItems.map(item => item.tag.id)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
    expect(pageText).toContain('Backend Tag 10');
    expect(pageText).not.toContain('Backend Tag 11');
    expect(pageText).toContain('Show less');
    expect(pageText).toContain('Showing 10 of 12 tags');

    component.toggleClassificationExpanded();
    fixture.detectChanges();

    pageText = fixture.nativeElement.textContent as string;
    expect(component.visibleClassItems.map(item => item.tag.id)).toEqual([1, 2, 3, 4, 5]);
    expect(pageText).not.toContain('Backend Tag 06');
    expect(pageText).toContain('Show more tags');
  });

  it('does not show a classification expand control when five or fewer tags exist', () => {
    const fiveTags = buildTags(5);
    component.classItems = fiveTags.map(tag => ({ tag, checked: false }));
    fixture.detectChanges();

    const pageText = fixture.nativeElement.textContent as string;
    expect(component.showClassificationToggle).toBe(false);
    expect(pageText).not.toContain('Show more tags');
    expect(pageText).not.toContain('Showing 5 of 5 tags');
  });

  it('preserves selected classification tags while expanding and collapsing', () => {
    const manyTags = buildTags(12);
    component.classItems = manyTags.map(tag => ({ tag, checked: false }));
    const selectedHiddenItem = component.classItems[7];

    selectedHiddenItem.checked = true;
    component.onClassItemChange(selectedHiddenItem);
    component.toggleClassificationExpanded();
    component.toggleClassificationExpanded();
    fixture.detectChanges();

    expect(component.selectedTagIds.has(selectedHiddenItem.tag.id)).toBe(true);
    expect(component.classItems[7].checked).toBe(true);
  });

  it('does not render runtime hardcoded classification tags that were not returned by the backend', () => {
    const labels = component.classItems.map(item => item.tag.name);

    expect(labels).toContain('Backend Coast');
    expect(labels).not.toContain('Nature & Outdoors');
    expect(labels).not.toContain('Adventure & Sport');
    expect(labels).not.toContain('Family Friendly');
  });

  it('submits selected tags with the active region and without internal reference id', () => {
    component.destinationName = 'Created destination';
    component.summary = 'A short summary';
    const backendTag = component.classItems.find(item => item.tag.id === 1);
    expect(backendTag).toBeTruthy();
    backendTag!.checked = true;
    component.onClassItemChange(backendTag!);
    component.selectAdditionalTag(searchedTag);
    expect(component.classItems.find(item => item.tag.id === searchedTag.id)?.checked).toBe(true);
    component.zoneConfirmed = true;
    (component as any).confirmedLayer = {};
    (component as any).confirmedShapeType = 'Polygon';
    component.editorRef.nativeElement.innerHTML = '<p>Description</p>';

    component.publish();

    expect(destinationsService.createDestination).toHaveBeenCalled();
    const payload = destinationsService.createDestination.mock.calls.at(-1)?.[0] as RegionalDestinationCreateRequest & {
      internalReferenceId?: string;
    };
    expect(payload.regionId).toBe(7);
    expect(payload.tagIds.sort()).toEqual([1, 4]);
    expect(payload.internalReferenceId).toBeUndefined();
  });

  it('clears loading and shows a safe error when create fails', () => {
    destinationsService.createDestination.mockReturnValue(throwError(() => new HttpErrorResponse({
      status: 400,
      error: { message: 'Validation failed.' },
    })));

    component.destinationName = 'Created destination';
    component.zoneConfirmed = true;
    (component as any).confirmedLayer = {};
    (component as any).confirmedShapeType = 'Polygon';

    component.publish();

    expect(component.isSaving).toBe(false);
    expect(component.errorMessage).toBe('Validation failed.');
  });
});

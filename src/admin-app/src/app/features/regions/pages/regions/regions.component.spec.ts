import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { AllRegionsComponent } from './regions.component';
import { AuthService } from '../../../../core/services/auth.service';
import { TerritoryDashboardService } from '../../../../core/services/territory-dashboard.service';
import { RegionsService } from '../../../../core/services/national/regions.service';

describe('AllRegionsComponent', () => {
  let component: AllRegionsComponent;
  let fixture: ComponentFixture<AllRegionsComponent>;

  const authService = {
    fullName: 'Mika Mikic',
    getUser: vi.fn(() => ({ role: 'Territory Manager', region: '' })),
    isTerritoryManager: vi.fn(() => true),
    logout: vi.fn(),
  };

  const territoryService = {
    getAssignedTerritories: vi.fn(() => of([{ id: 10, name: 'Croatia', displayName: 'Croatia' }])),
    getActiveTerritoryId: vi.fn(() => 10),
    setActiveTerritoryId: vi.fn(),
  };

  const regionsService = {
    getListState: vi.fn(() => null),
    clearListState: vi.fn(),
    saveListState: vi.fn(),
    setActiveTerritoryId: vi.fn(),
    getPage$: vi.fn(() => of({
      items: [
        {
          id: '11',
          name: 'Dalmatia',
          description: 'Regional Manager: Lead',
          isTop: false,
          destinations: 2,
          locations: 3,
          places: 4,
          events: 5,
          assignedPersons: 1,
          status: 'Active',
          imageBg: 'linear-gradient(135deg,#dbeafe 0%,#bfdbfe 100%)',
          pendingRequests: 0,
        },
      ],
      total: 1,
    })),
  };

  beforeEach(async () => {
    vi.clearAllMocks();
    await TestBed.configureTestingModule({
      imports: [AllRegionsComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authService },
        { provide: TerritoryDashboardService, useValue: territoryService },
        { provide: RegionsService, useValue: regionsService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AllRegionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders real read-only region rows from the service', () => {
    const text = fixture.nativeElement.textContent;

    expect(text).toContain('Regional Management');
    expect(text).toContain('Dalmatia');
    expect(text).not.toContain('Delete Region');
  });
});

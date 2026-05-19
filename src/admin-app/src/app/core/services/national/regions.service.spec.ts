import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { vi } from 'vitest';

import { RegionsService } from './regions.service';
import { TerritoryDashboardService } from '../territory-dashboard.service';

describe('RegionsService', () => {
  let service: RegionsService;
  let territoryDashboard: {
    getActiveTerritoryId: ReturnType<typeof vi.fn>;
    setActiveTerritoryId: ReturnType<typeof vi.fn>;
    getRegions: ReturnType<typeof vi.fn>;
    getRegionDetail: ReturnType<typeof vi.fn>;
    getRegionContent: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    territoryDashboard = {
      getActiveTerritoryId: vi.fn(() => 10),
      setActiveTerritoryId: vi.fn(),
      getRegions: vi.fn(() => of({
        territory: { id: 10, name: 'Croatia', displayName: 'Croatia' },
        items: [
          {
            id: 11,
            regionId: 11,
            name: 'Dalmatia',
            regionName: 'Dalmatia',
            displayName: 'Dalmatia',
            description: null,
            territoryName: 'Croatia',
            territoryId: 10,
            territoryDisplayName: 'Croatia',
            status: 'Active',
            managerNames: ['Lead Manager'],
            destinationsCount: 2,
            locationsCount: 3,
            placesCount: 4,
            staysCount: 1,
            eventsCount: 5,
            pendingRequestsCount: 1,
            assignedPersonsCount: 1,
            activeManagersCount: 1,
            managerDisplayName: 'Lead Manager',
            assignedManagers: [
              {
                userId: 3,
                displayName: 'Lead Manager',
                email: 'lead@example.com',
                initials: 'LM',
                avatarUrl: '/media/uploads/avatars/lead.png',
                role: 'RegionalManager',
                assignedAt: '2026-05-01T00:00:00Z',
              },
            ],
            healthScore: 82,
            activityScore: 80,
            imageUrl: null,
            detailRoute: '/territory/regions/11',
            createdAt: '2026-05-01T00:00:00Z',
          },
        ],
        totalCount: 1,
        skip: 0,
        take: 25,
        hasMore: false,
      })),
      getRegionDetail: vi.fn(),
      getRegionContent: vi.fn(),
    };

    TestBed.configureTestingModule({
      providers: [
        { provide: TerritoryDashboardService, useValue: territoryDashboard },
      ],
    });
    service = TestBed.inject(RegionsService);
  });

  it('maps territory region list responses to read-only list items', () => {
    service.getPage$({
      territoryId: 10,
      search: '',
      sort: 'healthHigh',
      skip: 0,
      take: 25,
    }).subscribe(page => {
      expect(page.total).toBe(1);
      expect(page.items[0]).toMatchObject({
        id: '11',
        regionId: 11,
        name: 'Dalmatia',
        destinations: 2,
        locations: 3,
        places: 4,
        stays: 1,
        events: 5,
        assignedPersons: 1,
        assignedManagers: [
          expect.objectContaining({
            initials: 'LM',
            avatarUrl: '/media/uploads/avatars/lead.png',
          }),
        ],
        status: 'Active',
        pendingRequests: 1,
        healthScore: 82,
        detailRoute: '/territory/regions/11',
      });
    });

    expect(territoryDashboard.getRegions).toHaveBeenCalledWith({
      territoryId: 10,
      search: '',
      sort: 'health-high',
      skip: 0,
      take: 25,
    });
  });
});

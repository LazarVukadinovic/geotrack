import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  TerritoryMapBoundaryFeatureCollection,
  TerritoryMapFeatureCollection,
  TerritoryMapStatusCollection,
} from '../../models/administrator/territory.model';
import { TerritoryService } from './territory.service';

describe('TerritoryService', () => {
  let service: TerritoryService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TerritoryService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('caches the admin territory map response within the session', () => {
    const received: TerritoryMapFeatureCollection[] = [];

    service.getMap$().subscribe(collection => received.push(collection));
    service.getMap$().subscribe(collection => received.push(collection));

    const boundaries = http.expectOne('/api/admin/territories/map/boundaries');
    expect(boundaries.request.method).toBe('GET');
    boundaries.flush(boundaryCollection());

    const status = http.expectOne('/api/admin/territories/map/status');
    expect(status.request.method).toBe('GET');
    status.flush(statusCollection('Available'));

    expect(received.length).toBe(2);
    expect(received[0].features[0].properties.status).toBe('Available');
    http.expectNone('/api/admin/territories/map/boundaries');
    http.expectNone('/api/admin/territories/map/status');
  });

  it('bypasses cached boundaries and status when the full map is refreshed', () => {
    service.getMap$().subscribe();
    http.expectOne('/api/admin/territories/map/boundaries').flush(boundaryCollection());
    http.expectOne('/api/admin/territories/map/status').flush(statusCollection('Available'));

    let refreshedStatus = '';
    service.refreshMap$().subscribe(collection => {
      refreshedStatus = collection.features[0].properties.status;
    });

    const boundaries = http.expectOne(req =>
      req.url === '/api/admin/territories/map/boundaries' &&
      req.params.has('_') &&
      req.headers.get('Cache-Control') === 'no-cache');
    boundaries.flush(boundaryCollection());

    const status = http.expectOne(req =>
      req.url === '/api/admin/territories/map/status' &&
      req.params.has('_') &&
      req.headers.get('Cache-Control') === 'no-cache');
    status.flush(statusCollection('Assigned'));

    expect(refreshedStatus).toBe('Assigned');
  });

  it('refreshes assignment status without reloading cached boundary geometry', () => {
    service.getMap$().subscribe();
    http.expectOne('/api/admin/territories/map/boundaries').flush(boundaryCollection());
    http.expectOne('/api/admin/territories/map/status').flush(statusCollection('Available'));

    let refreshedStatus = '';
    service.refreshMapStatus$().subscribe(collection => {
      refreshedStatus = collection.features[0].properties.status;
    });

    http.expectNone('/api/admin/territories/map/boundaries');
    const status = http.expectOne(req =>
      req.url === '/api/admin/territories/map/status' &&
      req.params.has('_') &&
      req.headers.get('Cache-Control') === 'no-cache');
    status.flush(statusCollection('Assigned'));

    expect(refreshedStatus).toBe('Assigned');
  });

  function boundaryCollection(): TerritoryMapBoundaryFeatureCollection {
    return {
      type: 'FeatureCollection',
      attribution: 'Boundary data provided by geoBoundaries (gbOpen), licensed under CC BY 4.0.',
      generatedAt: '2026-05-26T00:00:00Z',
      features: [
        {
          type: 'Feature',
          geometry: {
            type: 'Polygon',
            coordinates: [
              [
                [20, 10],
                [21, 10],
                [21, 11],
                [20, 11],
                [20, 10],
              ],
            ],
          },
          properties: {
            territoryId: 101,
            countryId: 101,
            territoryKind: 'Country',
            iso2: 'AA',
            iso3: 'AAA',
            countryCode: 'AA',
            name: 'Backend Territory',
            displayName: 'Backend Territory',
            shapeGroup: 'AAA',
            shapeType: 'ADM0',
            shapeId: 'AAA-ADM0',
            hasDisplayBoundary: true,
            viewport: { isEmpty: false },
          },
        },
      ],
    };
  }

  function statusCollection(status: 'Assigned' | 'Available'): TerritoryMapStatusCollection {
    return {
      generatedAt: '2026-05-26T00:00:01Z',
      items: [
        {
          territoryId: 101,
          countryId: 101,
          territoryKind: 'Country',
          iso2: 'AA',
          iso3: 'AAA',
          countryCode: 'AA',
          name: 'Backend Territory',
          displayName: 'Backend Territory',
          shapeGroup: 'AAA',
          shapeType: 'ADM0',
          shapeId: 'AAA-ADM0',
          status,
          assignedManager: status === 'Assigned'
            ? {
                id: '11',
                userId: 11,
                displayName: 'Candidate User',
                name: 'Candidate User',
                email: 'candidate@example.com',
                initials: 'CU',
              }
            : null,
          hasBoundary: true,
          hasValidationBoundary: true,
          hasDisplayBoundary: true,
          canAssign: status === 'Available',
          canEdit: status === 'Assigned',
          boundaryRequiredMessage: null,
          boundaryMissingReason: null,
        },
      ],
    };
  }
});

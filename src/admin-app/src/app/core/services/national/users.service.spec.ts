import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { UsersService } from './users.service';

describe('UsersService', () => {
  let service: UsersService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(UsersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('loads territory users from the backend with search sort and paging', () => {
    let resultTotal = 0;

    service.getPage$({
      territoryId: 12,
      search: 'ana',
      sort: 'name_asc',
      skip: 8,
      take: 8,
    }).subscribe(page => {
      resultTotal = page.total;
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/users' &&
      req.params.get('territoryId') === '12' &&
      req.params.get('search') === 'ana' &&
      req.params.get('sort') === 'name_asc' &&
      req.params.get('skip') === '8' &&
      req.params.get('take') === '8');

    expect(request.request.method).toBe('GET');
    request.flush({
      items: [],
      totalCount: 3,
      skip: 8,
      take: 8,
      hasMore: false,
    });

    expect(resultTotal).toBe(3);
  });

  it('updates users and region assignments through territory endpoints', () => {
    service.updateUser$(42, {
      territoryId: 7,
      firstName: 'Mila',
      lastName: 'Markovic',
      regionIdsToAdd: [3, 4],
    }).subscribe();

    const update = http.expectOne('/api/admin/territory/users/42');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body.territoryId).toBe(7);
    expect(update.request.body.displayName).toBeUndefined();
    expect(update.request.body.regionIdsToAdd).toEqual([3, 4]);
    update.flush({ userId: 42, assignedRegions: [] });

    service.addRegion$(42, 7, 3).subscribe();
    const add = http.expectOne('/api/admin/territory/users/42/regions');
    expect(add.request.method).toBe('POST');
    expect(add.request.body).toEqual({ territoryId: 7, regionId: 3 });
    add.flush({ userId: 42, assignedRegions: [] });

    service.removeManagerRole$(42, 7).subscribe();
    const remove = http.expectOne('/api/admin/territory/users/42/remove-manager-role');
    expect(remove.request.method).toBe('POST');
    expect(remove.request.body).toEqual({ territoryId: 7 });
    remove.flush({ userId: 42, assignedRegions: [] });
  });

  it('searches assignable users for role assignment modals', () => {
    let resultCount = 0;

    service.searchAssignableUsers$(7, 'mila', 5, {
      excludeAlreadyRegionalManager: true,
      excludeAlreadyTerritoryAssociate: true,
    }).subscribe(users => {
      resultCount = users.length;
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '7' &&
      req.params.get('query') === 'mila' &&
      req.params.get('take') === '5' &&
      req.params.get('excludeAlreadyRegionalManager') === 'true' &&
      req.params.get('excludeAlreadyTerritoryAssociate') === 'true');
    expect(request.request.method).toBe('GET');
    request.flush({
      items: [{
        userId: 42,
        displayName: 'Mila Markovic',
        firstName: 'Mila',
        lastName: 'Markovic',
        email: 'mila@example.com',
        initials: 'MM',
        roles: ['RegisteredUser'],
        assignedRegions: [],
        assignedTerritories: [],
        isActive: true,
      }],
    });

    expect(resultCount).toBe(1);
  });

  it('posts regional manager and territory associate assignment requests', () => {
    service.addRegionalManager$(42, 7, [3, 4]).subscribe();
    const addRegionalManager = http.expectOne('/api/admin/territory/users/42/regional-manager');
    expect(addRegionalManager.request.method).toBe('POST');
    expect(addRegionalManager.request.body).toEqual({ territoryId: 7, regionIds: [3, 4] });
    addRegionalManager.flush({ userId: 42, assignedRegions: [] });

    service.addTerritoryAssociate$(42, 7).subscribe();
    const addAssociate = http.expectOne('/api/admin/territory/users/42/territory-associate');
    expect(addAssociate.request.method).toBe('POST');
    expect(addAssociate.request.body).toEqual({ territoryId: 7 });
    addAssociate.flush({ userId: 42, assignedRegions: [] });

    service.removeTerritoryAssociate$(42, 7).subscribe();
    const removeAssociate = http.expectOne('/api/admin/territory/users/42/territory-associate/remove');
    expect(removeAssociate.request.method).toBe('POST');
    expect(removeAssociate.request.body).toEqual({ territoryId: 7 });
    removeAssociate.flush({ userId: 42, assignedRegions: [] });
  });

  it('loads only available regions for a user in the active territory', () => {
    let regionCount = 0;

    service.getAvailableRegions$(42, 7, 'north').subscribe(regions => {
      regionCount = regions.length;
    });

    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/users/42/available-regions' &&
      req.params.get('territoryId') === '7' &&
      req.params.get('search') === 'north');
    expect(request.request.method).toBe('GET');
    request.flush({
      items: [{ regionId: 5, regionName: 'North', displayName: 'North' }],
    });

    expect(regionCount).toBe(1);
  });
});

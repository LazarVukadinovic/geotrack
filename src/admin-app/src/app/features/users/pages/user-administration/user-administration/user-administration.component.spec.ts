import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UserAdministrationComponent } from './user-administration.component';
import { TerritoryUser } from '../../../../../core/services/national/users.service';

describe('UserAdministrationComponent', () => {
  let component: UserAdministrationComponent;
  let fixture: ComponentFixture<UserAdministrationComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserAdministrationComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UserAdministrationComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    http.verify();
  });

  it('loads real backend users for the active territory', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([buildUser(1, 'Ana Manager')], 1);
    fixture.detectChanges();

    expect(component.items.length).toBe(1);
    expect(component.totalItems).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('ana.manager@example.com');
  });

  it('search and sort trigger backend reloads', fakeAsync(() => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([buildUser(1, 'Ana Manager')], 1);

    component.searchQuery = 'banat';
    component.onSearchChange();
    tick(300);

    const searchRequest = http.expectOne(req =>
      req.url === '/api/admin/territory/users' &&
      req.params.get('search') === 'banat' &&
      req.params.get('territoryId') === '10');
    expect(searchRequest.request.method).toBe('GET');
    searchRequest.flush({ items: [], totalCount: 0, skip: 0, take: 8, hasMore: false });

    component.applySortOption({ label: 'Name: A-Z', value: 'name_asc' });
    const sortRequest = http.expectOne(req =>
      req.url === '/api/admin/territory/users' &&
      req.params.get('sort') === 'name_asc');
    expect(sortRequest.request.method).toBe('GET');
    sortRequest.flush({ items: [], totalCount: 0, skip: 0, take: 8, hasMore: false });
  }));

  it('show more appends users', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers(Array.from({ length: 8 }, (_, index) => buildUser(index + 1, `User ${index + 1}`)), 10);

    component.loadMore();
    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/users' &&
      req.params.get('skip') === '8' &&
      req.params.get('take') === '8');
    request.flush({
      items: [buildUser(9, 'User 9'), buildUser(10, 'User 10')],
      totalCount: 10,
      skip: 8,
      take: 8,
      hasMore: false,
    });

    expect(component.items.length).toBe(10);
  });

  it('renders avatar images and initials fallback', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([
      buildUser(1, 'Avatar User', '/media/uploads/avatars/avatar.png'),
      buildUser(2, 'Initials User'),
    ], 2);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('img[src="/media/uploads/avatars/avatar.png"]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('IU');
  });

  it('opens add region and remove manager role flows', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(1, 'Ana Manager');
    flushUsers([user], 1);

    component.openAddRegionModal(user);
    const available = http.expectOne(req =>
      req.url === '/api/admin/territory/users/1/available-regions' &&
      req.params.get('territoryId') === '10');
    available.flush({
      items: [{ regionId: 22, regionName: 'North', displayName: 'North' }],
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('North');

    component.selectedRegionId = 22;
    component.addSelectedRegion();
    const add = http.expectOne('/api/admin/territory/users/1/regions');
    expect(add.request.body).toEqual({ territoryId: 10, regionId: 22 });
    add.flush({ ...user, assignedRegions: [...user.assignedRegions, { regionId: 22, regionName: 'North', displayName: 'North' }] });

    component.openRemoveManagerConfirm(user);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('The user account will not be deleted.');
  });

  function flushTerritories(): void {
    const request = http.expectOne('/api/admin/territory/territories');
    expect(request.request.method).toBe('GET');
    request.flush([{ id: 10, name: 'Test Territory', displayName: 'Test Territory', statusLabel: 'Assigned' }]);
  }

  function flushUsers(items: TerritoryUser[], totalCount: number): void {
    const request = http.expectOne(req =>
      req.url === '/api/admin/territory/users' &&
      req.params.get('territoryId') === '10' &&
      req.params.get('skip') === '0' &&
      req.params.get('take') === '8');
    expect(request.request.method).toBe('GET');
    request.flush({
      territory: { id: 10, name: 'Test Territory', displayName: 'Test Territory' },
      items,
      totalCount,
      skip: 0,
      take: 8,
      hasMore: items.length < totalCount,
    });
  }

  function buildUser(userId: number, displayName: string, avatarUrl?: string): TerritoryUser {
    const [firstName, lastName = ''] = displayName.split(' ');
    return {
      userId,
      displayName,
      firstName,
      lastName,
      email: `${displayName.toLowerCase().replace(/\s+/g, '.')}@example.com`,
      avatarUrl,
      initials: displayName.split(/\s+/).map(part => part[0]).join('').slice(0, 2).toUpperCase(),
      roleCode: 'RegionalManager',
      roleDisplayName: 'Regional Manager',
      assignedRegions: [{ regionId: 11, regionName: 'Region One', displayName: 'Region One' }],
      status: 'Active',
      isActive: true,
      lastLoginAt: '2026-05-18T10:00:00Z',
      lastLoginDisplay: '1 day ago',
      canEdit: true,
      canAddRegion: true,
      canRemoveManagerRole: true,
      createdAt: '2026-01-01T00:00:00Z',
    };
  }
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { UserAdministrationComponent } from './user-administration.component';
import { TerritoryAssignableUser, TerritoryUser } from '../../../../../core/services/national/users.service';
import { AuthService } from '../../../../../core/services/auth.service';

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

  it('search and sort trigger backend reloads', async () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([buildUser(1, 'Ana Manager')], 1);

    component.searchQuery = 'banat';
    component.onSearchChange();
    await new Promise(resolve => setTimeout(resolve, 310));

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
  });

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

    expect(component.editUser?.userId).toBe(user.userId);
    expect(fixture.nativeElement.textContent).toContain('Assigned Regions');
    expect(fixture.nativeElement.textContent).toContain('North');

    component.selectedRegionId = 22;
    component.addPendingRegion();
    expect(component.pendingRegions.map(region => region.regionId)).toEqual([22]);

    component.saveEdit();
    const update = http.expectOne('/api/admin/territory/users/1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body).toEqual({
      territoryId: 10,
      firstName: 'Ana',
      lastName: 'Manager',
      regionIdsToAdd: [22],
    });
    update.flush({ ...user, assignedRegions: [...user.assignedRegions, { regionId: 22, regionName: 'North', displayName: 'North' }] });

    component.openRemoveManagerConfirm(user);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('The user account will not be deleted.');
  });

  it('renders add buttons for territory managers', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([buildUser(1, 'Ana Manager')], 1);
    loginAsAdmin(['TerritoryManager']);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Add Regional Manager');
    expect(fixture.nativeElement.textContent).toContain('Add Territory Associate');
  });

  it('hides add territory associate for territory associates', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([buildUser(1, 'Ana Manager')], 1);
    loginAsAdmin(['TerritoryAssociate']);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Add Regional Manager');
    expect(fixture.nativeElement.textContent).not.toContain('Add Territory Associate');
  });

  it('validates and saves the add regional manager modal through backend endpoints', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddRegionalManagerModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({ items: [buildAssignableUser(50, 'Mila User')] });

    component.saveAddRegionalManager();
    http.expectNone(req => req.url.includes('/regional-manager'));
    expect(component.addRegionalManagerSubmitted).toBe(true);

    component.selectRegionalManagerUser(buildAssignableUser(50, 'Mila User'));
    const regions = http.expectOne(req =>
      req.url === '/api/admin/territory/users/50/available-regions' &&
      req.params.get('territoryId') === '10');
    regions.flush({ items: [{ regionId: 22, regionName: 'North', displayName: 'North' }] });

    component.saveAddRegionalManager();
    http.expectNone(req => req.url.includes('/regional-manager'));

    component.addRegionalManagerRegion({ regionId: 22, regionName: 'North', displayName: 'North' });
    component.saveAddRegionalManager();

    const save = http.expectOne('/api/admin/territory/users/50/regional-manager');
    expect(save.request.method).toBe('POST');
    expect(save.request.body).toEqual({ territoryId: 10, regionIds: [22] });
    save.flush({
      ...buildUser(50, 'Mila User'),
      assignedRegions: [{ regionId: 22, regionName: 'North', displayName: 'North' }],
    });

    expect(component.addRegionalManagerOpen).toBe(false);
    expect(component.items.some(user => user.userId === 50)).toBe(true);
  });

  it('marks only the selected regional manager user row as selected', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddRegionalManagerModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({
      items: [
        buildAssignableUser(50, 'Mila User'),
        buildAssignableUser(51, 'Luka User'),
      ],
    });
    fixture.detectChanges();

    let options = regionalManagerUserOptions();
    expect(options.length).toBe(2);
    expect(options[0].getAttribute('aria-selected')).toBe('false');

    options[0].click();
    expectAvailableRegionsRequest(50)
      .flush({ items: [{ regionId: 22, regionName: 'North', displayName: 'North' }] });
    fixture.detectChanges();

    options = regionalManagerUserOptions();
    expect(options[0].getAttribute('aria-selected')).toBe('true');
    expect(options[0].className).toContain('border-l-blue-600');
    expect(options[0].textContent).toContain('Mila User');
    expect(options[1].getAttribute('aria-selected')).toBe('false');

    options[1].click();
    expectAvailableRegionsRequest(51)
      .flush({ items: [{ regionId: 23, regionName: 'South', displayName: 'South' }] });
    fixture.detectChanges();

    options = regionalManagerUserOptions();
    expect(options[0].getAttribute('aria-selected')).toBe('false');
    expect(options[1].getAttribute('aria-selected')).toBe('true');
  });

  it('keeps the add regional manager submit button disabled until user and region are selected', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddRegionalManagerModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({ items: [buildAssignableUser(50, 'Mila User')] });
    fixture.detectChanges();

    expect(addRegionalManagerSubmitButton().disabled).toBe(true);

    regionalManagerUserOptions()[0].click();
    expectAvailableRegionsRequest(50)
      .flush({ items: [{ regionId: 22, regionName: 'North', displayName: 'North' }] });
    fixture.detectChanges();

    expect(addRegionalManagerSubmitButton().disabled).toBe(true);

    component.addRegionalManagerRegion({ regionId: 22, regionName: 'North', displayName: 'North' });
    fixture.detectChanges();

    expect(addRegionalManagerSubmitButton().disabled).toBe(false);
  });

  it('clears regional manager selection when filtering removes the selected user', async () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddRegionalManagerModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({
      items: [
        buildAssignableUser(50, 'Mila User'),
        buildAssignableUser(51, 'Luka User'),
      ],
    });
    fixture.detectChanges();

    regionalManagerUserOptions()[0].click();
    expectAvailableRegionsRequest(50)
      .flush({ items: [{ regionId: 22, regionName: 'North', displayName: 'North' }] });
    component.addRegionalManagerRegion({ regionId: 22, regionName: 'North', displayName: 'North' });
    fixture.detectChanges();

    expect(component.selectedRegionalManagerUser?.userId).toBe(50);
    expect(addRegionalManagerSubmitButton().disabled).toBe(false);

    component.regionalManagerUserSearch = 'luka';
    component.onRegionalManagerUserSearchChange();
    await new Promise(resolve => setTimeout(resolve, 260));

    const filteredSearch = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10' &&
      req.params.get('query') === 'luka');
    filteredSearch.flush({ items: [buildAssignableUser(51, 'Luka User')] });
    fixture.detectChanges();

    expect(component.selectedRegionalManagerUser).toBeNull();
    expect(component.selectedRegionalManagerRegions).toEqual([]);
    expect(regionalManagerUserOptions()[0].getAttribute('aria-selected')).toBe('false');
    expect(addRegionalManagerSubmitButton().disabled).toBe(true);
  });

  it('validates and saves the add territory associate modal through backend endpoints', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddTerritoryAssociateModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({ items: [buildAssignableUser(51, 'Luka Associate')] });

    component.saveAddTerritoryAssociate();
    http.expectNone(req => req.url.includes('/territory-associate'));
    expect(component.addAssociateSubmitted).toBe(true);

    component.selectAssociateUser(buildAssignableUser(51, 'Luka Associate'));
    component.saveAddTerritoryAssociate();

    const save = http.expectOne('/api/admin/territory/users/51/territory-associate');
    expect(save.request.method).toBe('POST');
    expect(save.request.body).toEqual({ territoryId: 10 });
    save.flush({
      ...buildUser(51, 'Luka Associate'),
      roleCode: 'TerritoryAssociate',
      roleDisplayName: 'Territory Associate',
      roles: ['TerritoryAssociate'],
      assignedRegions: [],
      assignedTerritories: [{ territoryId: 10, territoryName: 'Test Territory', displayName: 'Test Territory' }],
      canRemoveTerritoryAssociateRole: true,
    });

    expect(component.addTerritoryAssociateOpen).toBe(false);
    expect(component.items.some(user => user.userId === 51)).toBe(true);
  });

  it('marks only the selected territory associate user row as selected', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddTerritoryAssociateModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({
      items: [
        buildAssignableUser(50, 'Mila User'),
        buildAssignableUser(51, 'Luka User'),
      ],
    });
    fixture.detectChanges();

    let options = associateUserOptions();
    expect(options.length).toBe(2);
    expect(options[0].getAttribute('aria-selected')).toBe('false');

    options[0].click();
    fixture.detectChanges();

    options = associateUserOptions();
    expect(options[0].getAttribute('aria-selected')).toBe('true');
    expect(options[0].className).toContain('border-l-blue-600');
    expect(options[0].textContent).toContain('Mila User');
    expect(options[0].querySelector('svg')).toBeTruthy();
    expect(options[1].getAttribute('aria-selected')).toBe('false');

    options[1].click();
    fixture.detectChanges();

    options = associateUserOptions();
    expect(options[0].getAttribute('aria-selected')).toBe('false');
    expect(options[1].getAttribute('aria-selected')).toBe('true');
  });

  it('keeps the add territory associate submit button disabled until a user is selected', () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddTerritoryAssociateModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({ items: [buildAssignableUser(51, 'Luka Associate')] });
    fixture.detectChanges();

    expect(addTerritoryAssociateSubmitButton().disabled).toBe(true);

    associateUserOptions()[0].click();
    fixture.detectChanges();

    expect(addTerritoryAssociateSubmitButton().disabled).toBe(false);
  });

  it('clears territory associate selection when filtering removes the selected user', async () => {
    fixture.detectChanges();
    flushTerritories();
    flushUsers([], 0);
    loginAsAdmin(['TerritoryManager']);

    component.openAddTerritoryAssociateModal();
    const search = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10');
    search.flush({
      items: [
        buildAssignableUser(50, 'Mila User'),
        buildAssignableUser(51, 'Luka User'),
      ],
    });
    fixture.detectChanges();

    associateUserOptions()[0].click();
    fixture.detectChanges();

    expect(component.selectedAssociateUser?.userId).toBe(50);
    expect(addTerritoryAssociateSubmitButton().disabled).toBe(false);

    component.associateUserSearch = 'luka';
    component.onAssociateUserSearchChange();
    await new Promise(resolve => setTimeout(resolve, 260));

    const filteredSearch = http.expectOne(req =>
      req.url === '/api/admin/territory/users/search' &&
      req.params.get('territoryId') === '10' &&
      req.params.get('query') === 'luka');
    filteredSearch.flush({ items: [buildAssignableUser(51, 'Luka User')] });
    fixture.detectChanges();

    expect(component.selectedAssociateUser).toBeNull();
    expect(associateUserOptions()[0].getAttribute('aria-selected')).toBe('false');
    expect(addTerritoryAssociateSubmitButton().disabled).toBe(true);
  });

  it('renders row add-role actions from backend flags', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(70, 'Nina Associate', undefined, {
      roleCode: 'TerritoryAssociate',
      roleDisplayName: 'Territory Associate',
      roles: ['TerritoryAssociate'],
      assignedRegions: [],
      assignedTerritories: [{ territoryId: 10, territoryName: 'Test Territory', displayName: 'Test Territory' }],
      canAddRegionalManagerRole: true,
      canAddRegion: false,
      canRemoveManagerRole: false,
      canRemoveRegionalManagerRole: false,
      canAddTerritoryAssociate: false,
      canAddTerritoryAssociateRole: false,
      canRemoveTerritoryAssociateRole: true,
    });
    flushUsers([user], 1);

    component.openDropdownUserId = user.userId;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Add Regional Manager Role');
    expect(fixture.nativeElement.textContent).not.toContain('Add Territory Associate Role');
    expect(fixture.nativeElement.textContent).toContain('Remove Territory Associate Role');
  });

  it('renders Add Territory Associate Role from row action flags', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(71, 'Rade Manager', undefined, {
      canAddTerritoryAssociate: true,
      canAddTerritoryAssociateRole: true,
    });
    flushUsers([user], 1);

    component.openDropdownUserId = user.userId;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Add Territory Associate Role');
  });

  it('hides remove role actions when backend flags disallow them', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(72, 'Tara Associate View', undefined, {
      canRemoveManagerRole: false,
      canRemoveRegionalManagerRole: false,
      canRemoveTerritoryAssociateRole: false,
      canAddTerritoryAssociate: false,
      canAddTerritoryAssociateRole: false,
    });
    flushUsers([user], 1);

    component.openDropdownUserId = user.userId;
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Remove Regional Manager Role');
    expect(fixture.nativeElement.textContent).not.toContain('Remove Territory Associate Role');
    expect(fixture.nativeElement.textContent).not.toContain('Add Territory Associate Role');
  });

  it('opens Add Regional Manager Role from a row with the user preselected and updates the row', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(73, 'Nina Regional Candidate', undefined, {
      roleCode: 'TerritoryAssociate',
      roleDisplayName: 'Territory Associate',
      roles: ['TerritoryAssociate'],
      assignedRegions: [],
      assignedTerritories: [{ territoryId: 10, territoryName: 'Test Territory', displayName: 'Test Territory' }],
      canAddRegionalManagerRole: true,
      canAddRegion: false,
      canRemoveManagerRole: false,
      canRemoveRegionalManagerRole: false,
      canAddTerritoryAssociate: false,
      canAddTerritoryAssociateRole: false,
      canRemoveTerritoryAssociateRole: true,
    });
    flushUsers([user], 1);

    component.openAddRegionalManagerModal(user);
    const regions = expectAvailableRegionsRequest(user.userId);
    regions.flush({ items: [{ regionId: 22, regionName: 'North', displayName: 'North' }] });
    fixture.detectChanges();

    expect(component.selectedRegionalManagerUser?.userId).toBe(user.userId);
    expect(component.regionalManagerPreselectedUser?.userId).toBe(user.userId);
    component.regionalManagerUserSearch = 'someone else';
    component.onRegionalManagerUserSearchChange();
    http.expectNone('/api/admin/territory/users/search');

    component.addRegionalManagerRegion({ regionId: 22, regionName: 'North', displayName: 'North' });
    component.saveAddRegionalManager();

    const save = http.expectOne(`/api/admin/territory/users/${user.userId}/regional-manager`);
    expect(save.request.method).toBe('POST');
    expect(save.request.body).toEqual({ territoryId: 10, regionIds: [22] });
    save.flush({
      ...user,
      roleCode: 'RegionalManager',
      roleDisplayName: 'Regional Manager',
      roles: ['TerritoryAssociate', 'RegionalManager'],
      assignedRegions: [{ regionId: 22, regionName: 'North', displayName: 'North' }],
      canAddRegionalManagerRole: false,
      canAddRegion: true,
      canRemoveManagerRole: true,
      canRemoveRegionalManagerRole: true,
    });

    expect(component.addRegionalManagerOpen).toBe(false);
    expect(component.items.find(item => item.userId === user.userId)?.assignedRegions.length).toBe(1);
  });

  it('opens Add Territory Associate Role from a row with the user preselected', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(74, 'Rade Associate Candidate', undefined, {
      canAddTerritoryAssociate: true,
      canAddTerritoryAssociateRole: true,
      canRemoveTerritoryAssociateRole: false,
    });
    flushUsers([user], 1);

    component.openAddTerritoryAssociateModal(user);
    fixture.detectChanges();

    expect(component.selectedAssociateUser?.userId).toBe(user.userId);
    expect(component.associatePreselectedUser?.userId).toBe(user.userId);
    component.associateUserSearch = 'someone else';
    component.onAssociateUserSearchChange();
    http.expectNone('/api/admin/territory/users/search');

    component.saveAddTerritoryAssociate();
    const save = http.expectOne(`/api/admin/territory/users/${user.userId}/territory-associate`);
    expect(save.request.method).toBe('POST');
    expect(save.request.body).toEqual({ territoryId: 10 });
    save.flush({
      ...user,
      roleCode: 'TerritoryAssociate',
      roleDisplayName: 'Territory Associate',
      roles: ['RegionalManager', 'TerritoryAssociate'],
      assignedTerritories: [{ territoryId: 10, territoryName: 'Test Territory', displayName: 'Test Territory' }],
      canAddTerritoryAssociate: false,
      canAddTerritoryAssociateRole: false,
      canRemoveTerritoryAssociateRole: true,
    });

    expect(component.addTerritoryAssociateOpen).toBe(false);
    expect(component.items.find(item => item.userId === user.userId)?.roles).toContain('TerritoryAssociate');
  });

  it('renders one edit action and no editable display name field', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(1, 'Ana Manager');
    flushUsers([user], 1);

    component.openDropdownUserId = user.userId;
    fixture.detectChanges();

    const actionLabels = Array.from(fixture.nativeElement.querySelectorAll('button'))
      .map(button => (button as HTMLButtonElement).textContent?.trim() ?? '')
      .filter(label => label.includes('Edit User'));
    expect(actionLabels.length).toBe(1);

    component.openEditModal(user);
    http.expectOne(req =>
      req.url === '/api/admin/territory/users/1/available-regions' &&
      req.params.get('territoryId') === '10').flush({ items: [] });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Display Name');
    expect(fixture.nativeElement.textContent).toContain('First Name');
    expect(fixture.nativeElement.textContent).toContain('Last Name');
    expect(fixture.nativeElement.textContent).toContain(user.email);
  });

  it('excludes assigned and pending regions from the edit selector', () => {
    fixture.detectChanges();
    flushTerritories();
    const user = buildUser(1, 'Ana Manager');
    flushUsers([user], 1);

    component.openEditModal(user);
    http.expectOne(req =>
      req.url === '/api/admin/territory/users/1/available-regions' &&
      req.params.get('territoryId') === '10').flush({
        items: [
          { regionId: 11, regionName: 'Region One', displayName: 'Region One' },
          { regionId: 22, regionName: 'North', displayName: 'North' },
          { regionId: 23, regionName: 'South', displayName: 'South' },
        ],
      });

    expect(component.selectableRegions.map(region => region.regionId)).toEqual([22, 23]);

    component.selectedRegionId = 22;
    component.addPendingRegion();
    expect(component.selectableRegions.map(region => region.regionId)).toEqual([23]);
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

  function buildUser(
    userId: number,
    displayName: string,
    avatarUrl?: string,
    overrides: Partial<TerritoryUser> = {},
  ): TerritoryUser {
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
      roles: ['RegionalManager'],
      assignedRegions: [{ regionId: 11, regionName: 'Region One', displayName: 'Region One' }],
      assignedTerritories: [],
      status: 'Active',
      isActive: true,
      isInTerritoryScope: true,
      lastLoginAt: '2026-05-18T10:00:00Z',
      lastLoginDisplay: '1 day ago',
      canEdit: true,
      canEditUser: true,
      canAddRegionalManagerRole: false,
      canAddRegion: true,
      canRemoveManagerRole: true,
      canRemoveRegionalManagerRole: true,
      canAddTerritoryAssociate: true,
      canAddTerritoryAssociateRole: true,
      canRemoveTerritoryAssociateRole: false,
      createdAt: '2026-01-01T00:00:00Z',
      ...overrides,
    };
  }

  function buildAssignableUser(userId: number, displayName: string): TerritoryAssignableUser {
    const [firstName, lastName = ''] = displayName.split(' ');
    return {
      userId,
      displayName,
      firstName,
      lastName,
      email: `${displayName.toLowerCase().replace(/\s+/g, '.')}@example.com`,
      initials: displayName.split(/\s+/).map(part => part[0]).join('').slice(0, 2).toUpperCase(),
      roles: ['RegisteredUser'],
      assignedRegions: [],
      assignedTerritories: [],
      isActive: true,
    };
  }

  function loginAsAdmin(roles: string[]): void {
    const auth = TestBed.inject(AuthService);
    auth.login('mika@gmail.com', 'Proba123!').subscribe();
    const request = http.expectOne('/api/admin/auth/login');
    expect(request.request.method).toBe('POST');
    request.flush({
      accessToken: 'admin-token',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      expiresIn: 900,
      sessionPersistent: false,
      user: {
        id: 7,
        email: 'mika@gmail.com',
        displayName: 'Mika Manager',
        roles,
        canAccessRegionalAdmin: false,
        canAccessTerritoryAdmin: roles.includes('TerritoryManager') || roles.includes('TerritoryAssociate'),
      },
    });
  }

  function regionalManagerUserOptions(): HTMLButtonElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll(
        '[aria-label="Assignable users for Regional Manager"] [role="option"]',
      ),
    ) as HTMLButtonElement[];
  }

  function associateUserOptions(): HTMLButtonElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll(
        '[aria-label="Assignable users for Territory Associate"] [role="option"]',
      ),
    ) as HTMLButtonElement[];
  }

  function addRegionalManagerSubmitButton(): HTMLButtonElement {
    const submitButtons = Array.from(
      fixture.nativeElement.querySelectorAll('form button[type="submit"]'),
    ) as HTMLButtonElement[];
    const button = submitButtons.find(item => item.textContent?.includes('Add Regional Manager'));
    expect(button).toBeTruthy();
    return button as HTMLButtonElement;
  }

  function addTerritoryAssociateSubmitButton(): HTMLButtonElement {
    const submitButtons = Array.from(
      fixture.nativeElement.querySelectorAll('form button[type="submit"]'),
    ) as HTMLButtonElement[];
    const button = submitButtons.find(item => item.textContent?.includes('Add Territory Associate'));
    expect(button).toBeTruthy();
    return button as HTMLButtonElement;
  }

  function expectAvailableRegionsRequest(userId: number) {
    return http.expectOne(req =>
      req.url === `/api/admin/territory/users/${userId}/available-regions` &&
      req.params.get('territoryId') === '10');
  }
});

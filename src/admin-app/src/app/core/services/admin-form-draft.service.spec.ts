import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';

import { AdminFormDraftService } from './admin-form-draft.service';
import { AuthService } from './auth.service';

describe('AdminFormDraftService', () => {
  const router = { url: '/destinations/new?regionId=7' };
  const auth = {
    getUser: () => ({ id: '42' }),
  };

  let service: AdminFormDraftService;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [
        AdminFormDraftService,
        { provide: Router, useValue: router },
        { provide: AuthService, useValue: auth },
      ],
    });

    service = TestBed.inject(AdminFormDraftService);
  });

  afterEach(() => {
    sessionStorage.clear();
  });

  it('saves and loads the registered active form draft for the current route and user', () => {
    service.register({
      formType: 'destination-new',
      snapshot: () => ({ name: 'Saved destination', tagIds: [1, 2] }),
    });

    expect(service.saveActiveDraftOnSessionExpiry()).toBe(true);

    const draft = service.loadDraft<{ name: string; tagIds: number[] }>('destination-new');
    expect(draft?.route).toBe('/destinations/new');
    expect(draft?.userId).toBe('42');
    expect(draft?.payload.name).toBe('Saved destination');
    expect(draft?.payload.tagIds).toEqual([1, 2]);
  });

  it('does not save when the registered source has no meaningful snapshot', () => {
    service.register({
      formType: 'destination-new',
      snapshot: () => null,
    });

    expect(service.saveActiveDraftOnSessionExpiry()).toBe(false);
    expect(service.loadDraft('destination-new')).toBeNull();
  });

  it('clears saved drafts by form type', () => {
    service.register({
      formType: 'destination-new',
      snapshot: () => ({ name: 'Saved destination' }),
    });
    service.saveActiveDraftOnSessionExpiry();

    service.clearDraft('destination-new');

    expect(service.loadDraft('destination-new')).toBeNull();
  });
});

import { TestBed } from '@angular/core/testing';

import { USER_APP_BASE_URL, UserAppLinkService } from './user-app-link.service';

describe('UserAppLinkService', () => {
  it('combines configured user-app origins with public routes', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: USER_APP_BASE_URL, useValue: 'https://user.example/' }],
    });

    const service = TestBed.inject(UserAppLinkService);

    expect(service.toUserAppUrl('/destinations/7')).toBe('https://user.example/destinations/7');
    expect(service.toUserAppUrl('/locations/8')).toBe('https://user.example/locations/8');
    expect(service.toUserAppUrl('/places/42')).toBe('https://user.example/places/42');
    expect(service.toUserAppUrl('/stays/43')).toBe('https://user.example/stays/43');
    expect(service.toUserAppUrl('/events/9')).toBe('https://user.example/events/9');
    expect(service.toUserAppUrl('/explore?destinationId=7')).toBe('https://user.example/explore?destinationId=7');
  });

  it('keeps root-relative routes for same-origin production deployments', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: USER_APP_BASE_URL, useValue: '' }],
    });

    const service = TestBed.inject(UserAppLinkService);

    expect(service.toUserAppUrl('/events/9')).toBe('/events/9');
  });

  it('rejects missing, admin, api, and non-relative routes', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: USER_APP_BASE_URL, useValue: 'https://user.example' }],
    });

    const service = TestBed.inject(UserAppLinkService);

    expect(service.toUserAppUrl(null)).toBeNull();
    expect(service.toUserAppUrl('https://user.example/places/1')).toBeNull();
    expect(service.toUserAppUrl('/admin/places/detail/1')).toBeNull();
    expect(service.toUserAppUrl('/api/places/1')).toBeNull();
  });
});

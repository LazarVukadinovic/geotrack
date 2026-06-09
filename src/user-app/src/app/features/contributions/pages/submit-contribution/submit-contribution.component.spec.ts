import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { vi } from 'vitest';

import { SubmitContributionComponent } from './submit-contribution.component';

describe('SubmitContributionComponent', () => {
  let component: SubmitContributionComponent;
  let fixture: ComponentFixture<SubmitContributionComponent>;
  let httpTesting: HttpTestingController;
  const routerMock = { navigate: vi.fn() };
  const activatedRouteMock = {
    snapshot: {
      paramMap: {
        get: vi.fn(() => null),
      },
    },
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SubmitContributionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(SubmitContributionComponent);
    component = fixture.componentInstance;
    httpTesting = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    flushInitialCatalogRequests();
    await fixture.whenStable();
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  function flushInitialCatalogRequests(): void {
    httpTesting.expectOne('/api/contributions/place-types').flush([]);
    httpTesting.expectOne('/api/contributions/stay-types').flush([]);
  }
});

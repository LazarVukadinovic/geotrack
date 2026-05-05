import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { vi } from 'vitest';

import { ContributionDetailPageComponent } from './contribution-detail.component';

describe('ContributionDetailPageComponent', () => {
  let component: ContributionDetailPageComponent;
  let fixture: ComponentFixture<ContributionDetailPageComponent>;
  const routerMock = {
    navigate: vi.fn(),
  };
  const activatedRouteMock = {
    snapshot: {
      paramMap: {
        get: vi.fn(() => null),
      },
    },
  };

  beforeEach(async () => {
    routerMock.navigate.mockClear();

    await TestBed.configureTestingModule({
      imports: [ContributionDetailPageComponent],
      providers: [
        provideHttpClient(),
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ContributionDetailPageComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

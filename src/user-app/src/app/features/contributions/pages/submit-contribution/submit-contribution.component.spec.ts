import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { vi } from 'vitest';

import { SubmitContributionComponent } from './submit-contribution.component';

describe('SubmitContributionComponent', () => {
  let component: SubmitContributionComponent;
  let fixture: ComponentFixture<SubmitContributionComponent>;
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
        { provide: Router, useValue: routerMock },
        { provide: ActivatedRoute, useValue: activatedRouteMock },
      ],
    })
    .compileComponents();

    fixture = TestBed.createComponent(SubmitContributionComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

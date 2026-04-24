import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { vi } from 'vitest';

import { ContributionDetailPageComponent } from './contribution-detail.component';

describe('ContributionDetailPageComponent', () => {
  let component: ContributionDetailPageComponent;
  let fixture: ComponentFixture<ContributionDetailPageComponent>;
  const routerMock = {
    getCurrentNavigation: vi.fn(() => ({
      extras: {
        state: {
          item: {
            id: '1',
            name: 'Sunny Seaside Villa',
            location: 'Malibu, California',
            imageUrl: '',
            status: 'APPROVED',
            updatedLabel: 'Updated 2 days ago',
            entityType: 'Stay',
          },
        },
      },
    })),
    navigate: vi.fn(),
  };

  beforeEach(async () => {
    routerMock.navigate.mockClear();

    await TestBed.configureTestingModule({
      imports: [ContributionDetailPageComponent],
      providers: [
        provideHttpClient(),
        { provide: Router, useValue: routerMock },
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

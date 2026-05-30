import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { MyCollectionsPageComponent } from './my-collections-page.component';
import { CollectionsService } from '../../services/collections';

describe('MyCollectionsPage', () => {
  let component: MyCollectionsPageComponent;
  let fixture: ComponentFixture<MyCollectionsPageComponent>;

  const router = {
    navigate: vi.fn(),
  };

  const location = {
    back: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [MyCollectionsPageComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: Location, useValue: location },
        {
          provide: CollectionsService,
          useValue: {
            collections: signal([]),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyCollectionsPageComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should navigate back to the profile page', () => {
    fixture.detectChanges();

    const backButton = fixture.nativeElement.querySelector('button.collections-page__back') as HTMLButtonElement;
    backButton.click();

    expect(router.navigate).toHaveBeenCalledWith(['/profile']);
  });
});

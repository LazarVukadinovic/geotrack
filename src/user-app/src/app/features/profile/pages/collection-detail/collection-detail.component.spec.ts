import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { vi } from 'vitest';
import { CollectionDetailComponent } from './collection-detail.component';
import { CollectionsService } from '../../services/collections';

describe('CollectionDetail', () => {
  let component: CollectionDetailComponent;
  let fixture: ComponentFixture<CollectionDetailComponent>;

  const router = {
    navigate: vi.fn(),
  };

  const location = {
    back: vi.fn(),
  };

  const collectionsService = {
    collections: signal([
      {
        id: 1,
        title: 'Weekend Trip',
        itemsCount: 0,
        coverImages: [],
        places: [],
      },
    ]),
    loadDetails: vi.fn(),
    removePlace: vi.fn(),
    renameCollection: vi.fn(),
    deleteCollection: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [CollectionDetailComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: Location, useValue: location },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: () => '1',
              },
            },
          },
        },
        { provide: CollectionsService, useValue: collectionsService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CollectionDetailComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

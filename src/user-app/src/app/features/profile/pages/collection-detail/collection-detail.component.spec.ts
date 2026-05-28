import { Location } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { vi } from 'vitest';
import { CollectionDetailComponent } from './collection-detail.component';
import { Collection, CollectionsService } from '../../services/collections';

describe('CollectionDetail', () => {
  let component: CollectionDetailComponent;
  let fixture: ComponentFixture<CollectionDetailComponent>;

  const location = {
    back: vi.fn(),
  };

  const router = {
    navigate: vi.fn(),
    navigateByUrl: vi.fn(),
  };

  const collectionsService = {
    collections: signal<Collection[]>([
      {
        id: 1,
        title: 'Weekend Trip',
        itemsCount: 0,
        coverImages: [],
        places: [],
      },
    ]),
    loadDetails: vi.fn(),
    getDetailLoadState: vi.fn(() => 'loaded'),
    removePlace: vi.fn(),
    renameCollection: vi.fn(),
    deleteCollection: vi.fn(),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [CollectionDetailComponent],
      providers: [
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
        { provide: Router, useValue: router },
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

  it('should navigate collection item cards to their detail route', () => {
    collectionsService.collections.set([
      {
        id: 1,
        title: 'Weekend Trip',
        itemsCount: 1,
        coverImages: [],
        places: [
          {
            itemId: 7,
            entityType: 'place',
            entityId: 20,
            placeId: 20,
            title: 'Villa Adriatica',
            image: '',
            detailRoute: '/stays/20',
          },
        ],
      },
    ]);
    fixture.detectChanges();

    const openButton = fixture.nativeElement.querySelector('button[aria-label="Open Villa Adriatica"]') as HTMLButtonElement;
    openButton.click();

    expect(router.navigateByUrl).toHaveBeenCalledWith('/stays/20');
  });

  it('should navigate to explore from the add button', () => {
    fixture.detectChanges();

    const addButton = fixture.nativeElement.querySelector(
      'button[aria-label="Explore and add saved items"]',
    ) as HTMLButtonElement;
    addButton.click();

    expect(router.navigate).toHaveBeenCalledWith(['/profile/collections', 1, 'add']);
  });

  it('should navigate back to the collections list', () => {
    fixture.detectChanges();

    const backButton = fixture.nativeElement.querySelector('button.collection-detail-page__back') as HTMLButtonElement;
    backButton.click();

    expect(router.navigate).toHaveBeenCalledWith(['/profile/collections']);
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { NewCollectionSheetComponent } from './new-collection-sheet.component';
import { CollectionsService } from '../../services/collections';

describe('NewCollectionSheet', () => {
  let component: NewCollectionSheetComponent;
  let fixture: ComponentFixture<NewCollectionSheetComponent>;

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [NewCollectionSheetComponent],
      providers: [
        {
          provide: CollectionsService,
          useValue: {
            addCollection: vi.fn(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(NewCollectionSheetComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

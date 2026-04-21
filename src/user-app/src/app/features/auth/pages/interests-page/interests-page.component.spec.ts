import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { InterestsPageComponent } from './interests-page.component';
import { TagService } from '../../../../core/services/tag.service';
import { UserService } from '../../../../core/services/user.service';

describe('InterestsPageComponent', () => {
  let component: InterestsPageComponent;
  let fixture: ComponentFixture<InterestsPageComponent>;

  const router = {
    navigate: vi.fn(),
  };

  const tagService = {
    loadTags: vi.fn(() => of([{ id: 1, name: 'Food & Drink', slug: 'food-drink' }])),
    loadUserInterests: vi.fn(() => of({ tags: [] })),
    saveUserTags: vi.fn(() => of(void 0)),
  };

  const userService = {
    load: vi.fn(() => of({})),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [InterestsPageComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: TagService, useValue: tagService },
        { provide: UserService, useValue: userService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InterestsPageComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load available tags on init', () => {
    expect(tagService.loadTags).toHaveBeenCalledTimes(1);
    expect(tagService.loadUserInterests).toHaveBeenCalledTimes(1);
    expect(component.tags()).toEqual([{ id: 1, name: 'Food & Drink', slug: 'food-drink' }]);
    expect(component.isLoadingTags()).toBe(false);
  });

  it('should clear onboarding state when skipping', () => {
    component.onSkip();

    expect(tagService.saveUserTags).toHaveBeenCalledWith([]);
    expect(userService.load).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/']);
  });
});

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { MyInterestsComponent } from './my-interests.component';
import { UserService } from '../../../../core/services/user.service';
import { TagService } from '../../../../core/services/tag.service';

describe('MyInterests', () => {
  let component: MyInterestsComponent;
  let fixture: ComponentFixture<MyInterestsComponent>;

  const tags = [
    { id: 1, name: 'Food & Drink', slug: 'food-drink' },
    { id: 2, name: 'Nature', slug: 'nature' },
    { id: 3, name: 'History', slug: 'history' },
  ];

  const router = {
    navigate: vi.fn(),
  };

  const userService = {
    load: vi.fn(() =>
      of({
        id: 1,
        username: 'marko',
        email: 'marko.p@example.com',
        firstName: 'Marko',
        lastName: 'Petrovic',
      }),
    ),
  };

  const tagService = {
    loadTags: vi.fn(() => of(tags)),
    loadUserInterests: vi.fn(() =>
      of({
        explicitTags: [{ tagId: 1, name: 'Food & Drink', slug: 'food-drink' }],
        inferredTags: [{ tagId: 2, name: 'Nature', slug: 'nature', score: 5, lastInteractionAt: new Date().toISOString() }],
        tags: [{ tagId: 1, name: 'Food & Drink', slug: 'food-drink' }],
      }),
    ),
    saveUserTags: vi.fn(() => of(void 0)),
    dismissInferredInterest: vi.fn(() => of(void 0)),
  };

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [MyInterestsComponent],
      providers: [
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { queryParams: of({}) } },
        { provide: UserService, useValue: userService },
        { provide: TagService, useValue: tagService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyInterestsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load selected interests from the current-user API', () => {
    expect(tagService.loadTags).toHaveBeenCalledTimes(1);
    expect(tagService.loadUserInterests).toHaveBeenCalledTimes(1);
    expect(component.selectedTags().map((tag) => tag.id)).toEqual([1]);
    expect(component.visibleInferredTags().map((tag) => tag.tagId)).toEqual([2]);
    expect(component.availableTags().map((tag) => tag.id)).toEqual([3]);
  });

  it('should promote only the strongest activity suggestions and keep the rest discoverable', () => {
    const catalogue = Array.from({ length: 8 }, (_, index) => ({
      id: index + 1,
      name: `Interest ${index + 1}`,
      slug: `interest-${index + 1}`,
      group: index < 4 ? 'Nature & Outdoors' : 'Culture & Heritage',
      sortOrder: index + 1,
    }));

    component.tags.set(catalogue);
    component.selectedTags.set([]);
    component.inferredTags.set(
      catalogue.slice(0, 6).map((tag, index) => ({
        tagId: tag.id,
        name: tag.name,
        slug: tag.slug,
        score: 20 - index,
        lastInteractionAt: new Date().toISOString(),
      })),
    );

    expect(component.visibleInferredTags().map(tag => tag.tagId)).toEqual([1, 2, 3, 4]);
    expect(component.availableTags().map(tag => tag.id)).toEqual([5, 6, 7, 8]);
    expect(component.availableTagGroups().map(group => group.group)).toEqual([
      'Culture & Heritage',
    ]);
  });

  it('should add and remove tags from the selection', () => {
    const tag = component.availableTags()[0];

    component.addTag(tag);
    expect(component.isSelected(tag)).toBe(true);

    component.removeTag(tag);
    expect(component.isSelected(tag)).toBe(false);
  });

  it('should let users dismiss inferred interests', () => {
    component.dismissInferred(component.visibleInferredTags()[0]);

    expect(tagService.dismissInferredInterest).toHaveBeenCalledWith(2);
    expect(component.visibleInferredTags()).toEqual([]);
  });

  it('should persist selected tags through the current API contract', () => {
    userService.load.mockClear();
    router.navigate.mockClear();
    tagService.saveUserTags.mockClear();

    component.selectedTags.set([tags[0], tags[2]]);
    component.saveChanges();

    expect(tagService.saveUserTags).toHaveBeenCalledWith([1, 3]);
    expect(userService.load).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/settings']);
  });

  it('should surface save errors instead of silently navigating away', () => {
    userService.load.mockClear();
    router.navigate.mockClear();
    tagService.saveUserTags.mockReturnValueOnce(
      throwError(() => new Error('Save failed')),
    );

    component.selectedTags.set([tags[1]]);
    component.saveChanges();

    expect(component.saveError()).toBe(
      'Could not save your interests. Please try again.',
    );
    expect(userService.load).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });
});

import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { MyInterestsComponent } from './my-interests.component';
import { UserService } from '../../../../core/services/user.service';

describe('MyInterests', () => {
  let component: MyInterestsComponent;
  let fixture: ComponentFixture<MyInterestsComponent>;
  let httpMock: HttpTestingController;

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

  const tags = [
    { id: 1, name: 'Food & Drink', slug: 'food-drink' },
    { id: 2, name: 'Nature', slug: 'nature' },
    { id: 3, name: 'History', slug: 'history' },
  ];

  beforeEach(async () => {
    vi.clearAllMocks();

    await TestBed.configureTestingModule({
      imports: [MyInterestsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: router },
        { provide: ActivatedRoute, useValue: { queryParams: of({}) } },
        { provide: UserService, useValue: userService },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MyInterestsComponent);
    component = fixture.componentInstance;

    httpMock.expectOne('/api/tags').flush(tags);
    httpMock.expectOne('/api/user/me/interests').flush({
      tags: [{ tagId: 1, name: 'Food & Drink', slug: 'food-drink' }],
    });
    await fixture.whenStable();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load selected interests from the current-user API', () => {
    expect(component.selectedTags.map((tag) => tag.id)).toEqual([1]);
  });

  it('should add and remove tags from the selection', () => {
    const tag = component.availableTags[0];

    component.addTag(tag);
    expect(component.isSelected(tag)).toBe(true);

    component.removeTag(tag);
    expect(component.isSelected(tag)).toBe(false);
  });

  it('should persist selected tags through the current API contract', () => {
    userService.load.mockClear();
    router.navigate.mockClear();

    component.selectedTags = [tags[0], tags[2]];
    component.saveChanges();

    const request = httpMock.expectOne('/api/user/me/interests');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ tagIds: [1, 3] });

    request.flush({});

    expect(userService.load).toHaveBeenCalledTimes(1);
    expect(router.navigate).toHaveBeenCalledWith(['/settings']);
  });

  it('should surface save errors instead of silently navigating away', () => {
    userService.load.mockClear();
    router.navigate.mockClear();

    component.selectedTags = [tags[1]];
    component.saveChanges();

    const request = httpMock.expectOne('/api/user/me/interests');
    request.flush(
      { message: 'Save failed' },
      { status: 500, statusText: 'Server Error' },
    );

    expect(component.saveError).toBe(
      'Could not save your interests. Please try again.',
    );
    expect(userService.load).not.toHaveBeenCalled();
    expect(router.navigate).not.toHaveBeenCalled();
  });
});

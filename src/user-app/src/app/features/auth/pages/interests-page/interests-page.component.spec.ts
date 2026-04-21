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
    loadTags: vi.fn(() => of([])),
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
});

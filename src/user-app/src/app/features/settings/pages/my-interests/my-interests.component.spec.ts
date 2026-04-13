import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { MyInterestsComponent } from './my-interests.component';

describe('MyInterests', () => {
  let component: MyInterestsComponent;
  let fixture: ComponentFixture<MyInterestsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MyInterestsComponent, RouterTestingModule],
    }).compileComponents();

    fixture = TestBed.createComponent(MyInterestsComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => localStorage.removeItem('userInterests'));

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load saved interests from localStorage', () => {
    localStorage.setItem('userInterests', JSON.stringify(['food', 'beaches']));
    const f2 = TestBed.createComponent(MyInterestsComponent);
    const c2 = f2.componentInstance;
    expect(c2.selectedInterests.length).toBe(2);
    expect(c2.selectedInterests.map(i => i.id)).toContain('food');
  });

  it('should add interest to selected', () => {
    const interest = component.availableInterests[0];
    component.addInterest(interest);
    expect(component.isSelected(interest)).toBe(true);
  });

  it('should remove interest from selected', () => {
    const interest = component.availableInterests[0];
    component.addInterest(interest);
    component.removeInterest(interest);
    expect(component.isSelected(interest)).toBe(false);
  });

  it('should not add duplicate interest', () => {
    const interest = component.availableInterests[0];
    component.addInterest(interest);
    component.addInterest(interest);
    expect(component.selectedInterests.filter(i => i.id === interest.id).length).toBe(1);
  });
});
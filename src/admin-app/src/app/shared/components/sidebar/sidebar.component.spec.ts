import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { vi } from 'vitest';

import { SidebarComponent } from './sidebar.component';

describe('SidebarComponent', () => {
  let component: SidebarComponent;
  let fixture: ComponentFixture<SidebarComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(SidebarComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('shows an available active region label instead of loading forever', async () => {
    fixture.componentRef.setInput('region', 'Coastal Test Region, Testland');
    fixture.componentRef.setInput('regionLoading', true);
    await fixture.whenStable();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Coastal Test Region, Testland');
    expect(text).not.toContain('Loading...');
  });

  it('emits a numeric region id when the selector changes', async () => {
    const regionChange = vi.spyOn(component.regionChange, 'emit');
    fixture.componentRef.setInput('regions', [
      { id: 7, name: 'Backend Region', displayName: 'Backend Region' },
      { id: 11, name: 'Northern Region', displayName: 'Northern Region' },
    ]);
    fixture.componentRef.setInput('activeRegionId', 7);
    await fixture.whenStable();

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement | null;
    expect(select).not.toBeNull();

    select!.value = '11';
    select!.dispatchEvent(new Event('change'));

    expect(regionChange).toHaveBeenCalledWith(11);
  });
});

import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';

@Component({
  selector: 'app-not-found-page',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="min-h-full bg-[#fafafa] flex items-center justify-center px-6">
      <div class="max-w-sm w-full bg-white rounded-[2rem] shadow-sm border border-slate-200 px-6 py-8 text-center">
        <p class="text-xs font-bold tracking-[0.15em] text-slate-400 uppercase mb-3">Page not found</p>
        <h1 class="text-2xl font-bold text-slate-800 mb-2">This route is not available.</h1>
        <p class="text-sm text-slate-500 leading-relaxed mb-6">
          GeoTrack could not find the page you requested. Use a trusted route from the app navigation instead.
        </p>
        <div class="flex flex-col gap-3">
          <button
            type="button"
            (click)="goHome()"
            class="w-full py-3 bg-[#e8570a] rounded-2xl text-white text-sm font-bold">
            Go to Home
          </button>
          <button
            type="button"
            (click)="goBack()"
            class="w-full py-3 border border-slate-200 rounded-2xl text-slate-700 text-sm font-semibold">
            Go Back
          </button>
        </div>
      </div>
    </div>
  `,
})
export class NotFoundPageComponent {
  private readonly router = inject(Router);

  protected goHome(): void {
    void this.router.navigate(['/home']);
  }

  protected goBack(): void {
    globalThis.history.back();
  }
}

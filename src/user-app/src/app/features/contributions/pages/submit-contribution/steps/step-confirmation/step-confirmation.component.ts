import { Component, input, inject } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-step-confirmation',
  standalone: true,
  templateUrl: './step-confirmation.component.html',
})
export class StepConfirmationComponent {
  entityType = input('Stay');
  private router = inject(Router);

  goToContributions(): void { this.router.navigate(['/contributions']); }
  addAnother(): void { this.router.navigate(['/contributions/submit'], { state: { entityType: this.entityType() } }); }
}
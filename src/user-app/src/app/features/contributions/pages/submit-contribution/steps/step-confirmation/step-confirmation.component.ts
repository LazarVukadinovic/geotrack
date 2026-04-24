import { Component, input,output, inject } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-step-confirmation',
  standalone: true,
  templateUrl: './step-confirmation.component.html',
})
export class StepConfirmationComponent {
  entityType = input('Stay');
  back = output<void>();
  addAnother = output<void>();
  private router = inject(Router);

  goToContributions(): void { this.router.navigate(['/contributions']); }
}
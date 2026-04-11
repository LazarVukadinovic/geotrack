import { Component } from '@angular/core';
import { PlaceholderViewComponent } from '../../../../shared/components/placeholder-view/placeholder-view.component';

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [PlaceholderViewComponent],
  template: `<app-placeholder-view title="Login" />`,
})
export class LoginPageComponent {}

import { Component, signal } from '@angular/core';
import { PaymentDetails } from './payment-details/payment-details';

@Component({
  selector: 'app-root',
  imports: [PaymentDetails],
  templateUrl: './app.html',
  styles: [],
})
export class App {
  protected readonly title = signal('DemoProjekatFrontEnd');
}

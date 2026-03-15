import { Component } from '@angular/core';
import { ProductsComponent } from './components/products';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [ProductsComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  protected title = 'store-ui';
}

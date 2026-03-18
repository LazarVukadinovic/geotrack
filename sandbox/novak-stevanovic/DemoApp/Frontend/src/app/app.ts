import { Component } from '@angular/core';
import { Products } from './products/products';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [Products],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
}
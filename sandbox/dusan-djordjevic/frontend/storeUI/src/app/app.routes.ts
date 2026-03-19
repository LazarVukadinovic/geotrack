import { Routes } from '@angular/router';
import { ProductsComponent } from './components/products/products';
import { HomeComponent } from './components/home';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'products', component: ProductsComponent }
];
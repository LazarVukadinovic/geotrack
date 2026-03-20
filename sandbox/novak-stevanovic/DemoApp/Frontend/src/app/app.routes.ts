import { Routes } from '@angular/router';
import { ProductsList } from './products-list/products-list';
import { ProductForm } from './product-form/product-form';

export const routes: Routes = [
  { path: '', redirectTo: 'products', pathMatch: 'full' },
  { path: 'products', component: ProductsList },
  { path: 'products/new', component: ProductForm },
  { path: 'products/:id/edit', component: ProductForm }
];
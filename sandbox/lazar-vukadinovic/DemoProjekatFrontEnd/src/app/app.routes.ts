import { Routes } from '@angular/router';
import { PaymentDetails } from './payment-details/payment-details';
import { About } from './about/about';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'payments',
    pathMatch: 'full',
  },
  {
    path: 'payments',
    component: PaymentDetails,
  },
  {
    path: 'about',
    component: About,
  },
];

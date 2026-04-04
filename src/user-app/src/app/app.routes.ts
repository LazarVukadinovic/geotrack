import { Routes } from '@angular/router';
import { Login } from './features/auth/pages/login/login';
import { Register } from './features/auth/pages/register/register';
import { Interests } from './features/auth/pages/interests/interests';
import { Home } from './features/home/pages/home/home';
import { MapPage } from './features/map/pages/map-page/map-page';

export const routes: Routes = [
    { path: '', redirectTo: 'home', pathMatch: 'full' },
    { path: 'login', component: Login },
    { path: 'register', component: Register },
    { path: 'interests', component: Interests },
    { path: 'home', component: Home},
    { path: 'map', component: MapPage },
];

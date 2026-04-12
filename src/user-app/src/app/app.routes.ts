import { Routes } from '@angular/router';
import { Login } from './features/auth/pages/login/login';
import { Register } from './features/auth/pages/register/register';
import { Interests } from './features/auth/pages/interests/interests';
import { EditProfile } from './features/profile/pages/edit-profile/edit-profile';
import { Settings } from './features/settings/pages/settings/settings';
import { MyInterests } from './features/settings/pages/my-interests/my-interests';
import { Profile } from './features/profile/pages/profile/profile';

export const routes: Routes = [
    { path: '', redirectTo: 'profile', pathMatch: 'full' },
    { path: 'login', component: Login },
    { path: 'register', component: Register },
    { path: 'interests', component: Interests },
    { path: 'edit-profile', component: EditProfile },
    { path: 'settings', component: Settings },
    { path: 'my-interests', component: MyInterests },
    { path: 'profile', component: Profile },
    {
    path: 'profile/collections',
    loadComponent: () =>
        import('./features/profile/pages/my-collections-page/my-collections-page').then(
        (m) => m.MyCollectionsPageComponent
        ),
    },
    {
  path: 'profile/collections/:id',
    loadComponent: () =>
        import('./features/profile/pages/collection-detail/collection-detail')
        .then(m => m.CollectionDetailComponent),
    },
];

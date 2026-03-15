import { Routes } from '@angular/router';
import { DashboardComponent } from './features/dashboard/dashboard.component';
import { AuthComponent } from './features/auth/auth.component';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: 'login', component: AuthComponent },
  { path: '', component: DashboardComponent, canActivate: [authGuard] },
  {
    path: 'paper-trades',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/paper-trade-workbench/paper-trade-workbench.component').then((m) => m.PaperTradeWorkbenchComponent)
  },
  { path: '**', redirectTo: '' }
];

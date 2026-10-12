import { Routes } from '@angular/router';
import { LandingComponent } from './pages/landing/landing.component';
import { PredictComponent } from './pages/predict/predict.component';
import { GuideComponent } from './pages/guide/guide.component';
import { DesignerComponent } from './pages/designer/designer.component';
import { ResultsComponent } from './pages/results/results.component';
import { ThanaweyaResultComponent } from './pages/thanaweya-result/thanaweya-result.component';
import { AdminImportResultsComponent } from './pages/admin/import-results/import-results.component';
import { AdminLoginComponent } from './pages/admin/login/login.component';
import { AdminLayoutComponent } from './pages/admin/admin-layout.component';
import { AdminDashboardComponent } from './pages/admin/dashboard/dashboard.component';
import { AdminCutoffsComponent } from './pages/admin/cutoffs/cutoffs.component';
import { AdminImportComponent } from './pages/admin/import/import.component';
import { AdminYearsComponent } from './pages/admin/years/years.component';
import { adminGuard } from './guards/admin.guard';
import { importUploadGuard } from './guards/import-upload.guard';

export const routes: Routes = [
  { path: '', component: LandingComponent },
  { path: 'predict', component: PredictComponent },
  { path: 'thanaweya-result', component: ThanaweyaResultComponent },
  { path: 'guide', component: GuideComponent },
  { path: 'designer', component: DesignerComponent },
  { path: 'results', component: ResultsComponent },
  { path: 'admin/login', component: AdminLoginComponent },
  {
    path: 'admin',
    canActivate: [adminGuard],
    component: AdminLayoutComponent,
    children: [
      { path: '', component: AdminDashboardComponent },
      { path: 'years', component: AdminYearsComponent },
      { path: 'cutoffs', component: AdminCutoffsComponent },
      { path: 'import', component: AdminImportComponent, canDeactivate: [importUploadGuard] },
      {
        path: 'import-results',
        component: AdminImportResultsComponent,
        canDeactivate: [importUploadGuard],
      },
    ],
  },
  { path: '**', redirectTo: '' }
];

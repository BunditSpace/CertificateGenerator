import { Component } from '@angular/core';
import { JobProgressComponent } from './components/job-progress/job-progress.component';
import { TemplateDesignerComponent } from './components/template-designer/template-designer.component';
import { TemplateConfig } from './models/document.models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [TemplateDesignerComponent, JobProgressComponent],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  templateConfig: TemplateConfig = {
    orientation: 'Landscape',
    backgroundUrl: null,
    logoUrl: null,
    logoPosition: 'TopCenter',
    logoWidth: 120,
    watermarkUrl: null,
    watermarkOpacity: 0.15,
    fontFamily: 'Sarabun',
    fields: [
      {
        name: 'FullName',
        x: 120,
        y: 230,
        fontSize: 34,
        color: '#0f172a',
        fontWeight: 'Bold',
        align: 'Center',
        width: 600,
        sampleValue: 'สมชาย ใจดี',
      },
      {
        name: 'CourseName',
        x: 120,
        y: 300,
        fontSize: 20,
        color: '#334155',
        fontWeight: 'Normal',
        align: 'Center',
        width: 600,
        sampleValue: 'หลักสูตรการพัฒนาเว็บแอปพลิเคชันยุคใหม่',
      },
      {
        name: 'Date',
        x: 120,
        y: 365,
        fontSize: 16,
        color: '#64748b',
        fontWeight: 'Normal',
        align: 'Center',
        width: 600,
        sampleValue: '17 พฤศจิกายน 2569',
      },
    ],
  };

  onConfigChange(updated: TemplateConfig): void {
    this.templateConfig = updated;
  }
}

import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  GenerateJobQueuedResponse,
  JobStatusUpdate,
  PreviewRequest,
  TemplateConfig,
} from '../models/document.models';

@Injectable({
  providedIn: 'root',
})
export class DocumentApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiBaseUrl.replace(/\/+$/, '');

  uploadAsset(file: File): Observable<{ url: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ url: string }>(`${this.baseUrl}/api/assets/upload`, formData);
  }

  previewPdf(request: PreviewRequest): Observable<Blob> {
    return this.http.post(`${this.baseUrl}/api/documents/preview`, request, {
      responseType: 'blob',
    });
  }

  generateBatch(excelFile: File, config: TemplateConfig): Observable<GenerateJobQueuedResponse> {
    const formData = new FormData();
    formData.append('file', excelFile);
    formData.append('config', JSON.stringify(config));
    return this.http.post<GenerateJobQueuedResponse>(
      `${this.baseUrl}/api/documents/generate`,
      formData,
    );
  }

  getJobStatus(jobId: string): Observable<JobStatusUpdate> {
    return this.http.get<JobStatusUpdate>(`${this.baseUrl}/api/documents/status/${jobId}`);
  }
}

import { CommonModule } from '@angular/common';
import { Component, inject, Input, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { JobStatusUpdate, TemplateConfig } from '../../models/document.models';
import { DocumentApiService } from '../../services/document-api.service';
import { SseProgressService } from '../../services/sse-progress.service';

@Component({
  selector: 'app-job-progress',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './job-progress.component.html',
})
export class JobProgressComponent implements OnDestroy {
  private readonly api = inject(DocumentApiService);
  private readonly sse = inject(SseProgressService);

  @Input({ required: true }) config!: TemplateConfig;

  selectedExcelFile: File | null = null;
  isSubmitting = false;
  submitError: string | null = null;
  jobStatus: JobStatusUpdate | null = null;

  private streamSubscription: Subscription | null = null;

  get progressPercentage(): number {
    if (!this.jobStatus || this.jobStatus.totalRecords <= 0) {
      return this.jobStatus?.status === 'Completed' ? 100 : 0;
    }
    return Math.min(
      100,
      Math.round((this.jobStatus.progress / this.jobStatus.totalRecords) * 100),
    );
  }

  onExcelSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.selectedExcelFile = file;
    this.submitError = null;
  }

  startBatchGeneration(): void {
    if (!this.selectedExcelFile) {
      this.submitError = 'กรุณาเลือกไฟล์ Excel (.xlsx) ก่อนเริ่มสร้างเอกสาร';
      return;
    }

    this.streamSubscription?.unsubscribe();
    this.isSubmitting = true;
    this.submitError = null;
    this.jobStatus = null;

    this.api.generateBatch(this.selectedExcelFile, this.config).subscribe({
      next: (queued) => {
        this.isSubmitting = false;
        this.jobStatus = {
          jobId: queued.jobId,
          status: queued.status,
          progress: 0,
          totalRecords: 0,
          updatedAt: new Date().toISOString(),
        };
        this.subscribeToJobStream(queued.jobId);
      },
      error: (err) => {
        this.isSubmitting = false;
        this.submitError =
          err?.error?.error ?? err?.message ?? 'ส่งคำขอสร้างเอกสารไม่สำเร็จ';
      },
    });
  }

  refreshStatus(): void {
    if (!this.jobStatus?.jobId) return;
    this.api.getJobStatus(this.jobStatus.jobId).subscribe({
      next: (status) => {
        this.jobStatus = status;
      },
      error: (err) => {
        this.submitError = err?.error?.error ?? 'ไม่สามารถดึงสถานะล่าสุดได้';
      },
    });
  }

  private subscribeToJobStream(jobId: string): void {
    this.streamSubscription = this.sse.streamJobProgress(jobId).subscribe({
      next: (update) => {
        this.jobStatus = update;
      },
      error: () => {
        // Fallback to REST status endpoint if SSE drops
        this.refreshStatus();
      },
    });
  }

  ngOnDestroy(): void {
    this.streamSubscription?.unsubscribe();
  }
}

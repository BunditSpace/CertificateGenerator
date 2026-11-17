import { CdkDragEnd, DragDropModule } from '@angular/cdk/drag-drop';
import { CommonModule } from '@angular/common';
import { Component, EventEmitter, inject, Input, OnDestroy, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import {
  LogoPosition,
  Orientation,
  TemplateConfig,
  TemplateFieldConfig,
} from '../../models/document.models';
import { DocumentApiService } from '../../services/document-api.service';

@Component({
  selector: 'app-template-designer',
  standalone: true,
  imports: [CommonModule, FormsModule, DragDropModule],
  templateUrl: './template-designer.component.html',
})
export class TemplateDesignerComponent implements OnDestroy {
  private readonly api = inject(DocumentApiService);
  private readonly sanitizer = inject(DomSanitizer);

  @Input({ required: true }) config!: TemplateConfig;
  @Output() configChange = new EventEmitter<TemplateConfig>();

  selectedFieldIndex = 0;
  uploadingAsset: 'background' | 'logo' | 'watermark' | null = null;
  uploadError: string | null = null;

  isGeneratingPreview = false;
  previewError: string | null = null;
  previewModalOpen = false;
  previewPdfSafeUrl: SafeResourceUrl | null = null;
  private rawPreviewObjectUrl: string | null = null;

  // A4 dimensions in PDF points (72 dpi)
  get pageWidthPt(): number {
    return this.config.orientation === 'Portrait' ? 595 : 842;
  }

  get pageHeightPt(): number {
    return this.config.orientation === 'Portrait' ? 842 : 595;
  }

  // Scale factor so the A4 canvas fits comfortably in the designer pane
  get previewScale(): number {
    return this.config.orientation === 'Portrait' ? 0.72 : 0.78;
  }

  get selectedField(): TemplateFieldConfig | null {
    return this.config.fields[this.selectedFieldIndex] ?? null;
  }

  onOrientationChange(orientation: Orientation): void {
    this.config.orientation = orientation;
    this.emitChange();
  }

  onLogoPositionChange(position: LogoPosition): void {
    this.config.logoPosition = position;
    this.emitChange();
  }

  emitChange(): void {
    this.configChange.emit({
      ...this.config,
      fields: this.config.fields.map((f) => ({ ...f })),
    });
  }

  onAssetSelected(event: Event, kind: 'background' | 'logo' | 'watermark'): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    this.uploadingAsset = kind;
    this.uploadError = null;

    this.api.uploadAsset(file).subscribe({
      next: ({ url }) => {
        if (kind === 'background') this.config.backgroundUrl = url;
        if (kind === 'logo') this.config.logoUrl = url;
        if (kind === 'watermark') this.config.watermarkUrl = url;
        this.uploadingAsset = null;
        this.emitChange();
      },
      error: (err) => {
        this.uploadingAsset = null;
        this.uploadError = `อัปโหลดรูป ${kind} ไม่สำเร็จ: ${err?.error?.error ?? err?.message ?? 'Unknown error'}`;
      },
    });

    input.value = '';
  }

  clearAsset(kind: 'background' | 'logo' | 'watermark'): void {
    if (kind === 'background') this.config.backgroundUrl = null;
    if (kind === 'logo') this.config.logoUrl = null;
    if (kind === 'watermark') this.config.watermarkUrl = null;
    this.emitChange();
  }

  addField(): void {
    const nextIndex = this.config.fields.length + 1;
    this.config.fields.push({
      name: `Field${nextIndex}`,
      x: 120,
      y: 160 + nextIndex * 36,
      fontSize: 22,
      color: '#111827',
      fontWeight: 'Normal',
      align: 'Center',
      width: 500,
      sampleValue: `Sample Text ${nextIndex}`,
    });
    this.selectedFieldIndex = this.config.fields.length - 1;
    this.emitChange();
  }

  removeField(index: number): void {
    this.config.fields.splice(index, 1);
    if (this.selectedFieldIndex >= this.config.fields.length) {
      this.selectedFieldIndex = Math.max(0, this.config.fields.length - 1);
    }
    this.emitChange();
  }

  onFieldDragEnded(event: CdkDragEnd, index: number): void {
    const field = this.config.fields[index];
    if (!field) return;

    const delta = event.source.getFreeDragPosition();
    const nextX = Math.max(0, Math.round(field.x + delta.x / this.previewScale));
    const nextY = Math.max(0, Math.round(field.y + delta.y / this.previewScale));

    field.x = nextX;
    field.y = nextY;
    event.source.reset();
    this.selectedFieldIndex = index;
    this.emitChange();
  }

  getLogoContainerClasses(): string {
    switch (this.config.logoPosition) {
      case 'TopLeft':
        return 'top-5 left-5';
      case 'TopRight':
        return 'top-5 right-5';
      case 'BottomLeft':
        return 'bottom-5 left-5';
      case 'BottomRight':
        return 'bottom-5 right-5';
      default:
        return 'top-5 left-1/2 -translate-x-1/2';
    }
  }

  openPdfPreview(): void {
    this.isGeneratingPreview = true;
    this.previewError = null;

    const sampleData: Record<string, string> = {};
    for (const field of this.config.fields) {
      if (field.name.trim()) {
        sampleData[field.name.trim()] = field.sampleValue?.trim() || `[${field.name}]`;
      }
    }

    this.api.previewPdf({ config: this.config, sampleData }).subscribe({
      next: (blob) => {
        this.revokePreviewUrl();
        this.rawPreviewObjectUrl = URL.createObjectURL(blob);
        this.previewPdfSafeUrl = this.sanitizer.bypassSecurityTrustResourceUrl(
          this.rawPreviewObjectUrl,
        );
        this.previewModalOpen = true;
        this.isGeneratingPreview = false;
      },
      error: (err) => {
        this.isGeneratingPreview = false;
        this.previewError =
          err?.error?.error ?? err?.message ?? 'ไม่สามารถสร้างตัวอย่าง PDF ได้ กรุณาตรวจสอบว่า Backend ทำงานอยู่';
      },
    });
  }

  closePreviewModal(): void {
    this.previewModalOpen = false;
    this.revokePreviewUrl();
  }

  private revokePreviewUrl(): void {
    if (this.rawPreviewObjectUrl) {
      URL.revokeObjectURL(this.rawPreviewObjectUrl);
      this.rawPreviewObjectUrl = null;
      this.previewPdfSafeUrl = null;
    }
  }

  ngOnDestroy(): void {
    this.revokePreviewUrl();
  }
}

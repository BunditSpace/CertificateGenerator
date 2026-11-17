export type Orientation = 'Landscape' | 'Portrait';
export type LogoPosition = 'TopLeft' | 'TopCenter' | 'TopRight' | 'BottomLeft' | 'BottomRight';
export type FontWeight = 'Normal' | 'Bold';
export type TextAlign = 'Left' | 'Center' | 'Right';

export interface TemplateFieldConfig {
  name: string;
  x: number;
  y: number;
  fontSize: number;
  color: string;
  fontWeight: FontWeight;
  align: TextAlign;
  width?: number;
  sampleValue?: string;
}

export interface TemplateConfig {
  orientation: Orientation;
  backgroundUrl?: string | null;
  logoUrl?: string | null;
  logoPosition: LogoPosition;
  logoWidth: number;
  watermarkUrl?: string | null;
  watermarkOpacity: number;
  fontFamily: string;
  fields: TemplateFieldConfig[];
}

export interface PreviewRequest {
  config: TemplateConfig;
  sampleData: Record<string, string>;
}

export type JobStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed';

export interface JobStatusUpdate {
  jobId: string;
  status: JobStatus;
  progress: number;
  totalRecords: number;
  zipDownloadUrl?: string | null;
  errorMessage?: string | null;
  updatedAt: string;
}

export interface GenerateJobQueuedResponse {
  jobId: string;
  status: JobStatus;
  streamUrl: string;
  statusUrl: string;
}

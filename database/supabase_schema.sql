-- ============================================================================
-- Bulk Certificate & Payslip Generator - Supabase Database & Storage Setup
-- Run this script in Supabase Dashboard -> SQL Editor
-- ============================================================================

-- 1. Enable UUID extension (usually enabled by default in Supabase)
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- 2. Create `document_jobs` table
CREATE TABLE IF NOT EXISTS public.document_jobs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    status TEXT NOT NULL DEFAULT 'Queued'
        CONSTRAINT chk_document_jobs_status CHECK (status IN ('Queued', 'Processing', 'Completed', 'Failed')),
    progress INTEGER NOT NULL DEFAULT 0
        CONSTRAINT chk_document_jobs_progress CHECK (progress >= 0),
    total_records INTEGER NOT NULL DEFAULT 0
        CONSTRAINT chk_document_jobs_total_records CHECK (total_records >= 0),
    zip_download_url TEXT NULL,
    error_message TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Indexes for common queries (status lookup & recent jobs ordering)
CREATE INDEX IF NOT EXISTS idx_document_jobs_created_at
    ON public.document_jobs (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_document_jobs_status
    ON public.document_jobs (status);

-- Auto-update `updated_at` timestamp on row modification
CREATE OR REPLACE FUNCTION public.set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_document_jobs_updated_at ON public.document_jobs;
CREATE TRIGGER trg_document_jobs_updated_at
    BEFORE UPDATE ON public.document_jobs
    FOR EACH ROW
    EXECUTE FUNCTION public.set_updated_at();

-- Enable Row Level Security (RLS)
-- Note: Backend using Supabase Service Role Key bypasses RLS automatically.
ALTER TABLE public.document_jobs ENABLE ROW LEVEL SECURITY;

-- Optional: Allow anonymous/authenticated read-only access to job status by ID
-- Remove or tighten if you add user authentication later.
DROP POLICY IF EXISTS "Allow public read access to document_jobs" ON public.document_jobs;
CREATE POLICY "Allow public read access to document_jobs"
    ON public.document_jobs
    FOR SELECT
    USING (true);

-- ============================================================================
-- 3. Create Supabase Storage Buckets
-- ============================================================================

-- Bucket: templates (Backgrounds, Logos, Watermarks)
INSERT INTO storage.buckets (id, name, public)
VALUES ('templates', 'templates', true)
ON CONFLICT (id) DO NOTHING;

-- Bucket: raw-uploads (Uploaded Excel files - private)
INSERT INTO storage.buckets (id, name, public)
VALUES ('raw-uploads', 'raw-uploads', false)
ON CONFLICT (id) DO NOTHING;

-- Bucket: outputs (Generated .zip files - public or signed URLs)
INSERT INTO storage.buckets (id, name, public)
VALUES ('outputs', 'outputs', true)
ON CONFLICT (id) DO NOTHING;

-- Storage Policies for `templates` (Public Read)
DROP POLICY IF EXISTS "Public read access for templates" ON storage.objects;
CREATE POLICY "Public read access for templates"
    ON storage.objects
    FOR SELECT
    USING (bucket_id = 'templates');

-- Storage Policies for `outputs` (Public Read)
DROP POLICY IF EXISTS "Public read access for outputs" ON storage.objects;
CREATE POLICY "Public read access for outputs"
    ON storage.objects
    FOR SELECT
    USING (bucket_id = 'outputs');

-- ============================================================================
-- Auto-Cleanup for Supabase Free Tier (1 GB Storage Limit)
-- - `outputs` & `raw-uploads`: deleted after 24 hours
-- - `templates`: deleted after 7 days (adjust `template_retention` as needed)
-- Run this script in Supabase Dashboard -> SQL Editor.
-- ============================================================================

-- 1. Enable pg_cron extension (if not already enabled)
CREATE EXTENSION IF NOT EXISTS pg_cron;

-- 2. Cleanup function
CREATE OR REPLACE FUNCTION public.cleanup_old_certificate_files(
    job_file_retention INTERVAL DEFAULT INTERVAL '24 hours',
    template_retention INTERVAL DEFAULT INTERVAL '7 days'
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    -- Clean up temporary Excel uploads and generated ZIP outputs
    DELETE FROM storage.objects
    WHERE bucket_id IN ('outputs', 'raw-uploads')
      AND created_at < NOW() - job_file_retention;

    -- Clean up old template assets (backgrounds, logos, watermarks)
    DELETE FROM storage.objects
    WHERE bucket_id = 'templates'
      AND created_at < NOW() - template_retention;
END;
$$;

-- 3. Schedule to run every hour at minute 0 (idempotent)
SELECT cron.unschedule('cleanup-old-certificate-files')
WHERE EXISTS (
    SELECT 1 FROM cron.job WHERE jobname = 'cleanup-old-certificate-files'
);

SELECT cron.schedule(
    'cleanup-old-certificate-files',
    '0 * * * *',
    $$SELECT public.cleanup_old_certificate_files();$$
);

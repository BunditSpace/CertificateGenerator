-- ============================================================================
-- Optional: Auto-Cleanup for Supabase Free Tier (1 GB Storage Limit)
-- Deletes storage objects in `outputs` and `raw-uploads` older than 24 hours,
-- and cleans up old job rows if desired.
-- Note: Enable `pg_cron` extension in Supabase Dashboard -> Database -> Extensions
-- before scheduling this job.
-- ============================================================================

CREATE OR REPLACE FUNCTION public.cleanup_old_certificate_files()
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
BEGIN
    DELETE FROM storage.objects
    WHERE bucket_id IN ('outputs', 'raw-uploads')
      AND created_at < NOW() - INTERVAL '24 hours';
END;
$$;

-- Schedule to run every hour (requires pg_cron extension enabled):
-- SELECT cron.schedule(
--     'cleanup-old-certificate-files',
--     '0 * * * *',
--     $$SELECT public.cleanup_old_certificate_files();$$
-- );

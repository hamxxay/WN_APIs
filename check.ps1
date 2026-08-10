$lines = Get-Content 'f:\WN_APIs\SQL\StoredProcedure.sql'
Write-Host "Current lines:" $lines.Count

# The file is corrupted (185 lines). We need to check if it still has content
# If not, we need to rebuild from scratch using the known good content

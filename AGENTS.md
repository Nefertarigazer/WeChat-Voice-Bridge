# WeChat database probes

- For live probes, use `tempfile.TemporaryDirectory()` on the configured local scratch drive outside OneDrive. Fix older probes that use bare `mkdtemp()` before rerunning them. Close SQLite connections and stop any child processes before leaving the temporary directory context, including when a probe fails.
- Reuse a single snapshot within each batch of read-only queries. Keep database copies and their WAL and SHM files out of the project, test reports, and published artifacts.

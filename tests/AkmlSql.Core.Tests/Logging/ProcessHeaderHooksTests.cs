using System;
using System.IO;
using System.Linq;
using AkmlSql.Core.Logging;
using Serilog;
using Xunit;

namespace AkmlSql.Core.Tests.Logging
{
    /// <summary>
    /// Every process logs to the same rolling file name, so a day's log is spread over
    /// <c>_001</c>, <c>_002</c>, … and a size or day roll can append to a file another process
    /// wrote. The header written on every open is how the log viewer attributes the entries that
    /// follow it; these tests drive Serilog's real file sink, not a fake.
    /// </summary>
    public sealed class ProcessHeaderHooksTests : IDisposable
    {
        private const string Template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

        // Same shape as the output template, so readers parse it as an ordinary entry.
        private const string HeaderShape =
            @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2} \[INF\] AKML SQL \S+ log file opened by AkmlSql\.Engine \(pid 42\)$";

        private readonly string _dir;

        public ProcessHeaderHooksTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "akml-header-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void Every_file_a_size_roll_opens_starts_with_the_header()
        {
            using (var log = new LoggerConfiguration()
                .WriteTo.File(Path.Combine(_dir, "akmlsql-.log"),
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 600,
                    outputTemplate: Template,
                    hooks: new ProcessHeaderHooks("AkmlSql.Engine", 42))
                .CreateLogger())
            {
                for (var i = 0; i < 30; i++)
                    log.Information("entry {Index} padded so the 600-byte limit rolls the file several times", i);
            }

            var files = Directory.GetFiles(_dir, "akmlsql-*.log");
            Assert.True(files.Length > 2, $"expected several rolled files, got {files.Length}");
            foreach (var file in files)
                Assert.Matches(HeaderShape, File.ReadLines(file).First());
        }

        [Fact]
        public void Appending_to_a_file_another_process_wrote_adds_a_header_before_the_new_entries()
        {
            var path = Path.Combine(_dir, "akmlsql-20261013_008.log");
            File.WriteAllLines(path, new[]
            {
                "2026-10-13 22:56:10.820 +03:00 [INF] AKML SQL 1.26.1013.1822 logger initialized",
                "2026-10-13 22:56:12.663 +03:00 [INF] No update available",
            });

            using (var log = new LoggerConfiguration()
                .WriteTo.File(path, outputTemplate: Template, hooks: new ProcessHeaderHooks("AkmlSql.Engine", 42))
                .CreateLogger())
            {
                log.Error("AI provider test failed after {Ms}ms", 370);
            }

            var lines = File.ReadAllLines(path);
            Assert.Equal(4, lines.Length);
            Assert.EndsWith("No update available", lines[1]);
            Assert.Matches(HeaderShape, lines[2]);
            Assert.EndsWith("[ERR] AI provider test failed after 370ms", lines[3]);
        }

        [Fact]
        public void FormatHeader_uses_the_output_template_timestamp_shape()
        {
            var line = ProcessHeaderHooks.FormatHeader(
                new DateTimeOffset(2026, 10, 13, 23, 0, 58, 947, TimeSpan.FromHours(3)), "1.26.1013.2300", "SSMS", 27504);

            Assert.Equal("2026-10-13 23:00:58.947 +03:00 [INF] AKML SQL 1.26.1013.2300 log file opened by SSMS (pid 27504)", line);
        }

        [Fact]
        public void A_blank_process_name_still_writes_a_parseable_header()
        {
            using var stream = new MemoryStream();
            var hooks = new ProcessHeaderHooks(" ", 7);

            var returned = hooks.OnFileOpened("x.log", stream, new System.Text.UTF8Encoding(false));

            Assert.Same(stream, returned);
            Assert.Contains("log file opened by unknown (pid 7)", System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
    }
}

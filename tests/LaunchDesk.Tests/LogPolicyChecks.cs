using System.Text.Json;
using LaunchDesk.Core;

internal static class LogPolicyChecks
{
    public static async Task Run(string root, Func<string, Func<Task>, Task> test)
    {
        foreach (var offset in new[] { -12, 0, 8, 14 })
            await test($"Log policy: UTC and local midnight at offset {offset}", () =>
            {
                var clock = new Clock(offset) { Utc = DateTimeOffset.Parse("2026-01-01T23:59:59Z") };
                using var log = new LogStore(root, Guid.NewGuid(), new(), clock);
                log.Write("stdout", "before UTC midnight");
                clock.Utc = clock.Utc.AddSeconds(2); log.Write("stdout", "after UTC midnight");
                var files = Directory.GetFiles(log.DirectoryPath).Order().ToArray();
                Check(files.Length == 2 && Path.GetFileName(files[0]).StartsWith("20260101-")
                    && Path.GetFileName(files[1]).StartsWith("20260102-"), "UTC rotation/name mismatch");
                Check(log.Snapshot().All(x => x.Time.Offset == TimeSpan.FromHours(offset)), "display offset lost");
                using var local = new LogStore(root, Guid.NewGuid(), new(), clock);
                clock.Utc = new DateTimeOffset(2026, 1, 4, 23, 59, 59, TimeSpan.FromHours(offset)).ToUniversalTime();
                local.Write("stdout", "before local midnight");
                clock.Utc = clock.Utc.AddSeconds(2); local.Write("stdout", "after local midnight");
                Check(Directory.GetFiles(local.DirectoryPath).Length == (offset == 0 ? 2 : 1), "local midnight changed UTC day");
                foreach (var file in Directory.GetFiles(local.DirectoryPath))
                {
                    using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                    var record = JsonSerializer.Deserialize<LogRecord>(reader.ReadLine()!)!;
                    Check(Path.GetFileName(file).StartsWith(record.Time.UtcDateTime.ToString("yyyyMMdd-")), "filename uses a different instant/date");
                }
                using var changedZone = new LogStore(root, Guid.NewGuid(), new(), clock);
                clock.Utc = DateTimeOffset.Parse("2026-01-06T12:00:00Z"); changedZone.Write("stdout", "before zone change");
                clock.Zone = TimeZoneInfo.CreateCustomTimeZone("changed", TimeSpan.FromHours(-offset), "changed", "changed");
                clock.Utc = clock.Utc.AddSeconds(1); changedZone.Write("stdout", "after zone change");
                Check(Directory.GetFiles(changedZone.DirectoryPath).Length == 1 && changedZone.Snapshot()[1].Time.Offset == TimeSpan.FromHours(-offset), "zone change rotated UTC file or lost offset");
                return Task.CompletedTask;
            });
        await test("Log policy: ring wraparound and stable snapshot/delta references", () =>
        {
            using var log = new LogStore(root, Guid.NewGuid(), new());
            log.Write("stdout", "same"); var record = log.Snapshot().Single(); var cursor = log.ReadSince(-1).Cursor;
            Check(ReferenceEquals(record, log.Snapshot().Single()) && ReferenceEquals(record, log.ReadSince(-1).Records.Single()), "record reference changed");
            var projection = new LogProjection(); var id = Guid.NewGuid();
            projection.Update([(id, "source", log)], false, null, "");
            projection.Update([(id, "source", log)], false, null, "");
            Check(projection.FormattedRecords == 0, "unchanged record reformatted");
            for (var i = 0; i < LogStore.MaximumMemoryRecords * 2 + 1; i++) log.Write("stdout", "same");
            var snapshot = log.Snapshot(); var delta = log.ReadSince(cursor);
            Check(snapshot.Length == LogStore.MaximumMemoryRecords && delta.Reset && snapshot.Zip(delta.Records).All(x => ReferenceEquals(x.First, x.Second)), "ring wrap/reset identity mismatch");
            projection.Update([(id, "source", log)], false, null, "");
            for (var i = 0; i < 20; i++)
            {
                log.Write("stdout", "same"); projection.Update([(id, "source", log)], false, null, "");
                Check(projection.FormattedRecords == 1 && projection.CachedRecords <= LogStore.MaximumMemoryRecords, "rolling cache not bounded/incremental");
            }
            log.ClearPanel(); projection.Update([(id, "source", log)], false, null, "");
            Check(projection.CachedRecords == 0, "clear retains cache entries");
            return Task.CompletedTask;
        });
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private sealed class Clock(int offset) : TimeProvider
    {
        public DateTimeOffset Utc { get; set; }
        public override DateTimeOffset GetUtcNow() => Utc;
        public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.CreateCustomTimeZone("test-" + offset, TimeSpan.FromHours(offset), "test", "test");
        public override TimeZoneInfo LocalTimeZone => Zone;
    }
}

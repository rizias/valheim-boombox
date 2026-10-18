using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace BoomboxQa
{
    public static class QaPerfArtifacts
    {
        public static void PreserveFailed(QaLedger ledger)
        {
            string folder = Path.Combine(ledger.Root, "evidence/R11");
            string timing = Path.Combine(folder, "perf-probe-timing.json");
            if (!File.Exists(timing))
                return;
            using var previous = JsonDocument.Parse(File.ReadAllText(timing));
            if (previous.RootElement.GetProperty("passed").GetBoolean())
                throw new IOException("Existing successful probe requires an affected-case reason before repetition");
            int attempt = Enumerable.Range(1, 3).FirstOrDefault(
                n => !File.Exists(Path.Combine(folder, "perf-probe-attempt-" + n + "-timing.json"))
            );
            if (attempt == 0)
                throw new IOException("Three retained probe failures require owner diagnosis");
            foreach (string suffix in new[] { "-timing.json", "-restore.json", ".json", ".csv" })
            {
                string path = Path.Combine(folder, "perf-probe" + suffix);
                if (!File.Exists(path))
                    continue;
                QaFiles.Copy(ledger, path, Path.Combine(folder, "perf-probe-attempt-" + attempt + suffix));
                ledger.RemoveOwned(path);
            }
        }
        public static void Validate(QaLedger ledger)
        {
            string csv = Path.Combine(ledger.Root, "evidence/R11/perf-probe.csv");
            using var parser = new TextFieldParser(
                csv
            )
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(",");
            string[] header = parser.ReadFields()!;
            if (header.Length != 35 || header[34] != "resources")
                throw new IOException("Unexpected actual CSV header");
            long rows = 0;
            var samples = new System.Collections.Generic.HashSet<long>();
            while (!parser.EndOfData)
            {
                string[] fields = parser.ReadFields()!;
                if (fields.Length != header.Length || fields[34].Split(';').Length != 12
                || fields[34].Split(';').Any(value => !int.TryParse(value, out _)))
                    throw new IOException("CSV parser/resource columns mismatch");
                samples.Add(long.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture));
                rows++;
            }
            using var proof = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(ledger.Root, "evidence/R11/perf-probe.json"))
            );
            if (samples.Count != proof.RootElement.GetProperty("samples").GetInt64()
                || samples.Count == 0
                || samples.Min() != 0
                || samples.Max() != samples.Count - 1)
                throw new IOException("CSV actual sample sequence mismatch");
            QaFiles.Write(
                ledger,
                Path.Combine(ledger.Root, "evidence/R11/perf-probe-csv.json"),
                JsonSerializer.SerializeToUtf8Bytes(new
                {
                    passed = true,
                    independentParser = "Microsoft.VisualBasic.FileIO.TextFieldParser",
                    columns = header.Length,
                    resourceValues = 12,
                    rows,
                    samples = samples.Count,
                    sha = QaFiles.Hash(csv)
                })
            );
        }
    }
}

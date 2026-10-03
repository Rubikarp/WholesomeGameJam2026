using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Source-of-truth state for the data migrator: per-package version stamps (the trigger — keyed by
    /// package name, one per <see cref="IMigratedPackage"/>) and the ids of one-shot migrations already
    /// applied. Committed under <c>ProjectSettings/</c> so it is shared through version control.
    /// </summary>
    [Serializable]
    internal sealed class MigrationLedger
    {
        const int CurrentFormat = 1;

        public int format = CurrentFormat;
        public List<PackageStamp> packages = new();
        public List<string> completedMigrations = new();

        const string Path = "ProjectSettings/LightSide/MigrationLedger.json";

        [Serializable]
        public struct PackageStamp
        {
            public string name;
            public string version;
        }

        public string StampOf(string package)
        {
            foreach (var stamp in packages)
                if (stamp.name == package) return stamp.version;
            return null;
        }

        /// <summary>Records the version, reporting whether it differed from the stored stamp.</summary>
        public bool Stamp(string package, string version)
        {
            for (int i = 0; i < packages.Count; i++)
                if (packages[i].name == package)
                {
                    if (packages[i].version == version) return false;
                    packages[i] = new PackageStamp { name = package, version = version };
                    return true;
                }
            packages.Add(new PackageStamp { name = package, version = version });
            return true;
        }

        public static MigrationLedger Load()
        {
            if (!File.Exists(Path)) return new MigrationLedger();
            try
            {
                var json = File.ReadAllText(Path);
                if (json.IndexOf("\"packages\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"completedMigrations\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("The required collections are missing.");

                var ledger = JsonUtility.FromJson<MigrationLedger>(json);
                if (ledger == null)
                    throw new InvalidDataException("The document is empty.");
                if (json.IndexOf("\"format\"", StringComparison.Ordinal) < 0)
                    ledger.format = CurrentFormat;
                ledger.Validate();
                return ledger;
            }
            catch (Exception e)
            {
                throw new InvalidDataException(
                    $"[LightSide] Cannot read migration ledger '{Path}'. Restore or repair it before upgrading.", e);
            }
        }

        public void Save()
        {
            Validate();
            MigrationFile.WriteAllText(Path, JsonUtility.ToJson(this, true));
        }

        void Validate()
        {
            if (format != CurrentFormat)
                throw new InvalidDataException(
                    $"[LightSide] Migration ledger '{Path}' uses unsupported format {format}.");
            if (packages == null || completedMigrations == null)
                throw new InvalidDataException(
                    $"[LightSide] Migration ledger '{Path}' has null state collections.");

            var packageNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stamp in packages)
            {
                if (string.IsNullOrEmpty(stamp.name) || string.IsNullOrEmpty(stamp.version))
                    throw new InvalidDataException(
                        $"[LightSide] Migration ledger '{Path}' contains an incomplete package stamp.");
                if (!packageNames.Add(stamp.name))
                    throw new InvalidDataException(
                        $"[LightSide] Migration ledger '{Path}' contains duplicate package '{stamp.name}'.");
            }

            var migrationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in completedMigrations)
            {
                if (string.IsNullOrEmpty(id))
                    throw new InvalidDataException(
                        $"[LightSide] Migration ledger '{Path}' contains an empty migration id.");
                if (!migrationIds.Add(id))
                    throw new InvalidDataException(
                        $"[LightSide] Migration ledger '{Path}' contains duplicate migration '{id}'.");
            }
        }
    }

    internal static class MigrationFile
    {
        public static void WriteAllText(string path, string content)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = System.IO.Path.Combine(
                string.IsNullOrEmpty(directory) ? "." : directory,
                $".ls-migration-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, content);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            catch (Exception writeFailure)
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        $"[LightSide] Cannot write '{path}' or remove its temporary file.",
                        writeFailure, cleanupFailure);
                }
                throw new IOException($"[LightSide] Cannot write '{path}'.", writeFailure);
            }
        }
    }
}

// Copyright (c) Zankai LLC. See LICENSE.md for license terms.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Rulesets.Sticks.Beatmaps
{
    /// <summary>
    /// Exports one authored Sticks difficulty as an importable <c>.osz</c> while preserving its
    /// mode-0 carrier file and every non-beatmap resource from the containing set.
    /// </summary>
    /// <remarks>
    /// Stock lazer's legacy exporter first converts a stored beatmap into its database ruleset.
    /// That turns the Sticks carrier back into custom hit objects, which the legacy encoder then
    /// rejects. Sticks beatmap files are already saved as valid mode-0 carriers, so this exporter
    /// deliberately packages the stored file without re-encoding its hit objects. Online IDs are
    /// removed from the packaged copy so importing it cannot collide with an official beatmap set.
    /// </remarks>
    public sealed class SticksBeatmapPackageExporter : LegacyArchiveExporter<BeatmapSetInfo>
    {
        private readonly Guid selectedBeatmapId;

        public SticksBeatmapPackageExporter(Storage storage, Guid selectedBeatmapId)
            : base(storage)
        {
            this.selectedBeatmapId = selectedBeatmapId;
        }

        protected override bool UseFixedEncoding => false;

        protected override string FileExtension => ".osz";

        protected override string GetFilename(BeatmapSetInfo item)
        {
            BeatmapInfo selected = getSelected(item);
            return $"{selected.Metadata.Artist} - {selected.Metadata.Title} ({selected.Metadata.Author.Username}) [{selected.DifficultyName}]";
        }

        public override void ExportToStream(BeatmapSetInfo model, Stream outputStream, ProgressNotification? notification, CancellationToken cancellationToken = default)
        {
            BeatmapInfo selected = getSelected(model);
            if (!string.Equals(selected.Ruleset.ShortName, "sticks", StringComparison.Ordinal))
                throw new InvalidOperationException("Only authored Sticks difficulties can be exported by the Sticks package exporter.");

            string selectedPath = selected.Path
                                  ?? throw new InvalidOperationException("The selected Sticks difficulty has no stored beatmap file.");

            // Detach() intentionally returns the same instance for an already-unmanaged Realm
            // object. Export is commonly invoked with such a model, so construct a fresh package
            // unconditionally rather than risking deletion of difficulties/files from the caller.
            var package = new BeatmapSetInfo
            {
                OnlineID = model.OnlineID,
                DateAdded = model.DateAdded,
                DateSubmitted = model.DateSubmitted,
                DateRanked = model.DateRanked,
                Status = model.Status,
                DeletePending = model.DeletePending,
                Hash = model.Hash,
                Protected = model.Protected,
            };

            BeatmapInfo packagedSelected = selected.Clone();
            packagedSelected.BeatmapSet = package;
            package.Beatmaps.Add(packagedSelected);

            var copiedFiles = new Dictionary<string, RealmFile>(StringComparer.Ordinal);

            foreach (RealmNamedFileUsage file in model.Files)
            {
                bool isSelectedCarrier = string.Equals(file.Filename, selectedPath, StringComparison.OrdinalIgnoreCase);

                // Nested or otherwise-unmodelled .osu files are still collision-capable on a
                // subsequent import. A one-difficulty export must contain exactly one carrier.
                if (!isSelectedCarrier && isLegacyBeatmapFile(file.Filename))
                    continue;

                if (!copiedFiles.TryGetValue(file.File.Hash, out RealmFile? copiedFile))
                {
                    copiedFile = new RealmFile { Hash = file.File.Hash };
                    copiedFiles.Add(file.File.Hash, copiedFile);
                }

                package.Files.Add(new RealmNamedFileUsage(copiedFile, file.Filename));
            }

            if (package.Beatmaps.Count != 1
                || !package.Files.Any(file => string.Equals(file.Filename, selectedPath, StringComparison.OrdinalIgnoreCase))
                || !string.Equals(packagedSelected.Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The selected Sticks carrier could not be isolated for export.");

            base.ExportToStream(package, outputStream, notification, cancellationToken);
        }

        protected override Stream? GetFileContents(BeatmapSetInfo model, INamedFileUsage file)
        {
            Stream? stored = base.GetFileContents(model, file);
            string? carrierPath = model.Beatmaps.SingleOrDefault()?.Path;

            if (stored == null
                || carrierPath == null
                || !string.Equals(file.Filename, carrierPath, StringComparison.OrdinalIgnoreCase))
                return stored;

            // Rewrite only the archive entry; the database model and stored editor file remain
            // untouched. The byte parser preserves the original BOM, line endings, and every byte
            // outside the two decoder-recognised metadata lines.
            using (stored)
            using (var buffer = new MemoryStream())
            {
                stored.CopyTo(buffer);
                byte[] carrier = buffer.ToArray();
                return new MemoryStream(StripOnlineIds(carrier), writable: false);
            }
        }

        /// <summary>
        /// Removes legacy online-ID metadata without normalising the rest of the carrier file.
        /// Returns <paramref name="carrier"/> itself when no matching metadata line is present.
        /// </summary>
        internal static byte[] StripOnlineIds(byte[] carrier)
        {
            ArgumentNullException.ThrowIfNull(carrier);

            MemoryStream? output = null;
            int retainedStart = 0;
            int lineStart = 0;
            bool firstLine = true;

            while (lineStart < carrier.Length)
            {
                int contentEnd = lineStart;
                while (contentEnd < carrier.Length && carrier[contentEnd] is not (byte)'\r' and not (byte)'\n')
                    contentEnd++;

                int lineEnd = contentEnd;
                if (lineEnd < carrier.Length && carrier[lineEnd] == (byte)'\r')
                {
                    lineEnd++;
                    if (lineEnd < carrier.Length && carrier[lineEnd] == (byte)'\n')
                        lineEnd++;
                }
                else if (lineEnd < carrier.Length)
                    lineEnd++;

                ReadOnlySpan<byte> content = carrier.AsSpan(lineStart, contentEnd - lineStart);
                if (firstLine && content.StartsWith(Encoding.UTF8.Preamble))
                    content = content[Encoding.UTF8.Preamble.Length..];

                if (isOnlineIdLine(content))
                {
                    output ??= new MemoryStream(carrier.Length);
                    output.Write(carrier, retainedStart, lineStart - retainedStart);

                    // A UTF-8 preamble belongs to the stream, not to the first metadata line.
                    // Keep it even when an ID happens to be the very first line removed.
                    if (firstLine && lineStart == 0 && carrier.AsSpan().StartsWith(Encoding.UTF8.Preamble))
                        output.Write(Encoding.UTF8.Preamble);

                    retainedStart = lineEnd;
                }

                firstLine = false;
                lineStart = lineEnd;
            }

            if (output == null)
                return carrier;

            output.Write(carrier, retainedStart, carrier.Length - retainedStart);
            byte[] result = output.ToArray();
            output.Dispose();
            return result;
        }

        private static bool isOnlineIdLine(ReadOnlySpan<byte> line)
        {
            int separator = line.IndexOf((byte)':');
            if (separator < 0)
                return false;

            string key;

            try
            {
                // LegacyDecoder.SplitKeyVal() uses TrimEntries, which applies Unicode whitespace
                // trimming to the key. Decode only the key so arbitrary value bytes cannot conceal
                // an otherwise valid collision-bearing field.
                key = new UTF8Encoding(false, true).GetString(line[..separator]).Trim();
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            return string.Equals(key, "BeatmapID", StringComparison.Ordinal)
                   || string.Equals(key, "BeatmapSetID", StringComparison.Ordinal);
        }

        private static bool isLegacyBeatmapFile(string filename)
        {
            int separator = Math.Max(filename.LastIndexOf('/'), filename.LastIndexOf('\\'));
            return filename.AsSpan(separator + 1).EndsWith(".osu", StringComparison.OrdinalIgnoreCase);
        }

        private BeatmapInfo getSelected(BeatmapSetInfo set) =>
            set.Beatmaps.SingleOrDefault(beatmap => beatmap.ID == selectedBeatmapId)
            ?? throw new InvalidOperationException("The selected Sticks difficulty is no longer present in its beatmap set.");
    }
}

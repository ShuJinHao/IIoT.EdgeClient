using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public interface IPlcSignalBlockPlanner
{
    IReadOnlyList<PlcSignalBlock> Plan(
        IReadOnlyCollection<PlcIoScanMapping> mappings,
        int maxBlockWordCount,
        PlcIoWriteGapPolicy writeGapPolicy,
        bool isWrite);
}

public sealed class DefaultPlcSignalBlockPlanner : IPlcSignalBlockPlanner
{
    public IReadOnlyList<PlcSignalBlock> Plan(
        IReadOnlyCollection<PlcIoScanMapping> mappings,
        int maxBlockWordCount,
        PlcIoWriteGapPolicy writeGapPolicy,
        bool isWrite)
    {
        if (mappings.Count == 0)
        {
            return [];
        }

        var maxWords = maxBlockWordCount <= 0 ? 100 : maxBlockWordCount;
        var groups = mappings
            .Select(static mapping => new PlannedMapping(mapping, PlcAddressRange.TryParse(
                mapping.PlcAddress,
                mapping.AddressCount)))
            .GroupBy(static x => x.Range?.Prefix ?? x.Mapping.PlcAddress, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase);

        var blocks = new List<PlcSignalBlock>();
        foreach (var group in groups)
        {
            var parsed = group.Where(static x => x.Range is not null)
                .OrderBy(static x => x.Range!.Number)
                .ThenBy(static x => x.Mapping.SortOrder)
                .ToArray();
            var unparsed = group.Where(static x => x.Range is null).ToArray();

            BuildParsedBlocks(parsed, maxWords, writeGapPolicy, isWrite, blocks);
            foreach (var item in unparsed)
            {
                blocks.Add(new PlcSignalBlock(
                    item.Mapping.PlcAddress,
                    item.Mapping.AddressCount,
                    [new PlcSignalBlockItem(item.Mapping, 0)]));
            }
        }

        return blocks;
    }

    private static void BuildParsedBlocks(
        IReadOnlyList<PlannedMapping> mappings,
        int maxWords,
        PlcIoWriteGapPolicy writeGapPolicy,
        bool isWrite,
        ICollection<PlcSignalBlock> blocks)
    {
        var current = new List<PlannedMapping>();
        PlcAddressRange? currentStart = null;
        var currentEndExclusive = 0;

        foreach (var item in mappings)
        {
            var range = item.Range!;
            if (current.Count == 0)
            {
                current.Add(item);
                currentStart = range;
                currentEndExclusive = range.EndExclusive;
                continue;
            }

            var hasGap = range.Number > currentEndExclusive;
            var nextEndExclusive = Math.Max(currentEndExclusive, range.EndExclusive);
            var nextWordCount = currentStart!.ToWordCount(nextEndExclusive);
            var shouldSplitForGap = isWrite && writeGapPolicy == PlcIoWriteGapPolicy.Split && hasGap;

            if (shouldSplitForGap || nextWordCount > maxWords)
            {
                FlushCurrent(current, currentStart, currentEndExclusive, blocks);
                current.Clear();
                current.Add(item);
                currentStart = range;
                currentEndExclusive = range.EndExclusive;
                continue;
            }

            current.Add(item);
            currentEndExclusive = nextEndExclusive;
        }

        if (current.Count > 0 && currentStart is not null)
        {
            FlushCurrent(current, currentStart, currentEndExclusive, blocks);
        }
    }

    private static void FlushCurrent(
        IReadOnlyList<PlannedMapping> mappings,
        PlcAddressRange start,
        int endExclusive,
        ICollection<PlcSignalBlock> blocks)
    {
        var startAddress = start.FormatAddress();
        var totalCount = start.ToWordCount(endExclusive);
        var items = mappings
            .Select(mapping => new PlcSignalBlockItem(
                mapping.Mapping,
                start.ToWordOffset(mapping.Range!.Number)))
            .ToArray();

        blocks.Add(new PlcSignalBlock(startAddress, totalCount, items));
    }

    private sealed record PlannedMapping(PlcIoScanMapping Mapping, PlcAddressRange? Range);

    private sealed record PlcAddressRange(string Prefix, int Number, int AddressStep, int WordCount)
    {
        public int EndExclusive => Number + (WordCount * AddressStep);

        public string FormatAddress() => $"{Prefix}{Number}";

        public int ToWordOffset(int addressNumber) => (addressNumber - Number) / AddressStep;

        public int ToWordCount(int endExclusive)
            => Math.Max(1, (endExclusive - Number + AddressStep - 1) / AddressStep);

        public static PlcAddressRange? TryParse(string address, int wordCount)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            var trimmed = address.Trim();
            var index = trimmed.Length - 1;
            while (index >= 0 && char.IsDigit(trimmed[index]))
            {
                index--;
            }

            if (index == trimmed.Length - 1)
            {
                return null;
            }

            var prefix = trimmed[..(index + 1)];
            if (!int.TryParse(trimmed[(index + 1)..], out var number))
            {
                return null;
            }

            return new PlcAddressRange(prefix, number, ResolveAddressStep(prefix), Math.Max(1, wordCount));
        }

        private static int ResolveAddressStep(string prefix)
        {
            if (prefix.Contains("DBW", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (prefix.Contains("DBD", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            return 1;
        }
    }
}

public sealed record PlcSignalBlock(
    string StartAddress,
    int WordCount,
    IReadOnlyList<PlcSignalBlockItem> Items);

public sealed record PlcSignalBlockItem(
    PlcIoScanMapping Mapping,
    int Offset);

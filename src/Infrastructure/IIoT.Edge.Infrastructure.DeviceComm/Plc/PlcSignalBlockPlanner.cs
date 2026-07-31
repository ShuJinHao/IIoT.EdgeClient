using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
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
        var activeMappings = mappings
            .Where(static mapping => !string.IsNullOrWhiteSpace(mapping.PlcAddress))
            .ToArray();

        if (activeMappings.Length == 0)
        {
            return [];
        }

        var maxWords = maxBlockWordCount <= 0
            ? 100
            : Math.Min(100, maxBlockWordCount);
        var groups = activeMappings
            .SelectMany(mapping => ExpandMapping(mapping, maxWords))
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
                    [new PlcSignalBlockItem(
                        item.Mapping,
                        0,
                        item.MappingWordOffset,
                        item.SegmentWordCount)]));
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
            var shouldSplitForGap = hasGap
                                    && isWrite
                                    && writeGapPolicy == PlcIoWriteGapPolicy.Split;

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
                start.ToWordOffset(mapping.Range!.Number),
                mapping.MappingWordOffset,
                mapping.SegmentWordCount))
            .ToArray();

        blocks.Add(new PlcSignalBlock(startAddress, totalCount, items));
    }

    private static IReadOnlyList<PlannedMapping> ExpandMapping(
        PlcIoScanMapping mapping,
        int maxWords)
    {
        var range = PlcAddressRange.TryParse(mapping.PlcAddress, mapping.AddressCount);
        if (range is null)
        {
            if (mapping.AddressCount > maxWords)
            {
                throw new InvalidOperationException(
                    $"PLC 地址“{mapping.PlcAddress}”无法解析且长度超过 {maxWords} words，拒绝生成不安全读取块。");
            }

            return [new PlannedMapping(mapping, null, 0, mapping.AddressCount)];
        }

        if (mapping.AddressCount <= maxWords)
        {
            return [new PlannedMapping(mapping, range, 0, mapping.AddressCount)];
        }

        var segments = new List<PlannedMapping>();
        for (var mappingOffset = 0; mappingOffset < mapping.AddressCount; mappingOffset += maxWords)
        {
            var segmentWordCount = Math.Min(maxWords, mapping.AddressCount - mappingOffset);
            segments.Add(new PlannedMapping(
                mapping,
                range.Slice(mappingOffset, segmentWordCount),
                mappingOffset,
                segmentWordCount));
        }

        return segments;
    }

    private sealed record PlannedMapping(
        PlcIoScanMapping Mapping,
        PlcAddressRange? Range,
        int MappingWordOffset,
        int SegmentWordCount);

    private sealed record PlcAddressRange(string Prefix, int Number, int AddressStep, int WordCount)
    {
        public int EndExclusive => Number + (WordCount * AddressStep);

        public string FormatAddress() => $"{Prefix}{Number}";

        public int ToWordOffset(int addressNumber) => (addressNumber - Number) / AddressStep;

        public int ToWordCount(int endExclusive)
            => Math.Max(1, (endExclusive - Number + AddressStep - 1) / AddressStep);

        public PlcAddressRange Slice(int wordOffset, int wordCount)
            => new(
                Prefix,
                Number + (wordOffset * AddressStep),
                AddressStep,
                wordCount);

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
    int Offset,
    int MappingWordOffset = 0,
    int? SegmentWordCount = null)
{
    public int EffectiveWordCount
        => SegmentWordCount ?? Mapping.AddressCount;
}

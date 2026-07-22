using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public interface IIoViewSignalValueUpdater
{
    void Refresh(
        IEnumerable<IoInteractionRowModel> interactionRows,
        IEnumerable<IoDataSectionModel> dataSections,
        IEnumerable<IoContinuousReadMatrixSectionModel> arraySections,
        IPlcBuffer buffer);
}

internal sealed class IoViewSignalValueUpdater : IIoViewSignalValueUpdater
{
    public void Refresh(
        IEnumerable<IoInteractionRowModel> interactionRows,
        IEnumerable<IoDataSectionModel> dataSections,
        IEnumerable<IoContinuousReadMatrixSectionModel> arraySections,
        IPlcBuffer buffer)
    {
        foreach (var row in interactionRows)
        {
            foreach (var signal in row.PlcSignals)
            {
                UpdateReadSignal(signal, buffer);
            }

            foreach (var signal in row.HostSignals)
            {
                UpdateWriteSignal(signal, buffer);
            }

            row.InitializeWriteValueFromCurrentBuffer();
            row.NotifyValuesChanged();
        }

        foreach (var signal in dataSections.SelectMany(static section => section.Signals))
        {
            UpdateSignal(signal, buffer);
        }

        foreach (var section in arraySections)
        {
            foreach (var signal in section.Columns)
            {
                UpdateSignal(signal, buffer);
            }

            section.RefreshRows();
        }
    }

    private static void UpdateSignal(IoSignalModel signal, IPlcBuffer buffer)
    {
        if (string.Equals(signal.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase))
        {
            UpdateWriteSignal(signal, buffer);
            return;
        }

        UpdateReadSignal(signal, buffer);
    }

    private static void UpdateReadSignal(IoSignalModel signal, IPlcBuffer buffer)
    {
        var words = buffer.TryGetReadWords(signal.SignalKey, out var signalWords)
            ? EnsureLength(signalWords, signal.AddressCount)
            : ReadWords(signal, index => buffer.GetReadValue(index));
        ApplyDecodedValue(signal, words);
    }

    private static void UpdateWriteSignal(IoSignalModel signal, IPlcBuffer buffer)
    {
        var words = buffer.TryGetWriteWords(signal.SignalKey, out var signalWords)
            ? EnsureLength(signalWords, signal.AddressCount)
            : ReadWords(signal, index => buffer.GetWriteBufferValue(index));
        ApplyDecodedValue(signal, words);
    }

    private static ushort[] ReadWords(IoSignalModel signal, Func<int, ushort> read)
    {
        var words = new ushort[Math.Max(1, signal.AddressCount)];
        for (var offset = 0; offset < words.Length; offset++)
        {
            words[offset] = read(signal.StartIndex + offset);
        }

        return words;
    }

    private static ushort[] EnsureLength(IReadOnlyList<ushort> source, int addressCount)
    {
        var length = Math.Max(1, addressCount);
        if (source.Count == length && source is ushort[] array)
        {
            return array;
        }

        var words = new ushort[length];
        for (var index = 0; index < words.Length && index < source.Count; index++)
        {
            words[index] = source[index];
        }

        return words;
    }

    private static void ApplyDecodedValue(IoSignalModel signal, IReadOnlyList<ushort> words)
    {
        var values = DecodeWords(signal.DataType, words);
        var display = values.Count == 0 ? string.Empty : string.Join(", ", values);
        var preview = values.Count <= 8
            ? display
            : $"{string.Join(", ", values.Take(8))} ...";

        signal.DisplayValue = string.IsNullOrWhiteSpace(display) ? "-" : display;
        signal.PreviewValue = string.IsNullOrWhiteSpace(preview) ? "-" : preview;
        signal.Value = DecodeSingleEditValue(signal.DataType, words);
        var hadExpandedValues = signal.ExpandedValues.Count > 0;
        if (signal.IsContinuous
            && values.Count > 0
            && !string.Equals(signal.DataType, "Ascii", StringComparison.OrdinalIgnoreCase))
        {
            SyncExpandedValues(signal.ExpandedValues, values);
        }
        else if (signal.ExpandedValues.Count > 0)
        {
            signal.ExpandedValues.Clear();
        }

        if (hadExpandedValues != signal.ExpandedValues.Count > 0)
        {
            signal.OnPropertyChanged(nameof(IoSignalModel.HasExpandedValues));
        }
    }

    private static int DecodeSingleEditValue(string dataType, IReadOnlyList<ushort> words)
    {
        if (words.Count == 0)
        {
            return 0;
        }

        var normalizedType = (dataType ?? string.Empty).Trim();
        if (string.Equals(normalizedType, "Int16", StringComparison.OrdinalIgnoreCase))
        {
            return unchecked((short)words[0]);
        }

        if (IsInt32Type(normalizedType) && words.Count >= 2)
        {
            return CombineToInt32(words[1], words[0]);
        }

        if (IsUInt32Type(normalizedType) && words.Count >= 2)
        {
            return unchecked((int)CombineToUInt32(words[1], words[0]));
        }

        return words[0];
    }

    private static IReadOnlyList<string> DecodeWords(string dataType, IReadOnlyList<ushort> words)
    {
        var normalizedType = (dataType ?? string.Empty).Trim();
        if (string.Equals(normalizedType, "Ascii", StringComparison.OrdinalIgnoreCase))
        {
            return [DecodeAscii(words)];
        }

        if (string.Equals(normalizedType, "Float", StringComparison.OrdinalIgnoreCase))
        {
            var values = new List<string>();
            for (var index = 0; index + 1 < words.Count; index += 2)
            {
                values.Add(CombineToFloat(words[index + 1], words[index]).ToString("0.###", CultureInfo.InvariantCulture));
            }

            return values;
        }

        if (string.Equals(normalizedType, "Bool", StringComparison.OrdinalIgnoreCase))
        {
            return words.Select(static word => word == 0 ? "False" : "True").ToArray();
        }

        if (string.Equals(normalizedType, "Int16", StringComparison.OrdinalIgnoreCase))
        {
            return words.Select(static word => unchecked((short)word).ToString(CultureInfo.InvariantCulture)).ToArray();
        }

        if (IsInt32Type(normalizedType))
        {
            var values = new List<string>();
            for (var index = 0; index + 1 < words.Count; index += 2)
            {
                values.Add(CombineToInt32(words[index + 1], words[index]).ToString(CultureInfo.InvariantCulture));
            }

            return values;
        }

        if (IsUInt32Type(normalizedType))
        {
            var values = new List<string>();
            for (var index = 0; index + 1 < words.Count; index += 2)
            {
                values.Add(CombineToUInt32(words[index + 1], words[index]).ToString(CultureInfo.InvariantCulture));
            }

            return values;
        }

        return words.Select(static word => word.ToString(CultureInfo.InvariantCulture)).ToArray();
    }

    private static bool IsInt32Type(string dataType)
        => string.Equals(dataType, "Int32", StringComparison.OrdinalIgnoreCase);

    private static bool IsUInt32Type(string dataType)
        => string.Equals(dataType, "UInt32", StringComparison.OrdinalIgnoreCase)
            || string.Equals(dataType, "DWord", StringComparison.OrdinalIgnoreCase);

    private static int CombineToInt32(ushort high, ushort low)
        => unchecked((int)CombineToUInt32(high, low));

    private static uint CombineToUInt32(ushort high, ushort low)
        => ((uint)high << 16) | low;

    private static void SyncExpandedValues(
        ObservableCollection<IoSignalValueModel> target,
        IReadOnlyList<string> values)
    {
        while (target.Count > values.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = 0; index < values.Count; index++)
        {
            var nextValue = values[index];
            var nextIndex = index + 1;
            if (index >= target.Count)
            {
                target.Add(new IoSignalValueModel
                {
                    Index = nextIndex,
                    Value = nextValue
                });
                continue;
            }

            var current = target[index];
            if (current.Index != nextIndex || current.Value != nextValue)
            {
                target[index] = new IoSignalValueModel
                {
                    Index = nextIndex,
                    Value = nextValue
                };
            }
        }
    }

    private static string DecodeAscii(IReadOnlyList<ushort> words)
    {
        var builder = new StringBuilder(words.Count * 2);
        foreach (var word in words)
        {
            var low = (byte)(word & 0xFF);
            var high = (byte)(word >> 8);

            if (low != 0)
            {
                builder.Append((char)low);
            }

            if (high != 0)
            {
                builder.Append((char)high);
            }
        }

        return builder.ToString().Trim();
    }

    private static float CombineToFloat(ushort high, ushort low)
    {
        byte[] bytes =
        [
            (byte)(high >> 8),
            (byte)high,
            (byte)(low >> 8),
            (byte)low
        ];

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToSingle(bytes, 0);
    }
}

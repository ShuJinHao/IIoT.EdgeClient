using IIoT.Edge.Application.Abstractions.Plc.Store;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

internal static class IoViewPlcSnapshotApplier
{
    public static void Apply(
        IPlcBuffer buffer,
        IEnumerable<IoInteractionRowModel> interactionRows,
        IEnumerable<IoDataSectionModel> dataSections,
        IEnumerable<IoContinuousReadMatrixSectionModel> arraySections)
    {
        foreach (var row in interactionRows)
        {
            foreach (var signal in row.PlcSignals)
            {
                ApplySignalSnapshot(buffer, signal, readDirection: true);
            }

            foreach (var signal in row.HostSignals)
            {
                ApplySignalSnapshot(buffer, signal, readDirection: false);
            }

            row.InitializeWriteValueFromCurrentBuffer();
            row.NotifyValuesChanged();
        }

        foreach (var signal in dataSections.SelectMany(static section => section.Signals))
        {
            ApplySignalSnapshot(buffer, signal, readDirection: true);
        }

        foreach (var section in arraySections)
        {
            foreach (var column in section.Columns)
            {
                ApplySignalSnapshot(buffer, column, readDirection: true);
            }

            section.RebuildRows();
        }
    }

    private static void ApplySignalSnapshot(IPlcBuffer buffer, IoSignalModel signal, bool readDirection)
    {
        ushort[] words;
        var found = readDirection
            ? buffer.TryGetReadWords(signal.SignalKey, out words)
            : buffer.TryGetWriteWords(signal.SignalKey, out words);

        if (!found)
        {
            signal.DisplayValue = "-";
            signal.PreviewValue = "-";
            return;
        }

        if (signal.AddressCount > 1)
        {
            signal.ExpandedValues.Clear();
            for (var index = 0; index < Math.Min(signal.AddressCount, words.Length); index++)
            {
                signal.ExpandedValues.Add(new IoSignalValueModel(index + 1, words[index].ToString()));
            }
        }

        signal.SetValue(words.Length == 0 ? 0 : words[0]);
        if (words.Length > 1 && signal.AddressCount <= 1)
        {
            signal.DisplayValue = string.Join(",", words);
            signal.PreviewValue = signal.DisplayValue;
        }
    }
}

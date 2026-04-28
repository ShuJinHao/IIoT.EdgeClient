namespace IIoT.Edge.Application.Abstractions.Plc.Signals;

public interface ILogicalSignalAccessor
{
    bool CanRead(string label);

    bool CanWrite(string label);

    bool TryRead(string label, out ushort value);

    ushort Read(string label);

    void Write(string label, ushort value);
}

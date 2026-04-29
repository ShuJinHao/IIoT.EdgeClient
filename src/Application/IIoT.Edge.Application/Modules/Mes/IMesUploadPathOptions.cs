namespace IIoT.Edge.Application.Modules.Mes;

public interface IMesUploadPathOptions
{
    string Inbound { get; }

    string Outbound { get; }

    string Recipe { get; }

    string Realtime { get; }

    string EquipmentStatus { get; }
}

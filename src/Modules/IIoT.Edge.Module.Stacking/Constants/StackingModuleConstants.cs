namespace IIoT.Edge.Module.Stacking.Constants;

public static class StackingModuleConstants
{
    public const string ModuleId = "Stacking";
    public const string ProcessType = "Stacking";

    public const string RuntimeTaskName = "Stacking.SignalCapture";

    public const string RuntimeRegisteredKey = "Stacking.RuntimeTaskRegistered";
    public const string LastObservedSequenceKey = "Stacking.LastObservedSequence";
    public const string LastObservedLayerCountKey = "Stacking.LastObservedLayerCount";
    public const string LastObservedResultCodeKey = "Stacking.LastObservedResultCode";
    public const string LastObservedAtKey = "Stacking.LastObservedAt";
    public const string LastPublishedSequenceKey = "Stacking.LastPublishedSequence";
    public const string LastPublishedBarcodeKey = "Stacking.LastPublishedBarcode";

    public const string CloudUploadEnabledKey = "Stacking.CloudUploadEnabled";
    public const string LastCloudUploadStatusKey = "Stacking.LastCloudUploadStatus";
    public const string LastCloudUploadAtKey = "Stacking.LastCloudUploadAt";
    public const string LastCloudUploadErrorKey = "Stacking.LastCloudUploadError";

    public const string CloudUploadSuccessStatus = "Success";
    public const string CloudUploadFailedStatus = "Failed";
    public const string CloudUploadDisabledStatus = "Disabled";
}

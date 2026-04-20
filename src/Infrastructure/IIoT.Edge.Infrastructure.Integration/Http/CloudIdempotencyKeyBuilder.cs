using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Security.Cryptography;
using System.Text;

namespace IIoT.Edge.Infrastructure.Integration.Http;

public static class CloudIdempotencyKeyBuilder
{
    public static string ForRecord(
        string processType,
        string uploaderName,
        CellCompletedRecord record)
        => ComputeHash($"{processType}|{uploaderName}|{CellDataJsonSerializer.Serialize(record.CellData)}");

    public static string ForBatch(
        string processType,
        string uploaderName,
        IReadOnlyList<CellCompletedRecord> records)
    {
        if (records.Count == 0)
        {
            return ComputeHash($"{processType}|{uploaderName}|empty");
        }

        var itemHashes = records
            .Select(record => ForRecord(processType, uploaderName, record));

        return ComputeHash($"{processType}|{uploaderName}|{string.Join("|", itemHashes)}");
    }

    public static string ForPayload(
        string processType,
        string uploaderName,
        string canonicalPayloadJson)
        => ComputeHash($"{processType}|{uploaderName}|{canonicalPayloadJson}");

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

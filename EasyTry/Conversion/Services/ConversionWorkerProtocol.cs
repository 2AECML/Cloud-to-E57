using System.Text.Json;
using System.Text.Json.Serialization;
using EasyTry.Conversion.Models;

namespace EasyTry.Conversion.Services;

internal static class ConversionWorkerProtocol
{
    internal const string Argument = "--conversion-worker";
    internal const string Prefix = "EASYTRY_WORKER_V1:";
    internal static readonly JsonSerializerOptions Json = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    internal static string Encode(WorkerMessage message) =>
        Prefix + JsonSerializer.Serialize(message, Json);
}

internal sealed record WorkerMessage(
    string Kind,
    ConversionProgress? Progress = null,
    ConversionReport? Report = null,
    string? Error = null);

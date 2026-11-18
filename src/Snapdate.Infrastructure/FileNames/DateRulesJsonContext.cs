using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapdate.Infrastructure.FileNames;

/// <summary>Source-generated (trim/AOT-safe) serialisation of the rule files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(SystemDateRulesFile))]
[JsonSerializable(typeof(UserDateRulesFile))]
internal sealed partial class DateRulesJsonContext : JsonSerializerContext;

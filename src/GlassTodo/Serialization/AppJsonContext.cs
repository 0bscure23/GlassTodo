using System.Text.Json.Serialization;
using GlassTodo.Models;

namespace GlassTodo.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppData))]
[JsonSerializable(typeof(AppSettings))]
public partial class AppJsonContext : JsonSerializerContext
{
}

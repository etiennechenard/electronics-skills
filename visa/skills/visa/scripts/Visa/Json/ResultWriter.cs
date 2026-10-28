using System.Text;
using System.Text.Json;

namespace Visa.Json;

public static class ResultWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    public static void WriteSuccess(IDictionary<string, object?> payload)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", true);
            WritePayloadFields(writer, payload);
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    public static void WriteError(
        string errorClass,
        string operation,
        string description,
        IDictionary<string, object?>? context = null,
        int? statusInt = null)
    {
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", false);
            writer.WriteString("error", errorClass);
            writer.WriteString("operation", operation);
            if (statusInt.HasValue)
            {
                var hex = $"0x{(uint)statusInt.Value:X8}";
                writer.WriteString("status", hex);
                writer.WriteNumber("status_int", statusInt.Value);
            }
            writer.WriteString("description", description);
            if (context != null)
                WritePayloadFields(writer, context);
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static void WritePayloadFields(Utf8JsonWriter writer, IDictionary<string, object?> fields)
    {
        foreach (var (key, value) in fields)
        {
            switch (value)
            {
                case null:
                    writer.WriteNull(key);
                    break;
                case bool b:
                    writer.WriteBoolean(key, b);
                    break;
                case int i:
                    writer.WriteNumber(key, i);
                    break;
                case uint u:
                    writer.WriteNumber(key, u);
                    break;
                case long l:
                    writer.WriteNumber(key, l);
                    break;
                case double d:
                    writer.WriteNumber(key, d);
                    break;
                case string s:
                    writer.WriteString(key, s);
                    break;
                case IEnumerable<string> arr:
                    writer.WriteStartArray(key);
                    foreach (var item in arr)
                        writer.WriteStringValue(item);
                    writer.WriteEndArray();
                    break;
                case IDictionary<string, object?> nested:
                    writer.WriteStartObject(key);
                    WritePayloadFields(writer, nested);
                    writer.WriteEndObject();
                    break;
                case IEnumerable<IDictionary<string, object?>> objArr:
                    writer.WriteStartArray(key);
                    foreach (var obj in objArr)
                    {
                        writer.WriteStartObject();
                        WritePayloadFields(writer, obj);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    writer.WriteString(key, value.ToString());
                    break;
            }
        }
    }
}

using System.Reflection;

namespace RdtClient.Service.Services.Usenet.Extensions;

public static class UsenetObjectExtensions
{
    private const BindingFlags BindingAttr = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static Object? GetReflectionProperty(this Object obj, String propertyName)
    {
        var type = obj.GetType();
        var prop = type.GetProperty(propertyName, BindingAttr);
        return prop?.GetValue(obj);
    }

    public static Object? GetReflectionField(this Object obj, String fieldName)
    {
        var type = obj.GetType();
        var prop = type.GetField(fieldName, BindingAttr);
        return prop?.GetValue(obj);
    }

    public static String ToIndentedJson(this Object obj)
    {
        return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}

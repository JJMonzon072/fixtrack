using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace FixTrack.Web.Services;

/// <summary>Mensaje breve que se muestra una sola vez después de redirigir (patrón Post/Redirect/Get).</summary>
public static class StatusMessage
{
    private const string TextKey = "StatusMessage.Text";
    private const string KindKey = "StatusMessage.Kind";

    public static void Success(ITempDataDictionary tempData, string text) => Set(tempData, text, "success");

    public static void Warning(ITempDataDictionary tempData, string text) => Set(tempData, text, "warning");

    public static (string Text, string Kind)? Read(ITempDataDictionary tempData) =>
        tempData[TextKey] is string text ? (text, tempData[KindKey] as string ?? "success") : null;

    private static void Set(ITempDataDictionary tempData, string text, string kind)
    {
        tempData[TextKey] = text;
        tempData[KindKey] = kind;
    }
}

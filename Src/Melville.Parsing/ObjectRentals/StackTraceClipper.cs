using System.Diagnostics;
using System.Text;

namespace Melville.Parsing.ObjectRentals;

internal static class StackTraceClipper
{
    public static string Clip(this StackTrace trace)
    {
        var frames = trace.GetFrames().Select(PrintFrame).ToArray();
        var last = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i].Contains("Melville")) last = i;
        }

        var ret = new StringBuilder();
        for (int i = 0; i <= last; i++)
        {
            ret.AppendLine(frames[i]);
        }

        return ret.ToString();
    }

    private static string PrintFrame(StackFrame i)
    {
        // DiagnosticMethodInfo, not GetMethod(): it survives trimming (GetMethod needs reflection metadata).
        var method = DiagnosticMethodInfo.Create(i);
        if (method is null) return "No Method";
        return $"{method.DeclaringTypeName}.{method.Name}";
    }
}
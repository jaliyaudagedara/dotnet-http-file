using DotNetHttpFile.Parsing;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Resolution;

internal static class TemplateLines
{
    private const string ExpressionStart = "{{";
    private const string ExpressionEnd = "}}";

    /// <summary>
    /// Gets the line of the segment at <paramref name="segmentIndex"/> in a template whose text starts on
    /// <paramref name="firstLine"/>. Only a body spans lines, so for any other template this is the first line.
    /// </summary>
    public static int LineOf(Template template, int segmentIndex, int firstLine)
    {
        ArgumentNullException.ThrowIfNull(template);

        string raw = template.Raw;
        int offset = 0;
        for (int index = 0; index < segmentIndex; index++)
        {
            if (template.Segments[index] is LiteralSegment literal)
            {
                offset += literal.Text.Length;
                continue;
            }

            // Segments do not keep their offsets. An expression starts where the text before it ends and
            // closes at the next "}}", which is how Template.Parse delimits it.
            int end = offset + ExpressionStart.Length <= raw.Length
                ? raw.IndexOf(ExpressionEnd, offset + ExpressionStart.Length, StringComparison.Ordinal)
                : -1;
            if (end < 0)
            {
                return firstLine;
            }

            offset = end + ExpressionEnd.Length;
        }

        return firstLine + HttpFileParser.CountLineTerminators(raw.AsSpan(0, Math.Min(offset, raw.Length)));
    }
}

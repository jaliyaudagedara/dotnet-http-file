using DotNetHttpFile.Parsing;
using DotNetHttpFile.Templates;

namespace DotNetHttpFile.Resolution;

internal static class TemplateLines
{
    /// <summary>
    /// Gets the line of the segment at <paramref name="segmentIndex"/> in a template whose text starts on
    /// <paramref name="firstLine"/>. Only a body spans lines, so for any other template this is the first line.
    /// </summary>
    public static int LineOf(Template template, int segmentIndex, int firstLine)
    {
        ArgumentNullException.ThrowIfNull(template);

        int offset = template.SegmentOffsets[segmentIndex];

        return firstLine + HttpFileParser.CountLineTerminators(template.Raw.AsSpan(0, offset));
    }
}

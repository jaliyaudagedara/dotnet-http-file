namespace DotNetHttpFile.Templates;

internal enum ReferenceSource
{
    Request,
    Response,
}

internal enum ReferencePart
{
    Body,
    Headers,
}

internal abstract record TemplateSegment;

internal sealed record LiteralSegment(string Text) : TemplateSegment;

internal sealed record VariableSegment(string Name) : TemplateSegment;

internal sealed record DynamicSegment(string Function, IReadOnlyList<string> Arguments) : TemplateSegment;

internal sealed record RequestReferenceSegment(
    string RequestName,
    ReferenceSource Source,
    ReferencePart Part,
    string Selector,
    string Expression) : TemplateSegment;

using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Resolution;

/// <summary>
/// The provider-resolved values of a run. What the tool prints about the request it built, and its warnings and
/// errors, show them as <see cref="Placeholder"/>. The response is shown as the server sent it.
/// </summary>
internal sealed class SecretMask
{
    public const string Placeholder = "***";

    private const string AttributeName = "a";
    private const string AttributeOpening = "=\"";
    private const string UriPathPrefix = "http://h/";
    private const string UriQueryPrefix = "http://h/?";

    // Every text that is replaced: the secrets and the forms derived from them.
    private readonly HashSet<string> _forms = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers <paramref name="secret"/> together with the forms it takes when it is written into a URL, a
    /// JSON string, XML text or an XML attribute. An empty value is ignored.
    /// </summary>
    /// <remarks>
    /// The JSON form is the one this tool's own writers produce, as when a request reference copies a JSON value
    /// that holds a secret into a header or the URL. The URI forms catch a secret in a redirect location resolved
    /// against the request URL. The other escaped forms catch a value that a reference copies from a response
    /// body or URL that spelled it that way.
    /// </remarks>
    public void Add(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (secret.Length > 0)
        {
            AddWithEscapedForms(secret);
        }
    }

    /// <summary>Replaces every registered secret in <paramref name="text"/> with <see cref="Placeholder"/>.</summary>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Replace(text);
    }

    private static string JsonStringForm(string value) =>
        JsonEncodedText.Encode(value, BodyText.IndentedJsonOptions.Encoder).ToString();

    private static string XmlAttributeForm(string value)
    {
        string attribute = new XAttribute(AttributeName, value).ToString();

        // The text is name="value"; the value is what lies between the quotes.
        return attribute[(AttributeName.Length + AttributeOpening.Length)..^1];
    }

    private string Replace(string text)
    {
        List<(int Start, int End)> matches = FindMatches(text);
        if (matches.Count == 0)
        {
            return text;
        }

        // Matches that overlap are hidden as one run, so the tail of one secret never shows next to the
        // placeholder of another. Sorting the longest first at each position makes a secret win over its prefix.
        matches.Sort((left, right) =>
            left.Start != right.Start ? left.Start.CompareTo(right.Start) : right.End.CompareTo(left.End));

        StringBuilder builder = new(text.Length);
        int position = 0;
        int index = 0;
        while (index < matches.Count)
        {
            (int start, int end) = matches[index++];
            while (index < matches.Count && matches[index].Start < end)
            {
                end = Math.Max(end, matches[index++].End);
            }

            builder.Append(text, position, start - position).Append(Placeholder);
            position = end;
        }

        return builder.Append(text, position, text.Length - position).ToString();
    }

    private List<(int Start, int End)> FindMatches(string text)
    {
        List<(int Start, int End)> matches = [];
        foreach (string form in _forms)
        {
            int start = text.IndexOf(form, StringComparison.Ordinal);
            while (start >= 0)
            {
                matches.Add((start, start + form.Length));
                start = text.IndexOf(form, start + 1, StringComparison.Ordinal);
            }
        }

        return matches;
    }

    private void AddWithEscapedForms(string value)
    {
        _forms.Add(value);

        // A reference can copy a value that a response body or URL spelled percent-encoded.
        _forms.Add(Uri.EscapeDataString(value));

        // The request itself goes out in System.Uri's escaping, which differs: a space becomes %20 while
        // '@', '/' and '+' stay as they are. A redirect location resolved against the request URL holds the
        // secret in that form.
        AddUriForm(value, UriQueryPrefix, static uri => uri.Query);
        AddUriForm(value, UriPathPrefix, static uri => uri.AbsolutePath);

        AddEscapedForm(value, JsonStringForm);
        AddEscapedForm(value, static text => new XText(text).ToString());
        AddEscapedForm(value, XmlAttributeForm);
    }

    // A value that JSON or XML cannot hold, such as a lone surrogate or most control characters, has no such
    // form, so only its raw form is registered.
    private void AddEscapedForm(string value, Func<string, string> escape)
    {
        try
        {
            _forms.Add(escape(value));
        }
        catch (ArgumentException)
        {
        }
    }

    private void AddUriForm(string value, string prefix, Func<Uri, string> component)
    {
        if (!Uri.TryCreate(prefix + value, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        // The component starts with the '/' or '?' of the prefix.
        string form = component(uri)[1..];

        // Uri cuts the value at '#' or '?', folds dot segments and decodes some escapes. A form that no longer
        // spells the whole value would mask text that is not the secret.
        if (form.Length > 0 && string.Equals(Uri.UnescapeDataString(form), value, StringComparison.Ordinal))
        {
            _forms.Add(form);
        }
    }
}

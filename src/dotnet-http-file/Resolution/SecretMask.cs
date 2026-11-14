using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotNetHttpFile.Execution;

namespace DotNetHttpFile.Resolution;

/// <summary>The provider-resolved values of a run, which output shows as <see cref="Placeholder"/>.</summary>
internal sealed class SecretMask
{
    public const string Placeholder = "***";

    private const string AttributeName = "a";
    private const string AttributeOpening = "=\"";
    private const string UriPathPrefix = "http://h/";
    private const string UriQueryPrefix = "http://h/?";

    // Strict, so that a form with a lone surrogate, which no UTF-8 text can hold, is left out and does not
    // become the bytes of the replacement character.
    private static readonly UTF8Encoding s_strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);

    // Every text that is replaced: the secrets and the forms derived from them. Rebuilt on each add, because
    // the forms of one secret depend on which other secrets are registered.
    private HashSet<string> _forms = new(StringComparer.Ordinal);

    // The UTF-8 bytes of each secret and of its escaped forms. The forms with an inner secret already
    // replaced are left out: they describe text this tool has masked, and bytes as received never are.
    private byte[][] _utf8SecretForms = [];

    /// <summary>
    /// Registers <paramref name="secret"/> together with the forms it takes when it is written into a URL, a
    /// JSON string, XML text or an XML attribute. An empty value is ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The JSON and XML forms are the ones this tool's own writers produce. A server may spell the same value
    /// with other escapes, which only parsing the body can undo; the body formatter does that.
    /// </para>
    /// <para>
    /// A secret that holds another registered secret is also registered as it reads once the inner one has
    /// been replaced. A body is masked before it is parsed and again after: when the first pass finds only the
    /// inner secret, because the body spells the outer one with escapes of its own, the second pass meets the
    /// outer secret in exactly that form.
    /// </para>
    /// </remarks>
    public void Add(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (secret.Length == 0 || !_secrets.Add(secret))
        {
            return;
        }

        _forms = BuildForms();
        _utf8SecretForms = BuildUtf8SecretForms();
    }

    /// <summary>Replaces every registered secret in <paramref name="text"/> with <see cref="Placeholder"/>.</summary>
    public string Apply(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Replace(text, _forms);
    }

    /// <summary>
    /// Whether <paramref name="utf8Text"/> holds a registered secret, as it is or in one of its escaped forms.
    /// The bytes are searched as they are, so a secret in another encoding is not found.
    /// </summary>
    public bool Contains(ReadOnlySpan<byte> utf8Text)
    {
        foreach (byte[] form in _utf8SecretForms)
        {
            if (utf8Text.IndexOf(form) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string Replace(string text, IReadOnlyCollection<string> forms)
    {
        List<(int Start, int End)> matches = FindMatches(text, forms);
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

    private static List<(int Start, int End)> FindMatches(string text, IReadOnlyCollection<string> forms)
    {
        List<(int Start, int End)> matches = [];
        foreach (string form in forms)
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

    private static void AddWithEscapedForms(HashSet<string> forms, string value)
    {
        forms.Add(value);

        // A secret substituted into a URL prints percent-encoded.
        forms.Add(Uri.EscapeDataString(value));

        // The request itself goes out in System.Uri's escaping, which differs: a space becomes %20 while
        // '@', '/' and '+' stay as they are. A server that echoes its own URL returns the secret in that form.
        AddUriForm(forms, value, UriQueryPrefix, static uri => uri.Query);
        AddUriForm(forms, value, UriPathPrefix, static uri => uri.AbsolutePath);

        AddEscapedForm(forms, value, JsonStringForm);
        AddEscapedForm(forms, value, static text => new XText(text).ToString());
        AddEscapedForm(forms, value, XmlAttributeForm);
    }

    // A value that JSON or XML cannot hold, such as a lone surrogate or most control characters, has no such
    // form, so only its raw form is registered.
    private static void AddEscapedForm(HashSet<string> forms, string value, Func<string, string> escape)
    {
        try
        {
            forms.Add(escape(value));
        }
        catch (ArgumentException)
        {
        }
    }

    private static void AddUriForm(HashSet<string> forms, string value, string prefix, Func<Uri, string> component)
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
            forms.Add(form);
        }
    }

    private static string JsonStringForm(string value) =>
        JsonEncodedText.Encode(value, BodyText.IndentedJsonOptions.Encoder).ToString();

    private static string XmlAttributeForm(string value)
    {
        string attribute = new XAttribute(AttributeName, value).ToString();

        // The text is name="value"; the value is what lies between the quotes.
        return attribute[(AttributeName.Length + AttributeOpening.Length)..^1];
    }

    private static void AddFormWithInnerSecretsMasked(
        HashSet<string> forms,
        string secret,
        IReadOnlyCollection<string> innerSecrets)
    {
        string masked = Replace(secret, innerSecrets);

        // Text made of placeholders and separators alone, such as ***:***, is not a secret; registering it
        // would only merge placeholders that are already on show.
        if (masked.Replace(Placeholder, string.Empty, StringComparison.Ordinal).Any(char.IsLetterOrDigit))
        {
            AddWithEscapedForms(forms, masked);
        }
    }

    private HashSet<string> BuildForms()
    {
        HashSet<string> forms = new(StringComparer.Ordinal);
        foreach (string secret in _secrets)
        {
            AddWithEscapedForms(forms, secret);

            string[] innerSecrets =
            [
                .. _secrets.Where(other =>
                    other.Length < secret.Length && secret.Contains(other, StringComparison.Ordinal)),
            ];

            // An earlier pass may have found any one of the inner secrets or all of them, depending on how the
            // text spelled each. Every other combination is left out: the count doubles with each inner secret.
            foreach (string innerSecret in innerSecrets)
            {
                AddFormWithInnerSecretsMasked(forms, secret, [innerSecret]);
            }

            if (innerSecrets.Length > 1)
            {
                AddFormWithInnerSecretsMasked(forms, secret, innerSecrets);
            }
        }

        return forms;
    }

    private byte[][] BuildUtf8SecretForms()
    {
        HashSet<string> forms = new(StringComparer.Ordinal);
        foreach (string secret in _secrets)
        {
            AddWithEscapedForms(forms, secret);
        }

        List<byte[]> utf8Forms = [];
        foreach (string form in forms)
        {
            try
            {
                utf8Forms.Add(s_strictUtf8.GetBytes(form));
            }
            catch (EncoderFallbackException)
            {
            }
        }

        return [.. utf8Forms];
    }
}

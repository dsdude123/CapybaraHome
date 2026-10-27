using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Text;

namespace CapybaraHome.Web.Reports;

/// <summary>
/// Sends a document to a printer over IPP. The request is written out byte by byte because the
/// operation needed is one Print-Job with a handful of attributes, which is smaller than any
/// library that would do it.
/// </summary>
public class IppPrinter(HttpClient client)
{
    /// <summary>Print-Job, the operation that takes a document and prints it.</summary>
    private const ushort PrintJob = 0x0002;

    private const byte OperationAttributes = 0x01;
    private const byte JobAttributes = 0x02;
    private const byte EndOfAttributes = 0x03;

    private const byte Charset = 0x47;
    private const byte NaturalLanguage = 0x48;
    private const byte Uri = 0x45;
    private const byte NameWithoutLanguage = 0x42;
    private const byte MimeMediaType = 0x49;
    private const byte Keyword = 0x44;

    /// <summary>Prints <paramref name="document"/> on the printer at <paramref name="host"/>.</summary>
    /// <param name="host">Host name the printer answers IPP on.</param>
    /// <param name="port">Port it answers on.</param>
    /// <param name="resource">Path its queue lives at.</param>
    /// <param name="document">The PDF to print.</param>
    /// <param name="jobName">What the job is called in the printer's queue.</param>
    /// <param name="bothSides">Whether the job asks for both sides of the paper.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="InvalidOperationException">No printer was named, or the printer refused the job.</exception>
    public async Task PrintAsync(
        string? host,
        int port,
        string resource,
        byte[] document,
        string jobName,
        bool bothSides = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("No printer is configured, so there is nothing to print to.");
        }

        var path = resource.StartsWith('/') ? resource : "/" + resource;
        var body = Request(host, port, path, document, jobName, bothSides);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/ipp");

        using var response = await client.PostAsync(
            new Uri($"http://{host}:{port}{path}"),
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The printer answered {(int)response.StatusCode} to the print job.");
        }

        var answer = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (answer.Length < 4)
        {
            throw new InvalidOperationException("The printer answered with no IPP status.");
        }

        var status = BinaryPrimitives.ReadUInt16BigEndian(answer.AsSpan(2, 2));
        if (status != 0x0000)
        {
            throw new InvalidOperationException($"The printer refused the job with IPP status 0x{status:x4}.");
        }
    }

    /// <summary>Builds the Print-Job request, document and all.</summary>
    private static byte[] Request(string host, int port, string resource, byte[] document, string jobName, bool bothSides)
    {
        var body = new List<byte>(document.Length + 256)
        {
            0x02, 0x00, // IPP 2.0
        };

        body.AddRange([(byte)(PrintJob >> 8), (byte)(PrintJob & 0xFF)]);
        body.AddRange([0x00, 0x00, 0x00, 0x01]); // request id

        body.Add(OperationAttributes);
        Attribute(body, Charset, "attributes-charset", "utf-8");
        Attribute(body, NaturalLanguage, "attributes-natural-language", "en");
        Attribute(body, Uri, "printer-uri", $"ipp://{host}:{port}{resource}");
        Attribute(body, NameWithoutLanguage, "job-name", jobName);
        Attribute(body, MimeMediaType, "document-format", "application/pdf");

        body.Add(JobAttributes);
        Attribute(body, Keyword, "sides", bothSides ? "two-sided-long-edge" : "one-sided");

        body.Add(EndOfAttributes);
        body.AddRange(document);

        return [.. body];
    }

    /// <summary>Writes one attribute: its tag, then its name and its value, each with its length.</summary>
    private static void Attribute(List<byte> body, byte tag, string name, string value)
    {
        body.Add(tag);
        Text(body, name);
        Text(body, value);
    }

    private static void Text(List<byte> body, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        body.AddRange([(byte)(bytes.Length >> 8), (byte)(bytes.Length & 0xFF)]);
        body.AddRange(bytes);
    }
}

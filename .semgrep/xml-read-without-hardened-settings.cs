using System.IO;
using System.Xml;
using System.Xml.Linq;

class Fixture
{
    // ruleid: xml-read-without-hardened-settings
    XDocument Bad(Stream s) => XDocument.Load(s);

    // ruleid: xml-read-without-hardened-settings
    XDocument BadParse(string text) => XDocument.Parse(text);

    void BadDoc(string text)
    {
        var doc = new XmlDocument();
        // ruleid: xml-read-without-hardened-settings
        doc.LoadXml(text);
    }

    // The committed policy: a reader with DTD prohibited and no resolver, then the two-arg Load.
    // ok: xml-read-without-hardened-settings
    XDocument Good(Stream s)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(s, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
}

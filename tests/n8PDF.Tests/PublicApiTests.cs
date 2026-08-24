using System.Reflection;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The whole of what the package promises.
/// </summary>
/// <remarks>
/// A published version is a promise about every public name in it, and this assembly holds a
/// converter's worth of machinery: an OPC reader, a WordprocessingML model, a style cascade, a
/// font engine, a layout engine and a PDF writer. All of it was public, which would have frozen
/// 174 types — the shape of a positioned line, the name of a table's border edge — at the first
/// version anyone installed. What a caller actually needs is six.
///
/// So everything else is internal, the tests reach it through <c>InternalsVisibleTo</c>, and what
/// is left is written out here in full. Adding to it is then a deliberate act with a diff to show
/// for it, rather than something that happens because a type had to be reached from two places.
/// </remarks>
public class PublicApiTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string Expected =
        """
        class n8PDF.ConversionOptions
            .ctor()
            Boolean ApplyWordBuiltInStyleDefaults { get; set; }
            Boolean DropFontHinting { get; set; }
            DateTimeOffset? CreationDate { get; set; }
            DateTimeOffset? FieldsAsOf { get; set; }
            FontLibrary Fonts { get; set; }
            LayoutOptions Layout { get; set; }
            MailMergeRecord MergeRecord { get; set; }
            PackageLimits Limits { get; set; }
            String FileName { get; set; }
            String Title { get; set; }
        static class n8PDF.Converter
            Byte[] Convert(Byte[] docx, ConversionOptions options = ...)
            Void Convert(Stream docx, Stream pdf, ConversionOptions options = ...)
            Void ConvertFile(String docxPath, String pdfPath, ConversionOptions options = ...)
        class n8PDF.Fonts.FontFormatException
            .ctor(String message)
        class n8PDF.Fonts.FontLibrary
            .ctor()
            Boolean UseSystemFonts { get; set; }
            IReadOnlyCollection<String> RegisteredFamilies { get; }
            IReadOnlyList<String> GetSystemFontDirectories()
            Int32 RegisterDirectory(String path, Boolean recursive = ...)
            Int32 RegisteredFaceCount { get; }
            List<String> FallbackFamilies { get; }
            Void Register(Byte[] data)
            Void RegisterFile(String path)
        class n8PDF.Layout.LayoutOptions
            .ctor()
            Boolean ApplyKerning { get; set; }
            Int32 DefaultTabStopTwips { get; set; }
        class n8PDF.MailMergeRecord
            .ctor(IEnumerable<KeyValuePair<String, String>> fields = ...)
            Dictionary<String, String> Fields { get; }
            Int32 Number { get; set; }
            Int32 Sequence { get; set; }
            String Value(String name)
        class n8PDF.Packaging.PackageLimits
            .ctor()
            Int32 MaximumPartCount { get; set; }
            Int64 MaximumFontBytes { get; set; }
            Int64 MaximumImagePixels { get; set; }
            Int64 MaximumPartBytes { get; set; }
            Int64 MaximumTotalBytes { get; set; }
        class n8PDF.Packaging.PackageTooLargeException
            .ctor(String message)
        """;

    [Fact]
    public void The_package_promises_exactly_this_and_no_more()
    {
        var actual = string.Join("\n", Describe());

        if (actual == Expected.ReplaceLineEndings("\n")) return;

        _output.WriteLine(actual);

        Assert.Fail(
            $"""
             The public surface has changed.

             Every name in it is a promise the next version has to keep, so this is meant to be
             read rather than re-blessed: if the change is wanted, paste what follows into
             {nameof(PublicApiTests)}.{nameof(Expected)}; if it is not, the type or member that
             grew wants making internal.

             {actual}
             """);
    }

    /// <summary>Every exported type, and every public member declared on it.</summary>
    private static IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();

        foreach (var type in typeof(Converter).Assembly.GetExportedTypes()
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            lines.Add(Kind(type) + " " + type.FullName);

            const BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly;

            lines.AddRange(type.GetMembers(flags)
                .Where(Interesting)
                .Select(Signature)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => "    " + name));
        }

        return lines;
    }

    /// <summary>
    /// A property's own accessors rather than the get and set methods they compile to, and no
    /// inherited members: what is asked about is what this assembly declares.
    /// </summary>
    private static bool Interesting(MemberInfo member) => member switch
    {
        MethodInfo method => !method.IsSpecialName,
        ConstructorInfo or PropertyInfo or FieldInfo or EventInfo => true,
        _ => false
    };

    private static string Kind(Type type) =>
        type.IsEnum ? "enum"
        : type.IsInterface ? "interface"
        : type.IsValueType ? "struct"
        : type is { IsAbstract: true, IsSealed: true } ? "static class"
        : "class";

    private static string Signature(MemberInfo member) => member switch
    {
        PropertyInfo property => $"{Name(property.PropertyType)} {property.Name} {{ " +
                                 (property.GetMethod is { IsPublic: true } ? "get; " : string.Empty) +
                                 (property.SetMethod is { IsPublic: true } ? "set; " : string.Empty) + "}",

        FieldInfo field => $"{Name(field.FieldType)} {field.Name}",

        MethodInfo method =>
            $"{Name(method.ReturnType)} {method.Name}({Parameters(method.GetParameters())})",

        ConstructorInfo constructor => $".ctor({Parameters(constructor.GetParameters())})",

        _ => member.Name
    };

    private static string Parameters(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(parameter =>
            (parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty) +
            Name(parameter.ParameterType) + " " + parameter.Name +
            (parameter.HasDefaultValue ? " = ..." : string.Empty)));

    private static string Name(Type type)
    {
        if (type.IsByRef) return Name(type.GetElementType()!);

        if (Nullable.GetUnderlyingType(type) is { } nullable) return Name(nullable) + "?";

        if (!type.IsGenericType) return type.Name;

        return type.Name[..type.Name.IndexOf('`')] +
               "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
    }
}

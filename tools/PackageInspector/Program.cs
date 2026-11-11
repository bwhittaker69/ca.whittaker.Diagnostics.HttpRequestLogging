using System.IO.Compression;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length != 3) throw new ArgumentException("Supply nupkg, snupkg, and expected public repository URL.");
using var package = ZipFile.OpenRead(args[0]);
using var symbols = ZipFile.OpenRead(args[1]);
var root = XDocument.Load(package.Entries.Single(e => e.FullName.EndsWith(".nuspec")).Open()).Root!;
var ns = root.Name.Namespace;
var metadata = root.Element(ns + "metadata")!;
foreach (var name in new[] { "id", "version", "authors", "description", "tags", "copyright", "projectUrl", "releaseNotes", "readme", "icon", "license", "repository" })
{
    if (metadata.Element(ns + name) is null) throw new Exception("Missing metadata: " + name);
}
foreach (var name in new[] { "readme", "icon" })
{
    var path = metadata.Element(ns + name)!.Value;
    if (package.GetEntry(path) is null) throw new Exception("Missing packed file: " + path);
}
if (package.GetEntry("LICENSE.txt") is null) throw new Exception("Missing licence text.");
if (metadata.Element(ns + "license")!.Value != "GPL-3.0-only") throw new Exception("Unexpected licence.");
var repository = metadata.Element(ns + "repository")!;
if ((string?)repository.Attribute("url") != args[2]) throw new Exception("Unexpected repository.");
var commit = (string?)repository.Attribute("commit");
if (commit?.Length != 40) throw new Exception("Missing source commit.");
var dlls = package.Entries.Where(e => e.FullName.StartsWith("lib/") && e.FullName.EndsWith(".dll")).ToArray();
if (dlls.Length == 0) throw new Exception("No assemblies.");
foreach (var dll in dlls)
{
    var xml = Path.ChangeExtension(dll.FullName, ".xml").Replace('\\', '/');
    if (package.GetEntry(xml) is null) throw new Exception("Missing API documentation: " + xml);
    var pdb = symbols.GetEntry(Path.ChangeExtension(dll.FullName, ".pdb").Replace('\\', '/')) ?? throw new Exception("Missing symbols.");
    using var buffer = new MemoryStream();
    using (var input = pdb.Open()) input.CopyTo(buffer);
    buffer.Position = 0;
    using var provider = MetadataReaderProvider.FromPortablePdbStream(buffer);
    var reader = provider.GetMetadataReader();
    foreach (var handle in reader.Documents)
    {
        var path = reader.GetString(reader.GetDocument(handle).Name);
        if (path.Contains(":\\") || path.Contains(":/") || path.Contains("/Whittaker/", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Private source path: " + path);
    }
    var sourceLinkFound = false;
    foreach (var handle in reader.CustomDebugInformation)
    {
        var info = reader.GetCustomDebugInformation(handle);
        if (reader.GetGuid(info.Kind) != new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A")) continue;
        var json = Encoding.UTF8.GetString(reader.GetBlobBytes(info.Value));
        using var document = JsonDocument.Parse(json);
        foreach (var mapping in document.RootElement.GetProperty("documents").EnumerateObject())
        {
            var url = mapping.Value.GetString()!;
            var expected = args[2].Replace("https://github.com/", "https://raw.githubusercontent.com/") + "/" + commit + "/";
            if (!url.StartsWith(expected, StringComparison.Ordinal)) throw new Exception("Unexpected Source Link: " + url);
            Console.WriteLine("Source Link: " + url);
            sourceLinkFound = true;
        }
    }
    if (!sourceLinkFound) throw new Exception("Source Link missing.");
    Console.WriteLine("Assembly, XML, portable PDB verified: " + dll.FullName);
}
Console.WriteLine("Repository commit: " + commit);
Console.WriteLine("Frameworks/dependencies: " + metadata.Element(ns + "dependencies"));
var frameworkGroups = metadata.Element(ns + "frameworkReferences")?.Elements(ns + "group").ToArray() ?? [];
foreach (var framework in new[] { "net9.0", "net10.0" })
{
    var group = frameworkGroups.SingleOrDefault(g => (string?)g.Attribute("targetFramework") == framework);
    if (group is null || !group.Elements(ns + "frameworkReference").Any(r => (string?)r.Attribute("name") == "Microsoft.AspNetCore.App"))
        throw new Exception("Missing ASP.NET Core framework reference: " + framework);
}
Console.WriteLine("Framework references: " + metadata.Element(ns + "frameworkReferences"));
Console.WriteLine("Package inspection passed.");

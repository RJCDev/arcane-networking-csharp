using Godot;
using System.IO;
using System.Xml.Linq;

[Tool]
public partial class WeaverPlugin : EditorPlugin
{
    private const string WeaverName = "ArcaneWeaver";
    private const string FodyFile = "FodyWeavers.xml";
    private const string DllPath = "addons/arcane-networking/plugins/arcane-weaver-bin/ArcaneWeaver.dll";

    public override void _EnterTree()
    {
        EnsureFodyWeaverEntry();
    }

    private void EnsureFodyWeaverEntry()
    {
        var projectPath = ProjectSettings.GlobalizePath("res://");
        var fodyPath = Path.Combine(projectPath, FodyFile);

        XDocument doc;

        if (!File.Exists(fodyPath))
        {
            doc = new XDocument(
                new XElement("Weavers",
                    new XElement(WeaverName, new XAttribute("Path", DllPath))
                )
            );
        }
        else
        {
            doc = XDocument.Load(fodyPath);
            var weavers = doc.Element("Weavers");
            if (weavers == null)
            {
                weavers = new XElement("Weavers");
                doc.Add(weavers);
            }

            var existing = weavers.Element(WeaverName);
            if (existing == null)
            {
                weavers.Add(new XElement(WeaverName, new XAttribute("Path", DllPath)));
            }
            else
            {
                // Ensure Path attribute exists and is correct
                var pathAttr = existing.Attribute("Path");
                if (pathAttr == null || pathAttr.Value != DllPath)
                    existing.SetAttributeValue("Path", DllPath);
            }
        }

        doc.Save(fodyPath);
        GD.Print($"[{WeaverName}] ensured in {FodyFile}");
    }
}

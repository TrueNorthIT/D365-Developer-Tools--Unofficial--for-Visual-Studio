using System.IO;
using System.Windows.Media;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.ToolWindows
{
    /// <summary>Converts a Dataverse entity's SVG icon web resource into a WPF-renderable image.</summary>
    internal static class EntityIconRenderer
    {
        /// <summary>Returns null (rather than throwing) for malformed/unsupported SVG — callers fall back to a generic icon.</summary>
        public static ImageSource TryRender(byte[] svgBytes)
        {
            try
            {
                var reader = new FileSvgReader(new WpfDrawingSettings(), false);
                using (var stream = new MemoryStream(svgBytes))
                {
                    var drawing = reader.Read(stream);
                    if (drawing == null) { return null; }

                    var image = new DrawingImage(drawing);
                    image.Freeze(); // safe to hand back from a background thread and share across nodes
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

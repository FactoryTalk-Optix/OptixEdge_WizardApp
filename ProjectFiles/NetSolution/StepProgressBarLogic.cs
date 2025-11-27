#region Using directives
using System;
using UAManagedCore;
using FTOptix.UI;
using FTOptix.NetLogic;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ProgressBarSVG;
using FTOptix.Core;
#endregion

public class StepProgressBarLogic : BaseNetLogic
{

    public override void Start()
    {
        // Get reference to the SVG image component that will display the progress bar
        svgImage = (AdvancedSVGImage)Owner;
        // Initialize with step 1 and configuration 1 (in the home page user select between template and custom JSON)
        UpdateProgressBar(1, 1);
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    /// <summary>
    /// Updates the progress bar display with the current step and configuration
    /// </summary>
    /// <param name="currentStep">The current active step (1-based index)</param>
    /// <param name="configuration">Configuration type: 1 to 4 for template payload, 5 for custom JSON</param>
    [ExportMethod]
    public void UpdateProgressBar(int currentStep, int configuration)
    {
        // Select the appropriate step configuration and generate SVG content
        string svgContent = configuration switch
        {
            1 or 2 or 3 or 4 => GenerateSVGImage(stepsLabelConfigurationTemplate, currentStep), // Azure IoT Configuration, AWS IoT Configuration, Google Cloud IoT Configuration, HiveMQ Configuration
            5 => GenerateSVGImage(stepsLabelConfigurationCustom, currentStep), // Custom JSON Configuration
            _ => throw new ArgumentException("Invalid configuration value. Supported values are 1 to 4 and 5.")
        };

        // Update the SVG image component with the generated content
        svgImage.SetImageContent(svgContent);
    }

    /// <summary>
    /// Generates the complete SVG content for the progress bar
    /// </summary>
    /// <param name="stepsLabelConfiguration">Dictionary containing step numbers and their labels</param>
    /// <param name="currentStep">The currently active step</param>
    /// <returns>Complete SVG markup as string</returns>
    private static string GenerateSVGImage(Dictionary<int, string> stepsLabelConfiguration, int currentStep)
    {
        // Create the main SVG document
        var svg = new SvgDocument
        {
            // Dynamic width based on number of steps (128px per step + 96px margin)
            Width = (stepsLabelConfiguration.Count - 1) * 128 + 96 + ""
        };
        // Generate steps and labels for each step in the configuration
        foreach (var i in stepsLabelConfiguration.Keys)
        {
            // Calculate horizontal position for this step (48px initial offset + 128px spacing)
            int x = 48 + (i - 1) * 128;

            {
                // Determine circle colors based on step state
                string fillColor = i > currentStep ? "#ccc" : "none"; // Future steps: gray, completed: no fill
                fillColor = i == currentStep ? "#235d9f" : fillColor; // Current step: blue fill
                string strokeColor = i > currentStep ? "none" : "#235d9f"; // Future steps: no stroke, others: blue stroke              
                // Add the step circle
                svg.Steps.Add(new Step { Id = $"step-{i}", Cx = x, Fill = fillColor, Stroke = strokeColor, StrokeWidth = i > currentStep ? 0 : 3 });
                // Add checkmark for completed steps or step number for current/future steps
                if (i < currentStep)
                {
                    // Completed step: show checkmark
                    svg.Checks.Add(new CheckCompleted { Points = GetCheckPoints(x) });
                }
                else
                {
                    // Current/future step: show step number
                    svg.Texts.Add(new TextElement { X = x, Y = 54, Fill = "white", FontSize = "16", Content = i.ToString() });
                }
                // Add step label below the circle
                string label = stepsLabelConfiguration[i];
                fillColor = i > currentStep ? "#333" : "#235d9f"; // Future steps: dark gray, others: blue
                svg.Texts.Add(new TextElement { X = x, Y = 104, Fill = fillColor, FontSize = "12", Content = label });
            }
        }

        // Generate connection lines between steps
        for (int i = 0; i < stepsLabelConfiguration.Count - 1; i++)
        {
            // Line color depends on whether the connection represents a completed transition
            string fillColor = i + 2 > currentStep ? "#ccc" : "#235d9f"; // Gray for future, blue for completed
            svg.Lines.Add(new ConnectionLine { Id = $"line{i + 1}", X = 76 + i * 128, Fill = fillColor });
        }

        // Convert the SVG object to XML string
        var serializer = new XmlSerializer(typeof(SvgDocument));
        using var stringWriter = new StringWriter();
        serializer.Serialize(stringWriter, svg);
        return stringWriter.ToString();
    }

    /// <summary>
    /// Calculates the points for drawing a checkmark icon in SVG polyline format
    /// </summary>
    /// <param name="cx">Center X coordinate of the circle</param>
    /// <param name="cy">Center Y coordinate of the circle (default: 50)</param>
    /// <returns>String containing the polyline points for the checkmark</returns>
    public static string GetCheckPoints(int cx, int cy = 48)
    {
        // Offset relative to the center to create checkmark shape
        int offsetX1 = -10; // Left point of the checkmark
        int offsetX2 = -2;  // Middle point (vertex) of the checkmark
        int offsetX3 = 10;  // Right point of the checkmark

        int offsetY1 = 0;   // Left point vertical offset
        int offsetY2 = 8;   // Middle point vertical offset (lower)
        int offsetY3 = -8;  // Right point vertical offset (higher)

        // Calculate points
        var p1 = $"{cx + offsetX1},{cy + offsetY1}"; // Start of checkmark
        var p2 = $"{cx + offsetX2},{cy + offsetY2}"; // Vertex of checkmark
        var p3 = $"{cx + offsetX3},{cy + offsetY3}"; // End of checkmark

        // return the points as a string for svg polyline element
        return $"{p1} {p2} {p3}";
    }

    // Configuration 1: 3-step process for simple workflows
    readonly Dictionary<int, string> stepsLabelConfigurationTemplate = new()
    {
        {1, "Choose template"},
        {2, "Preview of payload"},
        {3, "Done"},
    };

    // Configuration 2: 4-step process for more complex workflows with JSON analysis
    readonly Dictionary<int, string> stepsLabelConfigurationCustom = new Dictionary<int, string>()
    {
        {1, "Choose template"},
        {2, "Analyze JSON payload"},
        {3, "Preview of payload"},
        {4, "Done"},
    };

    AdvancedSVGImage svgImage;
}

/// <summary>
/// Namespace containing classes for generating SVG-based progress bar elements
/// </summary>
namespace ProgressBarSVG
{
    /// <summary>
    /// Represents the main SVG document structure for the progress bar
    /// </summary>
    [XmlRoot("svg")]
    public class SvgDocument
    {
        [XmlAttribute("width")]
        public string Width { get; set; } = "500";

        [XmlAttribute("height")]
        public string Height { get; set; } = "120";

        [XmlAttribute("xmlns")]
        public string Xmlns { get; set; } = "http://www.w3.org/2000/svg";

        // Collection of step circles
        [XmlElement("circle")]
        public List<Step> Steps { get; set; } = new();

        // Collection of connection lines between steps
        [XmlElement("rect")]
        public List<ConnectionLine> Lines { get; set; } = new();

        // Collection of checkmark icons for completed steps
        [XmlElement("polyline")]
        public List<CheckCompleted> Checks { get; set; } = new();

        // Collection of text elements (step numbers and labels)
        [XmlElement("text")]
        public List<TextElement> Texts { get; set; } = new();
    }

    /// <summary>
    /// Represents a step circle in the progress bar
    /// </summary>
    public class Step
    {
        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlAttribute("cx")]
        public int Cx { get; set; }

        [XmlAttribute("cy")]
        public int Cy { get; set; } = 48;

        [XmlAttribute("r")]
        public int Radius { get; set; } = 24;

        [XmlAttribute("fill")]
        public string Fill { get; set; } = "#ccc";
        [XmlAttribute("stroke")]
        public string Stroke { get; set; } = "none";
        [XmlAttribute("stroke-width")]
        public int StrokeWidth { get; set; } = 0;
    }

    /// <summary>
    /// Represents a horizontal connection line between steps
    /// </summary>
    public class ConnectionLine
    {
        [XmlAttribute("id")]
        public string Id { get; set; }

        [XmlAttribute("x")]
        public int X { get; set; }

        [XmlAttribute("y")]
        public int Y { get; set; } = 46;

        [XmlAttribute("width")]
        public int Width { get; set; } = 72;

        [XmlAttribute("height")]
        public int Height { get; set; } = 4;

        [XmlAttribute("rx")]
        public int Rx { get; set; } = 2;

        [XmlAttribute("ry")]
        public int Ry { get; set; } = 2;

        [XmlAttribute("fill")]
        public string Fill { get; set; } = "#ccc";
    }

    /// <summary>
    /// Represents a checkmark polyline for completed steps
    /// </summary>
    public class CheckCompleted
    {
        [XmlAttribute("id")]
        public string Id { get; set; } = "check-icon";

        [XmlAttribute("points")]
        public string Points { get; set; }

        [XmlAttribute("fill")]
        public string Fill { get; set; } = "none";

        [XmlAttribute("stroke")]
        public string Stroke { get; set; } = "#235d9f";

        [XmlAttribute("stroke-width")]
        public int StrokeWidth { get; set; } = 3;
    }

    /// <summary>
    /// Represents a text element for step numbers and labels
    /// </summary>
    public class TextElement
    {
        [XmlAttribute("x")]
        public int X { get; set; }

        [XmlAttribute("y")]
        public int Y { get; set; }

        [XmlAttribute("text-anchor")]
        public string Anchor { get; set; } = "middle";

        [XmlAttribute("fill")]
        public string Fill { get; set; }

        [XmlAttribute("font-size")]
        public string FontSize { get; set; }

        [XmlAttribute("font-family")]
        public string FontFamily { get; set; } = "Noto Sans";

        [XmlText]
        public string Content { get; set; }
    }


}
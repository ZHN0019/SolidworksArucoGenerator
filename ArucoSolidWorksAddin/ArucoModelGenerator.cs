using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ArucoSolidWorksAddin
{
    public sealed class GenerationResult
    {
        public string PartPath { get; internal set; }
        public string ImagePath { get; internal set; }
        public string StepPath { get; internal set; }
        public string OutputDirectory { get; internal set; }
        public string FileStem { get; internal set; }
        public int SolidBodyCount { get; internal set; }
        public double[] ExtentsMm { get; internal set; }
    }

    public sealed class ArucoModelGenerator
    {
        private const double Mm = 0.001;
        private readonly SldWorks _application;
        private readonly Action<string> _log;

        public ArucoModelGenerator(SldWorks application, Action<string> log = null)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _log = log ?? (_ => { });
        }

        public GenerationResult Generate(ArucoParameters parameters)
        {
            parameters.Validate();
            string fileStem = parameters.GetAvailableFileStem();
            string outputDirectory = parameters.GetSizeOutputDirectory();
            string partPath = Path.Combine(outputDirectory, fileStem + ".SLDPRT");
            string imagePath = Path.Combine(outputDirectory, fileStem + ".png");
            string stepPath = Path.Combine(outputDirectory, fileStem + ".STEP");
            bool[,] marker = ArucoDictionary.GetMarker(parameters.MarkerId);

            _log($"Creating {ArucoDictionary.Name} marker ID {parameters.MarkerId}.");
            _log($"Marker={parameters.MarkerSideMm:0.###} mm, border={parameters.WhiteBorderMm:0.###} mm, " +
                 $"overall={parameters.OverallSideMm:0.###} mm, thickness={parameters.ThicknessMm:0.###} mm.");
            _log("Output directory: " + outputDirectory);

            ModelDoc2 model = CreatePart();
            string title = model.GetTitle();
            try
            {
                BuildTwoBodyPart(model, marker, parameters);
                SetDocumentProperties(model, parameters);
                SavePart(model, partPath);
                SaveMarkerImage(marker, parameters, imagePath);

                GenerationResult result = ValidateLiveModel(model, parameters);
                SaveStep(model, stepPath);
                result.FileStem = fileStem;
                result.PartPath = partPath;
                result.ImagePath = imagePath;
                result.StepPath = stepPath;
                result.OutputDirectory = outputDirectory;

                model.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
                model.ViewDisplayShaded();
                model.ViewZoomtofit2();
                model.GraphicsRedraw2();
                _log("Generation complete: " + partPath);
                return result;
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(model.GetPathName()))
                    _application.CloseDoc(title);
                throw;
            }
        }

        private ModelDoc2 CreatePart()
        {
            string template = _application.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                throw new InvalidOperationException(
                    "SOLIDWORKS default part template is not configured or cannot be found.");

            object created = _application.NewDocument(template, 0, 0.0, 0.0);
            if (!(created is ModelDoc2 model))
                throw new InvalidOperationException("SOLIDWORKS NewDocument did not return a part document.");
            return model;
        }

        private void BuildTwoBodyPart(ModelDoc2 model, bool[,] marker, ArucoParameters p)
        {
            var modeler = (IModeler)_application.GetModeler();
            var builder = new TemporaryBodyBuilder(modeler, _log);
            List<BoxSpec> blackBoxes = CreateConnectedBlackBoxes(marker, p);

            Body2 blackTemporary = builder.UnionConnected(blackBoxes);
            Body2 blackForCut = blackTemporary.Copy2(false) as Body2;
            if (blackForCut == null)
                throw new InvalidOperationException("Could not copy the black temporary body.");

            var outer = new BoxSpec(0.0, 0.0, 0.0,
                p.OverallSideMm, p.OverallSideMm, p.ThicknessMm);
            Body2 whiteBlank = builder.CreateBox(outer);
            Body2 whiteTemporary = builder.Subtract(whiteBlank, blackForCut);

            PartDoc part = (PartDoc)model;
            Feature whiteFeature = part.CreateFeatureFromBody3(
                whiteTemporary, false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) as Feature;
            if (whiteFeature == null)
                throw new InvalidOperationException("Could not insert the white imported-body feature.");
            whiteFeature.Name = "F01_White_Body";

            Body2 whiteBody = GetSolidBodies(part).Single();
            whiteBody.Name = "White_Body";
            ApplyAppearance(whiteBody, whiteFeature, true);

            Feature blackFeature = part.CreateFeatureFromBody3(
                blackTemporary, false, (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck) as Feature;
            if (blackFeature == null)
                throw new InvalidOperationException("Could not insert the black imported-body feature.");
            blackFeature.Name = "F02_Black_Body";

            Body2[] bodies = GetSolidBodies(part);
            if (bodies.Length != 2)
                throw new InvalidOperationException(
                    $"Expected exactly two solid bodies after insertion; found {bodies.Length}.");
            Body2 blackBody = bodies.Single(body =>
                !string.Equals(body.Name, "White_Body", StringComparison.OrdinalIgnoreCase));
            blackBody.Name = "Black_Body";
            ApplyAppearance(blackBody, blackFeature, false);

            if (!model.ForceRebuild3(false))
                throw new InvalidOperationException("SOLIDWORKS rebuild failed after body insertion.");
            _log("Created exactly two solids: White_Body and Black_Body.");
        }

        private static List<BoxSpec> CreateConnectedBlackBoxes(bool[,] marker, ArucoParameters p)
        {
            int n = ArucoDictionary.GridSize;
            double module = p.MarkerSideMm / n;
            double frontDepth = p.FrontPatternDepthMm;
            double backDepth = p.BackMarkDepthMm;
            double linkDepth = p.HiddenLinkDepthMm;
            double frontLinkZ = p.ThicknessMm - frontDepth;
            double backLinkZ = backDepth - linkDepth;
            double channel = Math.Max(module * 0.12, p.MarkerSideMm * 0.008);
            channel = Math.Min(channel, module * 0.28);

            double CellX(int column) => -p.MarkerSideMm / 2.0 + (column + 0.5) * module;
            double CellY(int row) => p.MarkerSideMm / 2.0 - (row + 0.5) * module;

            // The lower-left border module is always black in an ArUco marker.
            double viaX = CellX(0);
            double viaY = CellY(n - 1);
            double viaSize = Math.Min(module * 0.34, Math.Max(channel * 1.25, module * 0.20));

            var boxes = new List<BoxSpec>
            {
                new BoxSpec(viaX, viaY, 0.0, viaSize, viaSize, p.ThicknessMm),
                BoxSpec.Vertical(viaX, CellY(0), CellY(n - 1),
                    frontLinkZ, channel, linkDepth)
            };

            // One hidden horizontal rail per row connects every black cell to the spine.
            for (int row = 0; row < n; row++)
            {
                int maxBlackColumn = 0;
                for (int column = 0; column < n; column++)
                {
                    if (marker[row, column])
                        maxBlackColumn = column;
                }
                boxes.Add(BoxSpec.Horizontal(viaX, CellX(maxBlackColumn), CellY(row),
                    frontLinkZ, channel, linkDepth));
            }

            // Visible front modules overlap the hidden rails, so the black front is one body.
            for (int row = 0; row < n; row++)
            {
                for (int column = 0; column < n; column++)
                {
                    if (!marker[row, column])
                        continue;
                    const double overlapMm = 0.002;
                    double minX = -p.MarkerSideMm / 2.0 + column * module;
                    double maxX = minX + module;
                    double maxY = p.MarkerSideMm / 2.0 - row * module;
                    double minY = maxY - module;
                    if (column > 0) minX -= overlapMm;
                    if (column < n - 1) maxX += overlapMm;
                    if (row > 0) maxY += overlapMm;
                    if (row < n - 1) minY -= overlapMm;
                    boxes.Add(new BoxSpec(
                        (minX + maxX) / 2.0,
                        (minY + maxY) / 2.0,
                        frontLinkZ,
                        maxX - minX,
                        maxY - minY,
                        frontDepth));
                }
            }

            List<BoxSpec> backMarks = CreateBackMarks(p, viaX, viaY);
            double minMarkY = Math.Min(viaY, backMarks.Min(box => box.CenterYmm));
            double maxMarkY = Math.Max(viaY, backMarks.Max(box => box.CenterYmm));
            double backChannel = Math.Min(channel * 0.72, p.MarkerSideMm * 0.018);

            boxes.Add(BoxSpec.Vertical(viaX, minMarkY, maxMarkY,
                backLinkZ, backChannel, linkDepth));

            // Merge hidden back connectors by row to avoid duplicate/contained boolean tools.
            foreach (IGrouping<long, BoxSpec> rowGroup in backMarks.GroupBy(
                box => (long)Math.Round(box.CenterYmm * 100000.0)))
            {
                double y = rowGroup.First().CenterYmm;
                double minX = Math.Min(viaX, rowGroup.Min(box => box.CenterXmm));
                double maxX = Math.Max(viaX, rowGroup.Max(box => box.CenterXmm));
                boxes.Add(BoxSpec.Horizontal(minX, maxX, y,
                    backLinkZ, backChannel, linkDepth));
            }

            boxes.AddRange(backMarks);
            return boxes;
        }

        private static List<BoxSpec> CreateBackMarks(ArucoParameters p, double originX, double originY)
        {
            double z = 0.0;
            double depth = p.BackMarkDepthMm;
            double stroke = Math.Max(p.MarkerSideMm * 0.018, 0.12);
            stroke = Math.Min(stroke, p.MarkerSideMm / 18.0);
            double axisLength = p.MarkerSideMm * 0.42;
            double xTip = originX + axisLength;
            double yTip = originY + axisLength;

            var marks = new List<BoxSpec>
            {
                BoxSpec.Horizontal(originX, xTip, originY, z, stroke, depth),
                BoxSpec.Vertical(originX, originY, yTip, z, stroke, depth),
                new BoxSpec(xTip, originY, z, stroke * 1.8, stroke * 1.8, depth),
                new BoxSpec(xTip - stroke * 1.4, originY + stroke * 1.4,
                    z, stroke * 1.4, stroke * 1.4, depth),
                new BoxSpec(xTip - stroke * 1.4, originY - stroke * 1.4,
                    z, stroke * 1.4, stroke * 1.4, depth),
                new BoxSpec(originX, yTip, z, stroke * 1.8, stroke * 1.8, depth),
                new BoxSpec(originX + stroke * 1.4, yTip - stroke * 1.4,
                    z, stroke * 1.4, stroke * 1.4, depth),
                new BoxSpec(originX - stroke * 1.4, yTip - stroke * 1.4,
                    z, stroke * 1.4, stroke * 1.4, depth)
            };

            double labelPixel = p.MarkerSideMm * 0.022;
            marks.AddRange(CreatePixelText("X", xTip + labelPixel * 2.5,
                originY + labelPixel * 2.6, labelPixel, z, depth));
            marks.AddRange(CreatePixelText("Y", originX + labelPixel * 2.8,
                yTip + labelPixel * 2.6, labelPixel, z, depth));

            string idText = p.MarkerId.ToString(CultureInfo.InvariantCulture);
            double digitPixel = p.MarkerSideMm * 0.048;
            marks.AddRange(CreatePixelText(idText, p.MarkerSideMm * 0.16,
                p.MarkerSideMm * 0.12, digitPixel, z, depth));
            return marks;
        }

        private static IEnumerable<BoxSpec> CreatePixelText(string text, double centerX, double centerY,
            double pixel, double startZ, double depth)
        {
            int totalColumns = text.Length * 3 + Math.Max(0, text.Length - 1);
            double left = centerX - totalColumns * pixel / 2.0;
            double top = centerY + 2.5 * pixel;

            for (int characterIndex = 0; characterIndex < text.Length; characterIndex++)
            {
                string[] glyph = PixelFont.Get(text[characterIndex]);
                double glyphLeft = left + characterIndex * 4.0 * pixel;
                for (int row = 0; row < 5; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        if (glyph[row][column] != '1')
                            continue;
                        double logicalX = glyphLeft + (column + 0.5) * pixel;
                        const double overlapFactor = 1.002;
                        yield return new BoxSpec(
                            2.0 * centerX - logicalX,
                            top - (row + 0.5) * pixel,
                            startZ,
                            pixel * overlapFactor,
                            pixel * overlapFactor,
                            depth);
                    }
                }
            }
        }

        private static void ApplyAppearance(Body2 body, Feature feature, bool white)
        {
            double color = white ? 0.96 : 0.015;
            object values = new[]
            {
                color, color, color,
                0.25, 0.75, 0.18, 0.20, 0.0, 0.0
            };
            body.MaterialPropertyValues2 = values;
            feature.SetMaterialPropertyValues2(values,
                (int)swInConfigurationOpts_e.swThisConfiguration, null);
        }

        private static void SetDocumentProperties(ModelDoc2 model, ArucoParameters p)
        {
            CustomPropertyManager properties =
                model.Extension.CustomPropertyManager[string.Empty];
            AddProperty(properties, "Generator", "ArUco Generator for SOLIDWORKS 1.1");
            AddProperty(properties, "Dictionary", ArucoDictionary.Name);
            AddProperty(properties, "MarkerId", p.MarkerId.ToString(CultureInfo.InvariantCulture));
            AddProperty(properties, "MarkerSideMm", Invariant(p.MarkerSideMm));
            AddProperty(properties, "WhiteBorderMm", Invariant(p.WhiteBorderMm));
            AddProperty(properties, "OverallSideMm", Invariant(p.OverallSideMm));
            AddProperty(properties, "OverallThicknessMm", Invariant(p.ThicknessMm));
            AddProperty(properties, "FrontPatternDepthMm", Invariant(p.FrontPatternDepthMm));
            AddProperty(properties, "BackMarkDepthMm", Invariant(p.BackMarkDepthMm));
            AddProperty(properties, "SolidBodyDesign", "White_Body + Black_Body");
            AddProperty(properties, "CoordinateConvention",
                "+X right and +Y up when viewed from marker front");
        }

        private static void AddProperty(CustomPropertyManager manager, string name, string value)
        {
            manager.Add3(name,
                (int)swCustomInfoType_e.swCustomInfoText,
                value,
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }

        private static string Invariant(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static void SavePart(ModelDoc2 model, string path)
        {
            int errors = 0;
            int warnings = 0;
            bool saved = model.Extension.SaveAs(path,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null, ref errors, ref warnings);
            if (!saved || errors != 0 || !File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidOperationException(
                     $"Could not save SLDPRT; saved={saved}, errors={errors}, warnings={warnings}.");
        }

        private void SaveStep(ModelDoc2 model, string path)
        {
            int stepApPreference = (int)swUserPreferenceIntegerValue_e.swStepAP;
            int appearancePreference =
                (int)swUserPreferenceToggle_e.swStepExportAppearances;
            int previousStepAp =
                _application.GetUserPreferenceIntegerValue(stepApPreference);
            bool previousExportAppearances =
                _application.GetUserPreferenceToggle(appearancePreference);

            try
            {
                _application.SetUserPreferenceIntegerValue(stepApPreference, 214);
                _application.SetUserPreferenceToggle(appearancePreference, true);

                int errors = 0;
                int warnings = 0;
                bool saved = model.Extension.SaveAs(path,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, ref errors, ref warnings);
                if (!saved || errors != 0 || !File.Exists(path) ||
                    new FileInfo(path).Length < 100)
                {
                    throw new InvalidOperationException(
                        $"Could not save STEP; saved={saved}, errors={errors}, warnings={warnings}.");
                }
            }
            finally
            {
                _application.SetUserPreferenceIntegerValue(
                    stepApPreference, previousStepAp);
                _application.SetUserPreferenceToggle(
                    appearancePreference, previousExportAppearances);
            }

            using (var reader = new StreamReader(path))
            {
                char[] headerBuffer = new char[4096];
                int read = reader.Read(headerBuffer, 0, headerBuffer.Length);
                string header = new string(headerBuffer, 0, read);
                string firstLine = header.Split('\n')[0];
                if (!string.Equals(firstLine?.Trim(), "ISO-10303-21;",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "STEP export did not produce a valid ISO-10303-21 header.");
                }
                if (header.IndexOf("STEP AP214",
                    StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException(
                        "STEP export did not use the required AP214 format.");
                }
            }
        }

        private static void SaveMarkerImage(bool[,] marker, ArucoParameters p, string path)
        {
            const int imagePixels = 1200;
            double codeRatio = p.MarkerSideMm / p.OverallSideMm;
            int codePixels = Math.Max(6, (int)Math.Round(imagePixels * codeRatio));
            codePixels -= codePixels % ArucoDictionary.GridSize;
            int offset = (imagePixels - codePixels) / 2;
            int modulePixels = codePixels / ArucoDictionary.GridSize;

            using (var bitmap = new Bitmap(imagePixels, imagePixels, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                for (int row = 0; row < ArucoDictionary.GridSize; row++)
                {
                    for (int column = 0; column < ArucoDictionary.GridSize; column++)
                    {
                        if (!marker[row, column])
                            continue;
                        graphics.FillRectangle(Brushes.Black,
                            offset + column * modulePixels,
                            offset + row * modulePixels,
                            modulePixels,
                            modulePixels);
                    }
                }
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static GenerationResult ValidateLiveModel(ModelDoc2 model, ArucoParameters p)
        {
            if (!model.ForceRebuild3(false))
                throw new InvalidOperationException("Final model rebuild failed.");

            PartDoc part = (PartDoc)model;
            Body2[] bodies = GetSolidBodies(part);
            if (bodies.Length != 2)
                throw new InvalidOperationException(
                    $"Final model contains {bodies.Length} solid bodies instead of 2.");

            string[] names = bodies.Select(body => body.Name).OrderBy(name => name).ToArray();
            if (!names.Contains("Black_Body") || !names.Contains("White_Body"))
                throw new InvalidOperationException(
                    "Final body names are not White_Body and Black_Body.");

            double[] extents = ExactExtentsMm(bodies);
            double tolerance = 0.001;
            if (Math.Abs(extents[0] - p.OverallSideMm) > tolerance ||
                Math.Abs(extents[1] - p.OverallSideMm) > tolerance ||
                Math.Abs(extents[2] - p.ThicknessMm) > tolerance)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "Exact extents mismatch: {0:F6} x {1:F6} x {2:F6} mm.",
                    extents[0], extents[1], extents[2]));
            }

            return new GenerationResult
            {
                SolidBodyCount = bodies.Length,
                ExtentsMm = extents
            };
        }

        internal static Body2[] GetSolidBodies(PartDoc part)
        {
            object raw = part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            if (raw == null)
                return Array.Empty<Body2>();
            if (raw is object[] objects)
                return objects.OfType<Body2>().ToArray();
            if (raw is Array array)
                return array.Cast<object>().OfType<Body2>().ToArray();
            return raw is Body2 body ? new[] { body } : Array.Empty<Body2>();
        }

        internal static double[] ExactExtentsMm(IEnumerable<Body2> bodies)
        {
            double minX = double.PositiveInfinity;
            double minY = double.PositiveInfinity;
            double minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity;
            double maxY = double.NegativeInfinity;
            double maxZ = double.NegativeInfinity;

            foreach (Body2 body in bodies)
            {
                Extreme(body, -1, 0, 0, out double xMin, out _, out _);
                Extreme(body, 1, 0, 0, out double xMax, out _, out _);
                Extreme(body, 0, -1, 0, out _, out double yMin, out _);
                Extreme(body, 0, 1, 0, out _, out double yMax, out _);
                Extreme(body, 0, 0, -1, out _, out _, out double zMin);
                Extreme(body, 0, 0, 1, out _, out _, out double zMax);
                minX = Math.Min(minX, xMin);
                maxX = Math.Max(maxX, xMax);
                minY = Math.Min(minY, yMin);
                maxY = Math.Max(maxY, yMax);
                minZ = Math.Min(minZ, zMin);
                maxZ = Math.Max(maxZ, zMax);
            }

            return new[]
            {
                (maxX - minX) / Mm,
                (maxY - minY) / Mm,
                (maxZ - minZ) / Mm
            };
        }

        private static void Extreme(Body2 body, double dx, double dy, double dz,
            out double x, out double y, out double z)
        {
            if (!body.GetExtremePoint(dx, dy, dz, out x, out y, out z))
                throw new InvalidOperationException("IBody2.GetExtremePoint failed.");
        }
    }
}

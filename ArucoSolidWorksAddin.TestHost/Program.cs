using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using ArucoSolidWorksAddin;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ArucoSolidWorksAddin.TestHost
{
    internal static class Program
    {
        private const double Mm = 0.001;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 1 &&
                string.Equals(args[0], "--probe-connect", StringComparison.OrdinalIgnoreCase))
            {
                int cookie = args.Length >= 2
                    ? int.Parse(args[1], CultureInfo.InvariantCulture)
                    : 1;
                return ProbeConnect(cookie);
            }

            string output = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "validation-output"));
            int markerId = args.Length > 1
                ? int.Parse(args[1], CultureInfo.InvariantCulture)
                : 0;
            double markerSide = args.Length > 2
                ? double.Parse(args[2], CultureInfo.InvariantCulture)
                : 20.0;
            double thickness = args.Length > 3
                ? double.Parse(args[3], CultureInfo.InvariantCulture)
                : 1.0;
            double whiteBorder = args.Length > 4
                ? double.Parse(args[4], CultureInfo.InvariantCulture)
                : 0.0;
            bool validateAddinLoad = args.Any(value =>
                string.Equals(value, "--validate-addin", StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(output);
            foreach (string file in Directory.GetFiles(output))
                File.Delete(file);

            var report = new Dictionary<string, object>
            {
                ["passed"] = false,
                ["startedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["outputDirectory"] = output,
            };

            SldWorks application = null;
            ModelDoc2 reopened = null;
            bool ownsApplication = false;
            var existingSolidWorksPids = new HashSet<int>(
                Process.GetProcessesByName("SLDWORKS").Select(process => process.Id));
            try
            {
                Type progId = Type.GetTypeFromProgID("SldWorks.Application.33", true);
                application = (SldWorks)Activator.CreateInstance(progId);
                int solidWorksProcessId = application.GetProcessID();
                ownsApplication = !existingSolidWorksPids.Contains(solidWorksProcessId);
                if (!ownsApplication)
                    throw new InvalidOperationException(
                        "COM activation returned a pre-existing SOLIDWORKS session.");
                application.Visible = true;
                DateTime startupDeadline = DateTime.UtcNow.AddSeconds(45);
                while (!application.StartupProcessCompleted &&
                       DateTime.UtcNow < startupDeadline)
                {
                    System.Threading.Thread.Sleep(250);
                }
                if (!application.StartupProcessCompleted)
                    throw new InvalidOperationException(
                        "SOLIDWORKS startup did not complete within 45 seconds.");
                report["solidWorksRevision"] = application.RevisionNumber();
                report["solidWorksLanguage"] = application.GetCurrentLanguage();
                report["solidWorksProcessId"] = solidWorksProcessId;

                var parameters = new ArucoParameters
                {
                    MarkerId = markerId,
                    MarkerSideMm = markerSide,
                    ThicknessMm = thickness,
                    WhiteBorderMm = whiteBorder,
                    OutputDirectory = output,
                };
                var log = new List<string>();
                var generator = new ArucoModelGenerator(application,
                    message =>
                    {
                        log.Add(message);
                        Console.WriteLine(message);
                    });
                GenerationResult generated = generator.Generate(parameters);
                report["generationLog"] = log;
                report["partPath"] = generated.PartPath;
                report["imagePath"] = generated.ImagePath;
                report["stepPath"] = generated.StepPath;
                report["generationDirectory"] = generated.OutputDirectory;
                report["liveBodyCount"] = generated.SolidBodyCount;
                report["liveExtentsMm"] = generated.ExtentsMm;

                string expectedGenerationDirectory =
                    Path.GetFullPath(parameters.GetSizeOutputDirectory());
                if (!string.Equals(
                    Path.GetFullPath(generated.OutputDirectory),
                    expectedGenerationDirectory,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Generated files were not placed in the expected size directory.");
                }
                ValidateStep(generated.StepPath);
                report["stepValidated"] = true;

                ModelDoc2 live = (ModelDoc2)application.ActiveDoc;
                string liveTitle = live.GetTitle();
                application.CloseDoc(liveTitle);

                int openErrors = 0;
                int openWarnings = 0;
                reopened = application.OpenDoc6(
                    generated.PartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    string.Empty,
                    ref openErrors,
                    ref openWarnings);
                if (reopened == null)
                    throw new InvalidOperationException("OpenDoc6 returned null.");
                if (openErrors != 0 || openWarnings != 0)
                    throw new InvalidOperationException(
                        $"Reopen errors={openErrors}, warnings={openWarnings}.");
                if (!reopened.ForceRebuild3(false))
                    throw new InvalidOperationException("Reopened model rebuild failed.");
                report["reopenErrors"] = openErrors;
                report["reopenWarnings"] = openWarnings;

                PartDoc part = (PartDoc)reopened;
                Body2[] bodies = GetBodies(part);
                if (bodies.Length != 2)
                    throw new InvalidOperationException(
                        $"Reopened model has {bodies.Length} solid bodies.");
                string[] bodyNames = bodies.Select(body => body.Name)
                    .OrderBy(name => name).ToArray();
                if (!bodyNames.SequenceEqual(new[] { "Black_Body", "White_Body" }))
                    throw new InvalidOperationException(
                        "Unexpected body names: " + string.Join(", ", bodyNames));
                report["reopenedBodyCount"] = bodies.Length;
                report["bodyNames"] = bodyNames;

                double[] extents = ExactExtentsMm(bodies);
                AssertNear(extents[0], parameters.OverallSideMm, 0.001, "X extent");
                AssertNear(extents[1], parameters.OverallSideMm, 0.001, "Y extent");
                AssertNear(extents[2], parameters.ThicknessMm, 0.001, "Z extent");
                report["reopenedExtentsMm"] = extents;

                double totalVolumeMm3 = bodies.Sum(BodyVolumeM3) / (Mm * Mm * Mm);
                double expectedVolume = parameters.OverallSideMm *
                                        parameters.OverallSideMm *
                                        parameters.ThicknessMm;
                AssertNear(totalVolumeMm3, expectedVolume, 0.01, "combined body volume");
                report["combinedBodyVolumeMm3"] = totalVolumeMm3;

                var featureErrors = new List<object>();
                for (Feature feature = reopened.FirstFeature() as Feature;
                     feature != null;
                     feature = feature.GetNextFeature() as Feature)
                {
                    bool warning;
                    int code = feature.GetErrorCode2(out warning);
                    featureErrors.Add(new
                    {
                        name = feature.Name,
                        type = feature.GetTypeName2(),
                        errorCode = code,
                        warning,
                    });
                    if (code != 0 || warning)
                        throw new InvalidOperationException(
                            $"Feature {feature.Name} error={code}, warning={warning}.");
                }
                report["features"] = featureErrors;

                ValidatePng(generated.ImagePath, parameters);
                report["pngMatrixValidated"] = true;
                report["frontPreview"] = SavePreview(reopened,
                    Path.Combine(generated.OutputDirectory,
                        generated.FileStem + "_front.png"),
                    "*Front", (int)swStandardViews_e.swFrontView);
                report["backPreview"] = SavePreview(reopened,
                    Path.Combine(generated.OutputDirectory,
                        generated.FileStem + "_back.png"),
                    "*Back", (int)swStandardViews_e.swBackView);

                string reopenedTitle = reopened.GetTitle();
                application.CloseDoc(reopenedTitle);
                if (Marshal.IsComObject(reopened))
                    Marshal.FinalReleaseComObject(reopened);
                reopened = null;

                ValidateStepGeometry(application, generated.StepPath, parameters,
                    out int stepBodyCount, out double[] stepExtents, out int stepOpenErrors);
                report["stepOpenErrors"] = stepOpenErrors;
                report["stepBodyCount"] = stepBodyCount;
                report["stepExtentsMm"] = stepExtents;

                if (validateAddinLoad)
                {
                    string installedPath = Path.Combine(
                        System.Environment.GetFolderPath(
                            System.Environment.SpecialFolder.CommonApplicationData),
                        "Codex", "ArucoSolidWorksAddin", "ArucoSolidWorksAddin.dll");
                    string addinPath = File.Exists(installedPath)
                        ? installedPath
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            "ArucoSolidWorksAddin.dll");
                    int loadStatus = application.LoadAddIn(addinPath);
                    if (loadStatus != (int)swLoadAddinError_e.swSuccess &&
                        loadStatus != (int)swLoadAddinError_e.swAddinAlreadyLoaded)
                    {
                        throw new InvalidOperationException(
                            $"SOLIDWORKS LoadAddIn failed; status={loadStatus}.");
                    }
                    object loadedAddin = application.GetAddInObject(
                        "{78E6B279-EA99-4BD3-8C1B-CB1C8A309DF1}") ??
                        application.GetAddInObject("Codex.ArucoSolidWorksAddin");
                    if (loadedAddin == null)
                        throw new InvalidOperationException(
                            "LoadAddIn succeeded but GetAddInObject returned null.");
                    report["loadAddinStatus"] = loadStatus;
                    report["addinConnectToSwValidated"] = true;
                    int unloadStatus = application.UnloadAddIn(addinPath);
                    if (unloadStatus != 0)
                        throw new InvalidOperationException(
                            $"SOLIDWORKS UnloadAddIn failed; status={unloadStatus}.");
                    report["unloadAddinStatus"] = unloadStatus;
                }
                else
                {
                    report["addinConnectToSwValidated"] = false;
                    report["addinLoadNote"] =
                        "Run with --validate-addin after elevated COM registration.";
                }

                report["passed"] = true;
                report["finishedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                WriteReport(output, report);
                Console.WriteLine("VALIDATION PASSED");
                return 0;
            }
            catch (Exception ex)
            {
                report["error"] = ex.ToString();
                report["finishedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                WriteReport(output, report);
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                try
                {
                    if (application != null && reopened != null)
                        application.CloseDoc(reopened.GetTitle());
                }
                catch
                {
                    // Preserve the primary validation error.
                }

                try
                {
                    if (ownsApplication)
                        application?.ExitApp();
                }
                catch
                {
                    // The report already records the primary result.
                }

                if (reopened != null && Marshal.IsComObject(reopened))
                    Marshal.FinalReleaseComObject(reopened);
                if (application != null && Marshal.IsComObject(application))
                    Marshal.FinalReleaseComObject(application);
            }
        }

        private static int ProbeConnect(int cookie)
        {
            SldWorks application = null;
            bool ownsApplication = false;
            var existingSolidWorksPids = new HashSet<int>(
                Process.GetProcessesByName("SLDWORKS").Select(process => process.Id));
            try
            {
                Type progId = Type.GetTypeFromProgID("SldWorks.Application.33", true);
                application = (SldWorks)Activator.CreateInstance(progId);
                int solidWorksProcessId = application.GetProcessID();
                ownsApplication = !existingSolidWorksPids.Contains(solidWorksProcessId);
                if (!ownsApplication)
                    throw new InvalidOperationException(
                        "COM activation returned a pre-existing SOLIDWORKS session.");
                application.Visible = true;
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (!application.StartupProcessCompleted && DateTime.UtcNow < deadline)
                    System.Threading.Thread.Sleep(250);

                var addin = new SwAddin();
                bool connected = addin.ConnectToSW(application, cookie);
                Console.WriteLine("Cookie=" + cookie);
                Console.WriteLine("Connected=" + connected);
                Console.WriteLine(addin.LastConnectError ?? string.Empty);
                return connected ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 2;
            }
            finally
            {
                try
                {
                    if (ownsApplication)
                        application?.ExitApp();
                }
                catch { }
                if (application != null && Marshal.IsComObject(application))
                    Marshal.FinalReleaseComObject(application);
            }
        }

        private static void WriteReport(string output, Dictionary<string, object> report)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(Path.Combine(output, "validation_report.json"),
                serializer.Serialize(report), new UTF8Encoding(true));
        }

        private static Body2[] GetBodies(PartDoc part)
        {
            object raw = part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            if (raw is object[] objects)
                return objects.OfType<Body2>().ToArray();
            if (raw is Array array)
                return array.Cast<object>().OfType<Body2>().ToArray();
            return raw is Body2 body ? new[] { body } : Array.Empty<Body2>();
        }

        private static double BodyVolumeM3(Body2 body)
        {
            object raw = body.GetMassProperties(1.0);
            double[] values = raw is double[] doubles
                ? doubles
                : ((Array)raw).Cast<object>().Select(Convert.ToDouble).ToArray();
            return values[3];
        }

        private static double[] ExactExtentsMm(IEnumerable<Body2> bodies)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity,
                minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity,
                maxZ = double.NegativeInfinity;
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
                (maxZ - minZ) / Mm,
            };
        }

        private static void Extreme(Body2 body, double dx, double dy, double dz,
            out double x, out double y, out double z)
        {
            if (!body.GetExtremePoint(dx, dy, dz, out x, out y, out z))
                throw new InvalidOperationException("GetExtremePoint failed.");
        }

        private static void AssertNear(double actual, double expected, double tolerance, string label)
        {
            if (Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    "{0}: expected {1:F6}, actual {2:F6}, tolerance {3:F6}.",
                    label, expected, actual, tolerance));
        }

        private static void ValidatePng(string path, ArucoParameters parameters)
        {
            bool[,] expected = ArucoDictionary.GetMarker(parameters.MarkerId);
            using (var image = new Bitmap(path))
            {
                if (image.Width != 1200 || image.Height != 1200)
                    throw new InvalidOperationException("Marker PNG must be 1200 x 1200 pixels.");
                double codeRatio = parameters.MarkerSideMm / parameters.OverallSideMm;
                int codePixels = Math.Max(6, (int)Math.Round(image.Width * codeRatio));
                codePixels -= codePixels % ArucoDictionary.GridSize;
                int offset = (image.Width - codePixels) / 2;
                int module = codePixels / ArucoDictionary.GridSize;
                for (int row = 0; row < 6; row++)
                {
                    for (int column = 0; column < 6; column++)
                    {
                        Color pixel = image.GetPixel(
                            offset + column * module + module / 2,
                            offset + row * module + module / 2);
                        bool black = pixel.R < 128 && pixel.G < 128 && pixel.B < 128;
                        if (black != expected[row, column])
                            throw new InvalidOperationException(
                                $"PNG matrix mismatch at row={row}, column={column}.");
                    }
                }

                if (parameters.WhiteBorderMm > 0.0)
                {
                    Color corner = image.GetPixel(0, 0);
                    if (corner.R < 250 || corner.G < 250 || corner.B < 250)
                        throw new InvalidOperationException("PNG white border is not white.");
                }
            }
        }

        private static void ValidateStep(string path)
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 100)
                throw new InvalidOperationException("STEP file is missing or empty.");

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
                        "STEP file does not start with ISO-10303-21.");
                }
                if (header.IndexOf("STEP AP214",
                    StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException(
                        "STEP file was not exported as AP214.");
                }
            }
        }

        private static void ValidateStepGeometry(
            SldWorks application,
            string path,
            ArucoParameters parameters,
            out int bodyCount,
            out double[] extents,
            out int openErrors)
        {
            ModelDoc2 imported = null;
            bodyCount = 0;
            extents = null;
            openErrors = 0;
            try
            {
                imported = application.LoadFile4(path, "r", null, ref openErrors);
                if (imported == null)
                    throw new InvalidOperationException(
                        $"SOLIDWORKS could not reopen STEP; errors={openErrors}.");
                if (openErrors != 0)
                    throw new InvalidOperationException(
                        $"STEP reopen errors={openErrors}.");
                if (!imported.ForceRebuild3(false))
                    throw new InvalidOperationException(
                        "Reopened STEP model failed to rebuild.");

                Body2[] bodies = GetBodies((PartDoc)imported);
                bodyCount = bodies.Length;
                if (bodyCount != 2)
                    throw new InvalidOperationException(
                        $"Reopened STEP has {bodyCount} solid bodies instead of 2.");

                extents = ExactExtentsMm(bodies);
                AssertNear(extents[0], parameters.OverallSideMm, 0.001,
                    "STEP X extent");
                AssertNear(extents[1], parameters.OverallSideMm, 0.001,
                    "STEP Y extent");
                AssertNear(extents[2], parameters.ThicknessMm, 0.001,
                    "STEP Z extent");
            }
            finally
            {
                if (imported != null)
                {
                    try { application.CloseDoc(imported.GetTitle()); } catch { }
                    if (Marshal.IsComObject(imported))
                        Marshal.FinalReleaseComObject(imported);
                }
            }
        }

        private static string SavePreview(ModelDoc2 model, string pngPath,
            string viewName, int viewId)
        {
            model.ShowNamedView2(viewName, viewId);
            model.ViewDisplayShaded();
            model.ViewZoomtofit2();
            model.GraphicsRedraw2();
            string bmpPath = Path.ChangeExtension(pngPath, ".bmp");
            if (!model.SaveBMP(bmpPath, 1200, 1200))
                throw new InvalidOperationException("SaveBMP failed for " + viewName);
            using (Image image = Image.FromFile(bmpPath))
                image.Save(pngPath, ImageFormat.Png);
            File.Delete(bmpPath);
            return pngPath;
        }
    }
}

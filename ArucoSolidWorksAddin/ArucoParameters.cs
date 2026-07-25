using System;
using System.Globalization;
using System.IO;

namespace ArucoSolidWorksAddin
{
    public sealed class ArucoParameters
    {
        public int MarkerId { get; set; }
        public double MarkerSideMm { get; set; } = 20.0;
        public double ThicknessMm { get; set; } = 1.0;
        public double WhiteBorderMm { get; set; }
        public string OutputDirectory { get; set; }

        public double OverallSideMm => MarkerSideMm + 2.0 * WhiteBorderMm;
        public double FrontPatternDepthMm => ThicknessMm * 0.35;
        public double BackMarkDepthMm => ThicknessMm * 0.18;
        public double HiddenLinkDepthMm => ThicknessMm * 0.08;

        public void Validate()
        {
            if (MarkerId < 0 || MarkerId > 30)
                throw new ArgumentOutOfRangeException(nameof(MarkerId), "ArUco ID must be 0..30.");
            if (MarkerSideMm < 5.0 || MarkerSideMm > 500.0)
                throw new ArgumentOutOfRangeException(nameof(MarkerSideMm), "Marker side must be 5..500 mm.");
            if (ThicknessMm < 0.2 || ThicknessMm > 100.0)
                throw new ArgumentOutOfRangeException(nameof(ThicknessMm), "Thickness must be 0.2..100 mm.");
            if (WhiteBorderMm < 0.0 || WhiteBorderMm > 500.0)
                throw new ArgumentOutOfRangeException(nameof(WhiteBorderMm), "White border must be 0..500 mm.");
            if (string.IsNullOrWhiteSpace(OutputDirectory))
                throw new ArgumentException("Output directory is required.", nameof(OutputDirectory));
        }

        public string FileStem()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "ArUco_{0}_ID{1:00}_S{2}_B{3}_T{4}",
                ArucoDictionary.Name,
                MarkerId,
                FileNumber(MarkerSideMm),
                FileNumber(WhiteBorderMm),
                FileNumber(ThicknessMm));
        }

        public string GetSizeOutputDirectory()
        {
            return Path.Combine(OutputDirectory,
                "打印-" + FolderNumber(MarkerSideMm));
        }

        public string GetAvailableFileStem()
        {
            string sizeOutputDirectory = GetSizeOutputDirectory();
            Directory.CreateDirectory(sizeOutputDirectory);
            string stem = FileStem();
            int suffix = 2;
            while (File.Exists(Path.Combine(sizeOutputDirectory, stem + ".SLDPRT")) ||
                   File.Exists(Path.Combine(sizeOutputDirectory, stem + ".png")) ||
                   File.Exists(Path.Combine(sizeOutputDirectory, stem + ".STEP")))
            {
                stem = FileStem() + "_" + suffix;
                suffix++;
            }
            return stem;
        }

        private static string FileNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', 'p');
        }

        private static string FolderNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}

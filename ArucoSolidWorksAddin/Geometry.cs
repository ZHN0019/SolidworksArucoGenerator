using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ArucoSolidWorksAddin
{
    internal readonly struct BoxSpec
    {
        public BoxSpec(double centerXmm, double centerYmm, double startZmm,
            double widthMm, double lengthMm, double heightMm)
        {
            CenterXmm = centerXmm;
            CenterYmm = centerYmm;
            StartZmm = startZmm;
            WidthMm = widthMm;
            LengthMm = lengthMm;
            HeightMm = heightMm;
        }

        public double CenterXmm { get; }
        public double CenterYmm { get; }
        public double StartZmm { get; }
        public double WidthMm { get; }
        public double LengthMm { get; }
        public double HeightMm { get; }

        public static BoxSpec Horizontal(double x1, double x2, double y, double z,
            double channelWidth, double height)
        {
            double min = Math.Min(x1, x2) - channelWidth / 2.0;
            double max = Math.Max(x1, x2) + channelWidth / 2.0;
            return new BoxSpec((min + max) / 2.0, y, z, max - min, channelWidth, height);
        }

        public static BoxSpec Vertical(double x, double y1, double y2, double z,
            double channelWidth, double height)
        {
            double min = Math.Min(y1, y2) - channelWidth / 2.0;
            double max = Math.Max(y1, y2) + channelWidth / 2.0;
            return new BoxSpec(x, (min + max) / 2.0, z, channelWidth, max - min, height);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "C=({0:F4},{1:F4},{2:F4}) S=({3:F4},{4:F4},{5:F4})",
                CenterXmm, CenterYmm, StartZmm, WidthMm, LengthMm, HeightMm);
        }
    }

    internal sealed class TemporaryBodyBuilder
    {
        private const double Mm = 0.001;
        private readonly IModeler _modeler;
        private readonly Action<string> _log;

        public TemporaryBodyBuilder(IModeler modeler, Action<string> log)
        {
            _modeler = modeler ?? throw new ArgumentNullException(nameof(modeler));
            _log = log ?? (_ => { });
        }

        public Body2 CreateBox(BoxSpec box)
        {
            if (box.WidthMm <= 0 || box.LengthMm <= 0 || box.HeightMm <= 0)
                throw new InvalidOperationException("Invalid box dimensions: " + box);

            double[] data =
            {
                box.CenterXmm * Mm,
                box.CenterYmm * Mm,
                box.StartZmm * Mm,
                0.0, 0.0, 1.0,
                box.WidthMm * Mm,
                box.LengthMm * Mm,
                box.HeightMm * Mm
            };
            Body2 body = _modeler.CreateBodyFromBox3(data);
            if (body == null)
                throw new InvalidOperationException("CreateBodyFromBox3 failed: " + box);
            return body;
        }

        public Body2 UnionConnected(IEnumerable<BoxSpec> boxes)
        {
            List<BoxSpec> list = boxes.ToList();
            if (list.Count == 0)
                throw new InvalidOperationException("Cannot union an empty box list.");

            Body2 current = CreateBox(list[0]);
            for (int index = 1; index < list.Count; index++)
            {
                Body2 tool = CreateBox(list[index]);
                current = BooleanSingle(current, tool,
                    (int)swBodyOperationType_e.SWBODYADD,
                    "union box " + index + " of " + list.Count + ": " + list[index]);
            }

            _log("Black temporary body union complete: " + list.Count + " boxes.");
            return current;
        }

        public Body2 Subtract(Body2 target, Body2 tool)
        {
            return BooleanSingle(target, tool,
                (int)swBodyOperationType_e.SWBODYCUT, "subtract black volume from plate");
        }

        private static Body2 BooleanSingle(Body2 target, Body2 tool, int operation, string label)
        {
            int errorCode;
            object raw = target.Operations2(operation, tool, out errorCode);
            Body2[] bodies = ToBodies(raw);
            if (errorCode != (int)swBodyOperationError_e.swBodyOperationNoError)
                throw new InvalidOperationException(
                    $"Temporary-body {label} failed; error={errorCode}.");
            if (bodies.Length != 1)
                throw new InvalidOperationException(
                    $"Temporary-body {label} returned {bodies.Length} bodies; expected 1.");
            return bodies[0];
        }

        private static Body2[] ToBodies(object raw)
        {
            if (raw == null)
                return Array.Empty<Body2>();
            if (raw is Body2 body)
                return new[] { body };
            if (raw is object[] objects)
                return objects.OfType<Body2>().ToArray();
            if (raw is Array array)
                return array.Cast<object>().OfType<Body2>().ToArray();
            return Array.Empty<Body2>();
        }
    }
}

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace ArucoSolidWorksAddin
{
    internal sealed class GeneratorForm : Form
    {
        private readonly SldWorks _application;
        private readonly NumericUpDown _markerId;
        private readonly NumericUpDown _side;
        private readonly NumericUpDown _thickness;
        private readonly NumericUpDown _border;
        private readonly TextBox _output;
        private readonly MarkerPreviewPanel _preview;
        private readonly Button _generate;
        private readonly Label _status;

        public GeneratorForm(SldWorks application)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));

            Text = "ArUco 零件生成器";
            ClientSize = new Size(520, 600);
            MinimumSize = new Size(520, 600);
            MaximumSize = new Size(720, 780);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = Color.FromArgb(247, 248, 250);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            Controls.Add(root);

            var title = new Label
            {
                Dock = DockStyle.Fill,
                Text = "DICT_4X4_50  ·  ID 0–30",
                Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 34, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
            };
            root.Controls.Add(title, 0, 0);

            _preview = new MarkerPreviewPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 12),
                BackColor = Color.White,
            };
            root.Controls.Add(_preview, 0, 1);

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6,
                Margin = new Padding(0),
                Padding = new Padding(0, 4, 0, 0),
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
            for (int i = 0; i < 5; i++)
                fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(fields, 0, 2);

            _markerId = CreateNumeric(0, 30, 0, 0, 1);
            _side = CreateNumeric(5, 500, 20, 3, 0.5M);
            _thickness = CreateNumeric(0.2M, 100, 1, 3, 0.1M);
            _border = CreateNumeric(0, 500, 0, 3, 0.5M);
            AddField(fields, 0, "ArUco 编号", _markerId, "0–30");
            AddField(fields, 1, "码区边长", _side, "mm");
            AddField(fields, 2, "整体厚度", _thickness, "mm");
            AddField(fields, 3, "白色边缘宽度", _border, "mm");

            _output = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 7, 6, 7),
                Text = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "SOLIDWORKS ArUco"),
            };
            var browse = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 7, 0, 7),
                Text = "…",
                AccessibleName = "选择输出目录",
            };
            var outputLabel = CreateFieldLabel("输出目录");
            fields.Controls.Add(outputLabel, 0, 4);
            fields.Controls.Add(_output, 1, 4);
            fields.Controls.Add(browse, 2, 4);
            browse.Click += Browse_Click;

            var note = new Label
            {
                Dock = DockStyle.Fill,
                Text = "码区边长不包含白边；零件由 White_Body 与 Black_Body 两个实体组成。",
                ForeColor = Color.FromArgb(86, 91, 99),
                AutoEllipsis = true,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 8, 0, 0),
            };
            fields.Controls.Add(note, 0, 5);
            fields.SetColumnSpan(note, 3);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = new Padding(0),
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            root.Controls.Add(footer, 0, 3);

            _status = new Label
            {
                Dock = DockStyle.Fill,
                Text = "就绪",
                ForeColor = Color.FromArgb(76, 82, 90),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Padding = new Padding(0, 0, 8, 0),
            };
            footer.Controls.Add(_status, 0, 0);

            _generate = new Button
            {
                Dock = DockStyle.Fill,
                Text = "生成模型",
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                BackColor = Color.FromArgb(28, 98, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 8, 0, 6),
            };
            _generate.FlatAppearance.BorderSize = 0;
            footer.Controls.Add(_generate, 1, 0);

            _markerId.ValueChanged += (_, __) => UpdatePreview();
            _border.ValueChanged += (_, __) => UpdatePreview();
            _side.ValueChanged += (_, __) => UpdatePreview();
            _generate.Click += Generate_Click;
            AcceptButton = _generate;
            UpdatePreview();
        }

        private static NumericUpDown CreateNumeric(decimal min, decimal max,
            decimal value, int decimalPlaces, decimal increment)
        {
            return new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = min,
                Maximum = max,
                Value = value,
                DecimalPlaces = decimalPlaces,
                Increment = increment,
                ThousandsSeparator = false,
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(0, 7, 6, 7),
            };
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(42, 47, 54),
                AutoEllipsis = true,
            };
        }

        private static void AddField(TableLayoutPanel panel, int row, string label,
            Control input, string unit)
        {
            panel.Controls.Add(CreateFieldLabel(label), 0, row);
            panel.Controls.Add(input, 1, row);
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = unit,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(86, 91, 99),
                AutoEllipsis = true,
            }, 2, row);
        }

        private void Browse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "选择 SLDPRT、PNG 与 STEP 的输出根目录",
                SelectedPath = Directory.Exists(_output.Text)
                    ? _output.Text
                    : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                ShowNewFolderButton = true,
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _output.Text = dialog.SelectedPath;
            }
        }

        private void Generate_Click(object sender, EventArgs e)
        {
            var parameters = new ArucoParameters
            {
                MarkerId = decimal.ToInt32(_markerId.Value),
                MarkerSideMm = decimal.ToDouble(_side.Value),
                ThicknessMm = decimal.ToDouble(_thickness.Value),
                WhiteBorderMm = decimal.ToDouble(_border.Value),
                OutputDirectory = _output.Text.Trim(),
            };

            _generate.Enabled = false;
            UseWaitCursor = true;
            _status.Text = "正在创建双实体零件…";
            Application.DoEvents();

            try
            {
                var generator = new ArucoModelGenerator(_application,
                    message =>
                    {
                        _status.Text = message;
                        Application.DoEvents();
                    });
                GenerationResult result = generator.Generate(parameters);
                _status.Text = string.Format(CultureInfo.InvariantCulture,
                    "完成：{0} × {1} × {2} mm",
                    result.ExtentsMm[0].ToString("0.###", CultureInfo.InvariantCulture),
                    result.ExtentsMm[1].ToString("0.###", CultureInfo.InvariantCulture),
                    result.ExtentsMm[2].ToString("0.###", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                _status.Text = "生成失败";
                MessageBox.Show(this, ex.Message, "ArUco 生成失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                _generate.Enabled = true;
            }
        }

        private void UpdatePreview()
        {
            _preview.MarkerId = decimal.ToInt32(_markerId.Value);
            _preview.BorderRatio = decimal.ToDouble(_border.Value) /
                                   Math.Max(0.001, decimal.ToDouble(_side.Value));
            _preview.Invalidate();
        }
    }

    internal sealed class MarkerPreviewPanel : Panel
    {
        public int MarkerId { get; set; }
        public double BorderRatio { get; set; }

        public MarkerPreviewPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.FromArgb(232, 235, 239));
            int available = Math.Max(20, Math.Min(ClientSize.Width, ClientSize.Height) - 16);
            int left = (ClientSize.Width - available) / 2;
            int top = (ClientSize.Height - available) / 2;
            e.Graphics.FillRectangle(Brushes.White, left, top, available, available);

            double totalUnits = 1.0 + 2.0 * Math.Max(0.0, BorderRatio);
            int markerPixels = Math.Max(6, (int)Math.Round(available / totalUnits));
            markerPixels -= markerPixels % ArucoDictionary.GridSize;
            int markerLeft = left + (available - markerPixels) / 2;
            int markerTop = top + (available - markerPixels) / 2;
            int module = markerPixels / ArucoDictionary.GridSize;
            bool[,] marker = ArucoDictionary.GetMarker(MarkerId);

            for (int row = 0; row < ArucoDictionary.GridSize; row++)
            {
                for (int column = 0; column < ArucoDictionary.GridSize; column++)
                {
                    if (marker[row, column])
                    {
                        e.Graphics.FillRectangle(Brushes.Black,
                            markerLeft + column * module,
                            markerTop + row * module,
                            module,
                            module);
                    }
                }
            }
            e.Graphics.DrawRectangle(Pens.WhiteSmoke, left, top, available - 1, available - 1);
        }
    }
}

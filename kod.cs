using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace qqqwww_painT
{
    public enum Tool { Pencil, Line, Rectangle, Ellipse, Fill, Eraser }

    public class DoubleBufferedPictureBox : PictureBox
    {
        public DoubleBufferedPictureBox()
        {
            this.DoubleBuffered = true;
        }
    }

    public class MainForm : Form
    {
        private Bitmap canvas;
        private Stack<Bitmap> undoStack = new Stack<Bitmap>();
        private Stack<Bitmap> redoStack = new Stack<Bitmap>();
        private const int MAX_UNDO = 30;

        private Point startPoint, lastPoint, currentPoint;
        private bool isDrawing = false;
        private Tool currentTool = Tool.Pencil;

        private Color foreColor = Color.Black;
        private Color backColor = Color.White;
        private float thickness = 2f;
        private DashStyle dashStyle = DashStyle.Solid;

        private DoubleBufferedPictureBox canvasBox;
        private MenuStrip menuStrip;
        private ToolStrip toolStrip;
        private ToolStripButton[] toolButtons;
        private Panel propPanel;
        private Button btnForeColor, btnBackColor;
        private NumericUpDown nudThickness;
        private ComboBox cmbStyle;

        public MainForm()
        {
            this.Text = "Simple Paint (.NET Framework)";
            this.Size = new Size(1050, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(800, 500);

            InitializeUI();
            NewCanvas();
            SaveState(); 
        }

        private void InitializeUI()
        {
            menuStrip = new MenuStrip();

            var fileMenu = new ToolStripMenuItem("Файл");
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("Новый", null, (s, e) => NewCanvas()) { ShortcutKeys = Keys.Control | Keys.N });
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("Открыть", null, (s, e) => OpenFile()) { ShortcutKeys = Keys.Control | Keys.O });
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("Сохранить", null, (s, e) => SaveFile()) { ShortcutKeys = Keys.Control | Keys.S });
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("Выход", null, (s, e) => this.Close()));

            var editMenu = new ToolStripMenuItem("Правка");
            editMenu.DropDownItems.Add(new ToolStripMenuItem("Отменить", null, (s, e) => Undo()) { ShortcutKeys = Keys.Control | Keys.Z });
            editMenu.DropDownItems.Add(new ToolStripMenuItem("Повторить", null, (s, e) => Redo()) { ShortcutKeys = Keys.Control | Keys.Y });

            var viewMenu = new ToolStripMenuItem("Вид");
            viewMenu.DropDownItems.Add(new ToolStripMenuItem("Показать панель свойств", null, (s, e) => { propPanel.Visible = !propPanel.Visible; }));

            menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu });
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            toolStrip = new ToolStrip();
            toolStrip.Dock = DockStyle.Top;
            toolStrip.ImageScalingSize = new Size(24, 24);

            string[] toolNames = { "Карандаш", "Линия", "Прямоуг.", "Эллипс", "Заливка", "Ластик" };
            toolButtons = new ToolStripButton[toolNames.Length];
            for (int i = 0; i < toolNames.Length; i++)
            {
                toolButtons[i] = new ToolStripButton(toolNames[i]);
                toolButtons[i].Click += (s, e) => SetTool((Tool)i);
                toolStrip.Items.Add(toolButtons[i]);
            }
            SetTool(Tool.Pencil);
            this.Controls.Add(toolStrip);

            btnForeColor = new Button { Text = "Передний цвет", Location = new Point(10, 10), Width = 200, Height = 30, BackColor = foreColor };
            btnForeColor.Click += (s, e) => {
                using (var cd = new ColorDialog { Color = foreColor })
                {
                    if (cd.ShowDialog() == DialogResult.OK) { foreColor = cd.Color; btnForeColor.BackColor = foreColor; }
                }
            };

            btnBackColor = new Button { Text = "Фоновый цвет", Location = new Point(10, 50), Width = 200, Height = 30, BackColor = backColor };
            btnBackColor.Click += (s, e) => {
                using (var cd = new ColorDialog { Color = backColor })
                {
                    if (cd.ShowDialog() == DialogResult.OK) { backColor = cd.Color; btnBackColor.BackColor = backColor; }
                }
            };

            var lblThick = new Label { Text = "Толщина линии:", Location = new Point(10, 95), AutoSize = true };
            nudThickness = new NumericUpDown { Location = new Point(10, 115), Width = 100, Minimum = 1, Maximum = 50, Value = 2 };
            nudThickness.ValueChanged += (s, e) => thickness = (float)nudThickness.Value;

            var lblStyle = new Label { Text = "Стиль линии:", Location = new Point(10, 155), AutoSize = true };
            cmbStyle = new ComboBox { Location = new Point(10, 175), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStyle.Items.AddRange(new object[] { "Сплошная", "Штриховая", "Пунктирная" });
            cmbStyle.SelectedIndex = 0;
            cmbStyle.SelectedIndexChanged += (s, e) => {
                switch (cmbStyle.SelectedIndex)
                {
                    case 1:
                        dashStyle = DashStyle.Dash;
                        break;
                    case 2:
                        dashStyle = DashStyle.Dot;
                        break;
                    default:
                        dashStyle = DashStyle.Solid;
                        break;
                }
            };

            propPanel.Controls.AddRange(new Control[] { btnForeColor, btnBackColor, lblThick, nudThickness, lblStyle, cmbStyle });
            this.Controls.Add(propPanel);

            canvasBox = new DoubleBufferedPictureBox { Dock = DockStyle.Fill, BackColor = Color.LightGray, Cursor = Cursors.Cross };
            canvasBox.Paint += CanvasBox_Paint;
            canvasBox.MouseDown += CanvasBox_MouseDown;
            canvasBox.MouseMove += CanvasBox_MouseMove;
            canvasBox.MouseUp += CanvasBox_MouseUp;
            this.Controls.Add(canvasBox);
        }

        private void SetTool(Tool t)
        {
            currentTool = t;
            for (int i = 0; i < toolButtons.Length; i++)
                toolButtons[i].Checked = (i == (int)t);

            canvasBox.Cursor = t == Tool.Fill ? Cursors.Hand : Cursors.Cross;
        }
        private void CanvasBox_Paint(object sender, PaintEventArgs e)
        {
            if (canvas == null) return;
            e.Graphics.DrawImage(canvas, 0, 0);

            if (isDrawing && currentTool != Tool.Pencil && currentTool != Tool.Eraser && currentTool != Tool.Fill)
            {
                using (Pen pen = CreatePen())
                {
                    Rectangle rect = GetRectangle();
                    switch (currentTool)
                    {
                        case Tool.Line:
                            e.Graphics.DrawLine(pen, startPoint, currentPoint);
                            break;
                        case Tool.Rectangle:
                            e.Graphics.DrawRectangle(pen, rect);
                            break;
                        case Tool.Ellipse:
                            e.Graphics.DrawEllipse(pen, rect);
                            break;
                    }
                }
            }
        }

        private void CanvasBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (canvas == null) return;

            isDrawing = true;
            startPoint = e.Location;
            lastPoint = e.Location;
            currentPoint = e.Location;

            if (currentTool == Tool.Fill)
            {
                FloodFill(startPoint);
                SaveState();
                canvasBox.Invalidate();
                isDrawing = false;
                return;
            }

            SaveState();
        }
        private void CanvasBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDrawing) return;
            currentPoint = e.Location;

            if (currentTool == Tool.Pencil || currentTool == Tool.Eraser)
            {
                using (Graphics g = Graphics.FromImage(canvas))
                using (Pen pen = new Pen(currentTool == Tool.Eraser ? backColor : foreColor, thickness))
                {
                    g.DrawLine(pen, lastPoint, currentPoint);
                }
                lastPoint = currentPoint;
                canvasBox.Invalidate();
            }
            else if (currentTool == Tool.Line || currentTool == Tool.Rectangle || currentTool == Tool.Ellipse)
            {
                canvasBox.Invalidate(); 
            }
        }

        private void CanvasBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isDrawing) return;
            isDrawing = false;

            if (currentTool == Tool.Line || currentTool == Tool.Rectangle || currentTool == Tool.Ellipse)
            {
                using (Graphics g = Graphics.FromImage(canvas))
                using (Pen pen = CreatePen())
                {
                    Rectangle rect = GetRectangle();
                    switch (currentTool)
                    {
                        case Tool.Line: g.DrawLine(pen, startPoint, currentPoint); break;
                        case Tool.Rectangle: g.DrawRectangle(pen, rect); break;
                        case Tool.Ellipse: g.DrawEllipse(pen, rect); break;
                    }
                }
                canvasBox.Invalidate();
            }
        }

        private Pen CreatePen() => new Pen(foreColor, thickness) { DashStyle = dashStyle, StartCap = LineCap.Round, EndCap = LineCap.Round };
        private Rectangle GetRectangle() => Rectangle.FromLTRB(
            Math.Min(startPoint.X, currentPoint.X),
            Math.Min(startPoint.Y, currentPoint.Y), Math.Max(startPoint.X, currentPoint.X),
            Math.Max(startPoint.Y, currentPoint.Y)
        );

        private void FloodFill(Point start)
        {
            Color target = canvas.GetPixel(start.X, start.Y);
            if (target == foreColor) return;

            Stack<Point> stack = new Stack<Point>();
            stack.Push(start);

            int w = canvas.Width, h = canvas.Height;
            while (stack.Count > 0)
            {
                Point p = stack.Pop();
                if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;

                Color current = canvas.GetPixel(p.X, p.Y);
                if (current != target) continue;

                canvas.SetPixel(p.X, p.Y, foreColor);

                stack.Push(new Point(p.X + 1, p.Y));
                stack.Push(new Point(p.X - 1, p.Y));
                stack.Push(new Point(p.X, p.Y + 1));
                stack.Push(new Point(p.X, p.Y - 1));
            }
        }

        private void SaveState()
        {
            if (canvas == null) return;
            if (undoStack.Count >= MAX_UNDO) undoStack.Pop();
            undoStack.Push((Bitmap)canvas.Clone());
            redoStack.Clear();
        }

        private void Undo()
        {
            if (undoStack.Count > 1)
            {
                redoStack.Push((Bitmap)canvas.Clone());
                canvas = undoStack.Pop();
                canvasBox.Invalidate();
            }
        }
        private void Redo()
        {
            if (redoStack.Count > 0)
            {
                undoStack.Push((Bitmap)canvas.Clone());
                canvas = redoStack.Pop();
                canvasBox.Invalidate();
            }
        }

        private void NewCanvas()
        {
            canvas = new Bitmap(canvasBox.ClientSize.Width, canvasBox.ClientSize.Height);
            using (Graphics g = Graphics.FromImage(canvas))
                g.Clear(backColor);
            canvasBox.Image = null;
            canvasBox.Invalidate();
            undoStack.Clear();
            redoStack.Clear();
            SaveState();
        }

        private void OpenFile()
        {
            using (var ofd = new OpenFileDialog { Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp", Title = "Открыть изображение" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        canvas = new Bitmap(Image.FromFile(ofd.FileName));
                        canvasBox.Invalidate();
                        undoStack.Clear();
                        redoStack.Clear();
                        SaveState();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка открытия: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void SaveFile()
        {
            using (var sfd = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg|BMP|*.bmp", Title = "Сохранить изображение" })
            {
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string ext = Path.GetExtension(sfd.FileName).ToLower();
                        ImageFormat fmt;
                        if (ext == ".png")
                            fmt = ImageFormat.Png;
                        else if (ext == ".jpg" || ext == ".jpeg")
                            fmt = ImageFormat.Jpeg;
                        else
                            fmt = ImageFormat.Bmp;
                        canvas.Save(sfd.FileName, fmt);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
    }

}

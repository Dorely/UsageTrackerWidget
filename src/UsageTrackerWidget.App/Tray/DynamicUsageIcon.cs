using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace UsageTrackerWidget.App.Tray;

internal static class DynamicUsageIcon
{
    private const int PixelSize = 64;

    private static readonly ConditionalWeakTable<Icon, MemoryStream> IconStreams = new();
    private static readonly Color Background = Color.FromArgb(0x17, 0x1D, 0x28);
    private static readonly Color Foreground = Color.White;
    private static readonly Color Healthy = Color.FromArgb(0x4A, 0xDE, 0x80);
    private static readonly Color Warning = Color.FromArgb(0xFB, 0xBF, 0x24);
    private static readonly Color Critical = Color.FromArgb(0xFB, 0x71, 0x85);
    private static readonly Color Unknown = Color.FromArgb(0x5B, 0x9D, 0xFF);

    public static Icon Create(double? usedPercent, bool warning)
    {
        var text = usedPercent is null
            ? "?"
            : Math.Clamp(Math.Round(usedPercent.Value), 0, 100)
                .ToString("0", CultureInfo.InvariantCulture);

        var accent = warning
            ? Warning
            : usedPercent switch
            {
                null => Unknown,
                >= 90 => Critical,
                >= 70 => Warning,
                _ => Healthy,
            };

        using var bitmap = new Bitmap(PixelSize, PixelSize, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var backgroundBrush = new SolidBrush(Background))
        using (var foregroundBrush = new SolidBrush(Foreground))
        using (var borderPen = new Pen(accent, 4))
        using (var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        })
        using (var font = CreateNumberFont(graphics, text, format))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);
            graphics.FillEllipse(backgroundBrush, 2, 2, PixelSize - 4, PixelSize - 4);
            graphics.DrawEllipse(borderPen, 2.5f, 2.5f, PixelSize - 5, PixelSize - 5);
            graphics.DrawString(
                text,
                font,
                foregroundBrush,
                new RectangleF(0, -2, PixelSize, PixelSize),
                format);
        }

        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, ImageFormat.Png);
        var icoBytes = CreateSingleFrameIco(pngStream.ToArray(), PixelSize);
        var iconStream = new MemoryStream(icoBytes, writable: false);
        var icon = new Icon(iconStream);
        IconStreams.Add(icon, iconStream);
        return icon;
    }

    private static Font CreateNumberFont(Graphics graphics, string text, StringFormat format)
    {
        if (text.Length < 3)
        {
            var fontSize = text.Length == 2 ? 40f : 44f;
            return new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        Font? condensed = null;
        try
        {
            condensed = new Font(
                "Bahnschrift SemiCondensed",
                31f,
                FontStyle.Bold,
                GraphicsUnit.Pixel);
            if (graphics.MeasureString(text, condensed, PointF.Empty, format).Width <= 52f)
            {
                return condensed;
            }
        }
        catch (ArgumentException)
        {
        }

        condensed?.Dispose();
        return new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static byte[] CreateSingleFrameIco(byte[] pngBytes, int size)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)pngBytes.Length);
            writer.Write((uint)(6 + 16));
            writer.Write(pngBytes);
        }

        return stream.ToArray();
    }
}

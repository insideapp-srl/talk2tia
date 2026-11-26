using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Talk2Tia.Core.Updates;
using T = Talk2Tia.Core.Tray.TrayIcon.Text;
using Links = Talk2Tia.Core.Tray.TrayIcon.Links;

namespace Talk2Tia.Core.Tray;

/// <summary>Talk2Tia window opened from the notification-area icon: version, update, links, donation, InsideApp.</summary>
public sealed class AboutForm : Form
{
    private static readonly Color Primary = ColorTranslator.FromHtml("#335EEA");
    private static readonly Color Teal = ColorTranslator.FromHtml("#46CDCF");
    private static readonly Color Ink = ColorTranslator.FromHtml("#1B2A4E");
    private static readonly Color Muted = ColorTranslator.FromHtml("#506690");
    private static readonly Color Success = ColorTranslator.FromHtml("#2E9E7B");
    private static readonly Color KoFi = ColorTranslator.FromHtml("#FF5E5B");
    private static readonly Color Footer = ColorTranslator.FromHtml("#F4F7FC");
    private static readonly Color Line = ColorTranslator.FromHtml("#E3E8F2");

    private const int Width0 = 460;
    private const int Pad = 28;

    public AboutForm(UpdateService updates, int sessions, Action installUpdate)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.75F);
        Text = "Talk2Tia";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        ClientSize = new Size(Width0, 486);
        Icon = TrayIcon.LoadIcon(32);

        Controls.Add(Header());

        var y = 150;
        Controls.Add(Label(T.Sessions(sessions), Pad, y, Muted));
        y += 26;

        var update = updates.Available;
        if (update?.LocalMsi is not null && !updates.InstallScheduled)
        {
            Controls.Add(Label(T.UpdateReady(update.Version), Pad, y + 6, Primary, bold: true));
            var install = ButtonOf(T.Install, Primary, Color.White, Width0 - Pad - 110, y, 110, 32);
            install.Click += (_, _) =>
            {
                installUpdate();
                Close();
            };
            Controls.Add(install);
        }
        else if (updates.InstallScheduled && update is not null)
        {
            Controls.Add(Label(T.UpdateScheduled(update.Version), Pad, y + 6, Primary, bold: true));
        }
        else if (updates.Enabled)
        {
            Controls.Add(Label("✓ " + T.UpToDate, Pad, y + 6, Success));
        }

        y += 46;
        var links = new FlowLayoutPanel { Location = new Point(Pad - 3, y), Size = new Size(Width0 - 2 * Pad, 26), WrapContents = false, BackColor = Color.White };
        links.Controls.Add(LinkOf(T.Documentation, Links.Docs));
        links.Controls.Add(LinkOf(T.Website, Links.Site));
        links.Controls.Add(LinkOf("GitHub", Links.GitHub));
        links.Controls.Add(LinkOf(T.WorkFolderShort, null, () => TrayIcon.OpenWorkFolder()));
        Controls.Add(links);

        y += 40;
        Controls.Add(new Panel { Location = new Point(Pad, y), Size = new Size(Width0 - 2 * Pad, 1), BackColor = Line });

        y += 18;
        var pitch = new Label
        {
            Text = T.DonatePitch,
            Location = new Point(Pad, y),
            Size = new Size(Width0 - 2 * Pad, 42),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.25F),
        };
        Controls.Add(pitch);

        y += 48;
        var donate = ButtonOf("♥  " + T.DonateButton, KoFi, Color.White, Pad, y, Width0 - 2 * Pad, 42);
        donate.Font = new Font("Segoe UI Semibold", 10.5F);
        donate.Click += (_, _) => TrayIcon.Open(Links.Donate);
        Controls.Add(donate);

        Controls.Add(FooterPanel(y + 66));
    }

    private Control Header()
    {
        var header = new Panel { Location = Point.Empty, Size = new Size(Width0, 128) };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var brush = new LinearGradientBrush(header.ClientRectangle, Primary, Teal, LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(brush, header.ClientRectangle);
            }

            var s = header.Height / 128f; // DPI scale
            var tile = new RectangleF(Pad * s, 26 * s, 76 * s, 76 * s);
            using (var path = Rounded(tile, 18 * s))
            using (var white = new SolidBrush(Color.White))
            {
                g.FillPath(white, path);
            }

            using (var logo = TrayIcon.LoadImage("talk2tia-128.png"))
            {
                if (logo is not null)
                {
                    g.DrawImage(logo, tile.X + 10 * s, tile.Y + 10 * s, 56 * s, 56 * s);
                }
            }

            using var title = new Font("Segoe UI Semibold", 22F);
            using var sub = new Font("Segoe UI", 10F);
            using var small = new Font("Segoe UI", 8.75F);
            var x = tile.Right + 18 * s;
            g.DrawString("Talk2Tia", title, Brushes.White, x, 24 * s);
            g.DrawString(T.Tagline, sub, Brushes.White, x + 2 * s, 66 * s);
            using var faded = new SolidBrush(Color.FromArgb(215, Color.White));
            g.DrawString($"{T.VersionLabel} {UpdateService.Current.ToString(3)} · beta", small, faded, x + 2 * s, 88 * s);
        };
        return header;
    }

    private Control FooterPanel(int top)
    {
        var footer = new Panel { Location = new Point(0, top), Size = new Size(Width0, ClientSize.Height - top), BackColor = Footer };
        footer.Controls.Add(new Panel { Location = Point.Empty, Size = new Size(Width0, 1), BackColor = Line });
        footer.Controls.Add(new Label { Text = T.MadeBy, Location = new Point(Pad, 14), AutoSize = true, ForeColor = Muted, BackColor = Footer, Font = new Font("Segoe UI", 8.5F) });

        var logo = new PictureBox
        {
            Image = TrayIcon.LoadImage("insideapp.png"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(Pad, 34),
            Size = new Size(130, 25),
            Cursor = Cursors.Hand,
            BackColor = Footer,
        };
        logo.Click += (_, _) => TrayIcon.Open(Links.InsideApp);
        new ToolTip().SetToolTip(logo, Links.InsideApp);
        footer.Controls.Add(logo);

        footer.Controls.Add(new Label
        {
            Text = "© 2026 InsideApp S.r.l. · MIT",
            Location = new Point(Pad, 66),
            AutoSize = true,
            ForeColor = Muted,
            BackColor = Footer,
            Font = new Font("Segoe UI", 8F),
        });

        var close = ButtonOf(T.Close, Color.White, Ink, Width0 - Pad - 96, 26, 96, 34);
        close.FlatAppearance.BorderSize = 1;
        close.FlatAppearance.BorderColor = Line;
        close.Click += (_, _) => Close();
        footer.Controls.Add(close);
        CancelButton = close;
        return footer;
    }

    private static Label Label(string text, int x, int y, Color color, bool bold = false) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = color,
        BackColor = Color.White,
        Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", 9.75F),
    };

    private static Button ButtonOf(string text, Color back, Color fore, int x, int y, int w, int h)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.15f);
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(back, 0.05f);
        return button;
    }

    private static LinkLabel LinkOf(string text, string? url, Action? action = null)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Primary,
            ActiveLinkColor = Teal,
            VisitedLinkColor = Primary,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = new Font("Segoe UI Semibold", 9.75F),
            Margin = new Padding(3, 3, 14, 3),
            BackColor = Color.White,
        };
        link.LinkClicked += (_, _) =>
        {
            if (action is not null)
            {
                action();
            }
            else
            {
                TrayIcon.Open(url!);
            }
        };
        return link;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

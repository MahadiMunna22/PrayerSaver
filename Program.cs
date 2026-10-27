using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;

record Config(string City = "Bern", string Country = "CH", int Method = 3);

record Win(string Name, DateTime Start, DateTime End, DateTime GreenEnd, DateTime RedStart);

static class Store
{
    public static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrayerSaver");
    public static string ConfigPath => System.IO.Path.Combine(Dir, "config.json");

    public static Config LoadConfig()
    {
        System.IO.Directory.CreateDirectory(Dir);
        if (!System.IO.File.Exists(ConfigPath))
            System.IO.File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(new Config(), new JsonSerializerOptions { WriteIndented = true }));
        return JsonSerializer.Deserialize<Config>(System.IO.File.ReadAllText(ConfigPath)) ?? new Config();
    }

    static readonly string[] Keys = { "Fajr", "Sunrise", "Dhuhr", "Asr", "Sunset", "Maghrib", "Isha" };

    public static async Task<Dictionary<string, DateTime>> LoadDay(DateTime d, Config c)
    {
        var cache = System.IO.Path.Combine(Dir, $"{d:yyyy-MM-dd}_{c.City}_{c.Method}.json");
        string json;
        if (System.IO.File.Exists(cache)) json = System.IO.File.ReadAllText(cache);
        else
        {
            var url = $"https://api.aladhan.com/v1/timingsByCity/{d:dd-MM-yyyy}" +
                      $"?city={Uri.EscapeDataString(c.City)}&country={Uri.EscapeDataString(c.Country)}&method={c.Method}";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            json = await http.GetStringAsync(url);
            System.IO.File.WriteAllText(cache, json);
        }
        using var doc = JsonDocument.Parse(json);
        var t = doc.RootElement.GetProperty("data").GetProperty("timings");
        var r = new Dictionary<string, DateTime>();
        foreach (var k in Keys)
        {
            var s = t.GetProperty(k).GetString()!.Substring(0, 5);
            r[k] = d.Date + TimeSpan.ParseExact(s, "hh\\:mm", CultureInfo.InvariantCulture);
        }
        return r;
    }

    static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);
    static TimeSpan Mn(TimeSpan a, TimeSpan b) => a < b ? a : b;
    static DateTime Cl(DateTime v, DateTime lo, DateTime hi) => v < lo ? lo : v > hi ? hi : v;

    static Win Mk(string name, DateTime s, DateTime e, DateTime g, DateTime r)
    {
        g = Cl(g, s, e);
        r = Cl(r, g, e);
        return new Win(name, s, e, g, r);
    }

    public static List<Win> BuildWindows(Dictionary<DateTime, Dictionary<string, DateTime>> days)
    {
        var list = new List<Win>();
        foreach (var (d, t) in days)
        {
            if (!days.TryGetValue(d.AddDays(1), out var n)) continue;

            var night = n["Fajr"] - t["Sunset"];
            var mid = t["Sunset"] + night / 2;
            var third = t["Sunset"] + night / 3;

            var fajrS = t["Fajr"]; var fajrE = t["Sunrise"];
            list.Add(Mk("Fajr", fajrS, fajrE, fajrS + (fajrE - fajrS) / 2, fajrE - M(20)));

            var dhS = t["Dhuhr"]; var dhE = t["Asr"];
            list.Add(Mk("Dhuhr", dhS, dhE, dhS + (dhE - dhS) / 2, dhE - M(30)));

            var asrS = t["Asr"]; var asrE = t["Sunset"];
            list.Add(Mk("Asr", asrS, asrE, asrE - M(40), asrE - M(15)));

            var mgS = t["Maghrib"]; var mgE = t["Isha"]; var mgW = mgE - mgS;
            list.Add(Mk("Maghrib", mgS, mgE, mgS + Mn(M(20), mgW * 0.5), mgE - Mn(M(20), mgW * 0.25)));

            var isS = t["Isha"];
            if (mid > isS) list.Add(Mk("Isha", isS, mid, third, mid - M(30)));
        }
        return list.OrderBy(w => w.Start).ToList();
    }
}

class SaverWindow : Window
{
    List<Win>? wins;
    string? error;
    readonly TextBlock name, countdown, best, next, clock, dateTb;
    readonly Border bestPill;
    readonly Shapes.Path arc;
    readonly Shapes.Path[] zones;
    double shown, spin;
    Point? last;

    static readonly Color Green = Color.FromRgb(46, 204, 113);
    static readonly Color Orange = Color.FromRgb(243, 156, 18);
    static readonly Color Red = Color.FromRgb(231, 76, 60);

    static readonly Dictionary<string, string> EndLabel = new()
    {
        ["Fajr"] = "sunrise", ["Dhuhr"] = "Asr", ["Asr"] = "sunset",
        ["Maghrib"] = "Isha", ["Isha"] = "midnight"
    };

    static string Fmt(TimeSpan r) => $"{(int)r.TotalHours:00}:{r.Minutes:00}:{r.Seconds:00}";
    static string FmtShort(TimeSpan r) => r.TotalHours >= 1 ? Fmt(r) : $"{r.Minutes:00}:{r.Seconds:00}";

    static TextBlock Tb(double size, Brush b) => new TextBlock
    {
        FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground = b,
        HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center
    };

    public SaverWindow()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true;
        ShowInTaskbar = false; Background = Brushes.Black; Cursor = Cursors.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;

        var root = new Canvas();

        double cx = SystemParameters.PrimaryScreenWidth / 2 - SystemParameters.VirtualScreenLeft;
        double cy = SystemParameters.PrimaryScreenHeight / 2 - SystemParameters.VirtualScreenTop + 100;

        var box = new Grid { Width = 460, Height = 460 };
        Canvas.SetLeft(box, cx - 230);
        Canvas.SetTop(box, cy - 230);

        var ring = new Canvas { Width = 460, Height = 460 };
        var track = new Shapes.Ellipse
        {
            Width = 400, Height = 400, Stroke = new SolidColorBrush(Color.FromRgb(34, 34, 34)), StrokeThickness = 14
        };
        Canvas.SetLeft(track, 30); Canvas.SetTop(track, 30);
        arc = new Shapes.Path { StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

        ring.Children.Add(track);
        zones = new Shapes.Path[3];
        for (int i = 0; i < 3; i++)
        {
            zones[i] = new Shapes.Path { StrokeThickness = 14, Opacity = 0.3 };
            ring.Children.Add(zones[i]);
        }
        ring.Children.Add(arc);

        name = Tb(52, Brushes.White); countdown = Tb(38, Brushes.White);
        next = Tb(20, Brushes.Gray);

        best = Tb(20, Brushes.White);
        best.FontWeight = FontWeights.SemiBold;
        bestPill = new Border
        {
            Child = best,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(18, 5, 18, 6),
            MinWidth = 200,
            Margin = new Thickness(0, 10, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(name); sp.Children.Add(countdown); sp.Children.Add(bestPill); sp.Children.Add(next);

        box.Children.Add(ring); box.Children.Add(sp);

        clock = Tb(88, Brushes.White);
        clock.FontWeight = FontWeights.Light;
        dateTb = Tb(28, new SolidColorBrush(Color.FromRgb(170, 170, 170)));
        var head = new StackPanel { Width = 700 };
        head.Children.Add(clock); head.Children.Add(dateTb);
        Canvas.SetLeft(head, cx - 350);
        Canvas.SetTop(head, cy - 200 - 215);

        root.Children.Add(head);
        root.Children.Add(box);
        Content = root;

        MouseMove += (_, e) =>
        {
            var p = e.GetPosition(this);
            if (last is null) { last = p; return; }
            if ((p - last.Value).Length > 8) Application.Current.Shutdown();
        };
        MouseDown += (_, _) => Application.Current.Shutdown();
        KeyDown += (_, _) => Application.Current.Shutdown();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Tick();
        Loaded += async (_, _) => { timer.Start(); await Load(); };
    }

    async Task Load()
    {
        var cfg = Store.LoadConfig();
        var days = new Dictionary<DateTime, Dictionary<string, DateTime>>();
        string? err = null;
        foreach (var off in new[] { -1, 0, 1, 2 })
        {
            var d = DateTime.Today.AddDays(off);
            try { days[d] = await Store.LoadDay(d, cfg); }
            catch (Exception ex) { if (off == 0) err = ex.Message; }
        }
        error = err;
        wins = Store.BuildWindows(days);
    }

    static Geometry ArcGeo(double from, double to)
    {
        double r = 200; var c = new Point(230, 230);
        Point P(double f) => new(c.X + r * Math.Sin(f * 2 * Math.PI), c.Y - r * Math.Cos(f * 2 * Math.PI));
        to = Math.Min(to, from + 0.9999);
        var fig = new PathFigure { StartPoint = P(from) };
        fig.Segments.Add(new ArcSegment(P(to), new Size(r, r), 0, to - from > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { fig });
    }

    void DrawArc(double from, double to) => arc.Data = ArcGeo(from, to);

    void SetZones(double gf, double rf)
    {
        var cols = new[] { Green, Orange, Red };
        var b = new[] { 0.0, gf, rf, 1.0 };
        for (int i = 0; i < 3; i++)
        {
            zones[i].Stroke = new SolidColorBrush(cols[i]);
            zones[i].Data = b[i + 1] - b[i] > 0.001 ? ArcGeo(b[i], b[i + 1]) : null;
        }
    }

    void HideZones() { foreach (var z in zones) z.Data = null; }

    void SetBest(string text, Color c)
    {
        best.Text = text;
        best.Foreground = new SolidColorBrush(c);
        bestPill.Background = new SolidColorBrush(Color.FromArgb(40, c.R, c.G, c.B));
        bestPill.BorderBrush = new SolidColorBrush(Color.FromArgb(140, c.R, c.G, c.B));
        bestPill.Visibility = Visibility.Visible;
    }

    void HideBest() => bestPill.Visibility = Visibility.Collapsed;

    void Tick()
    {
        var now = DateTime.Now;
        clock.Text = now.ToString("HH:mm");
        dateTb.Text = now.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);

        if (wins is null)
        {
            spin = (spin + 0.012) % 1.0;
            arc.Stroke = new SolidColorBrush(Color.FromRgb(90, 90, 90));
            HideZones(); HideBest();
            DrawArc(spin, spin + 0.25);
            name.Text = "Loading…"; countdown.Text = ""; next.Text = "";
            return;
        }

        var cur = wins.FirstOrDefault(w => w.Start <= now && now < w.End);
        double t; DateTime target; Color col;

        if (cur != null)
        {
            double total = (cur.End - cur.Start).TotalSeconds;
            t = (now - cur.Start).TotalSeconds / total;
            SetZones((cur.GreenEnd - cur.Start).TotalSeconds / total,
                     (cur.RedStart - cur.Start).TotalSeconds / total);
            target = cur.End;
            name.Text = cur.Name;
            next.Text = $"until {EndLabel[cur.Name]} {cur.End:HH:mm}";

            if (now < cur.GreenEnd)
            {
                col = Green;
                SetBest($"Best time · {FmtShort(cur.GreenEnd - now)}", Green);
            }
            else if (now < cur.RedStart)
            {
                col = Orange;
                SetBest("Best time passed", Orange);
            }
            else
            {
                col = Red;
                SetBest("Last minutes", Red);
            }
        }
        else
        {
            var nxt = wins.FirstOrDefault(w => w.Start > now);
            var prev = wins.LastOrDefault(w => w.End <= now);
            HideBest();
            if (nxt is null || prev is null)
            {
                name.Text = "No prayer times"; countdown.Text = "";
                next.Text = error ?? "Check config.json / network";
                arc.Data = null; HideZones();
                return;
            }
            t = (now - prev.End) / (nxt.Start - prev.End);
            target = nxt.Start;
            col = Color.FromRgb(120, 120, 120);
            HideZones();
            name.Text = $"Next: {nxt.Name}";
            next.Text = $"starts {nxt.Start:HH:mm}";
        }

        t = Math.Clamp(t, 0, 1);
        shown += (t - shown) * 0.06;
        arc.Stroke = new SolidColorBrush(col);
        DrawArc(0, Math.Max(shown, 0.002));
        countdown.Text = Fmt(target - now);
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var mode = args.Length > 0 && args[0].Length >= 2 ? args[0].Substring(0, 2).ToLowerInvariant() : "/c";
        if (mode == "/p") return;
        if (mode == "/s")
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new SaverWindow());
            return;
        }
        Store.LoadConfig();
        Process.Start(new ProcessStartInfo("notepad.exe", Store.ConfigPath) { UseShellExecute = true });
    }
}
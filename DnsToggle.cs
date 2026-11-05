// DnsToggle: switches Windows' built-in DNS-over-HTTPS (DoH) on and off.
//
// Normal DNS is plain text on UDP port 53, so a router can read and answer it.
// DoH sends the same question inside HTTPS on TCP port 443, so the router only
// sees an encrypted connection to 1.1.1.1 and can't answer in its place.
//
// The app is a thin wrapper around the PowerShell scripts below. Run any of
// them by hand in an admin PowerShell window to see what they do.
//
// Usage: DnsToggle.exe                  opens the window
//        DnsToggle.exe --on|--off|--test|--status   no window, writes DnsToggle.log

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

static class Scripts
{
    // Tells Windows to use HTTPS for these servers and never to fall back to
    // plain DNS, then points every physical adapter at them. IPv6 servers are
    // set too, otherwise the router's IPv6 DNS server would stay on the list.
    public const string On = @"
$ErrorActionPreference = 'Stop'
$template = 'https://cloudflare-dns.com/dns-query'
$servers = '1.1.1.1','1.0.0.1','2606:4700:4700::1111','2606:4700:4700::1001'
foreach ($ip in $servers) {
    Set-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate $template -AutoUpgrade $true -AllowFallbackToUdp $false | Out-Null
    Write-Output ""Encrypted DNS enabled for $ip""
}
Get-NetAdapter -Physical | ForEach-Object {
    Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses $servers
    Write-Output ""Adapter '$($_.Name)' now uses Cloudflare DNS""
}
Clear-DnsClientCache
Write-Output 'DNS cache cleared'
";

    // Puts adapters back on the DNS servers the router hands out.
    public const string Off = @"
$ErrorActionPreference = 'Stop'
Get-NetAdapter -Physical | ForEach-Object {
    Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses
    Write-Output ""Adapter '$($_.Name)' back to automatic DNS""
}
$template = 'https://cloudflare-dns.com/dns-query'
foreach ($ip in '1.1.1.1','1.0.0.1','2606:4700:4700::1111','2606:4700:4700::1001') {
    Set-DnsClientDohServerAddress -ServerAddress $ip -DohTemplate $template -AutoUpgrade $false -AllowFallbackToUdp $false | Out-Null
}
Write-Output 'Encrypted DNS turned off'
Clear-DnsClientCache
Write-Output 'DNS cache cleared'
";

    public const string Status = @"
$doh = (Get-DnsClientDohServerAddress -ServerAddress '1.1.1.1').AutoUpgrade
$active = Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object {
    (Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4).ServerAddresses
}
if ($doh -and ($active -contains '1.1.1.1')) { 'ON' } else { 'OFF' }
";

    // Looks up a few well-known names. An open TCP 443 connection to
    // 1.1.1.1 afterwards is the sign that DNS really is travelling over HTTPS.
    public const string Test = @"
Clear-DnsClientCache
foreach ($name in 'example.com','wikipedia.org','github.com','microsoft.com') {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $ip = Resolve-DnsName $name -Type A -DnsOnly -QuickTimeout -ErrorAction Stop |
              Where-Object IPAddress | Select-Object -First 1 -ExpandProperty IPAddress
        Write-Output (""OK    {0,-28} {1,5} ms  {2}"" -f $name, $sw.ElapsedMilliseconds, $ip)
    } catch {
        Write-Output (""FAIL  {0,-28} {1,5} ms"" -f $name, $sw.ElapsedMilliseconds)
    }
}
$https = Get-NetTCPConnection -RemotePort 443 -ErrorAction SilentlyContinue |
         Where-Object { $_.RemoteAddress -in '1.1.1.1','1.0.0.1' -and $_.State -eq 'Established' }
if ($https) { 'DoH connection to Cloudflare: yes' } else { 'DoH connection to Cloudflare: none open' }
";
}

static class Runner
{
    public static Task<string> RunAsync(string script)
    {
        return Task.Run(() => Run(script));
    }

    static string Run(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var info = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;

        using (var process = Process.Start(info))
        {
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var text = output.Trim();
            // PowerShell writes a progress blob (#< CLIXML) to stderr; only real errors matter.
            if (process.ExitCode != 0)
                text += Environment.NewLine + "ERROR: " + error.Result.Trim();
            return text;
        }
    }
}

class MainForm : Form
{
    readonly Label status = new Label();
    readonly Button toggle = new Button();
    readonly Button test = new Button();
    readonly TextBox log = new TextBox();
    bool isOn;

    public MainForm()
    {
        Text = "DNS Toggle";
        ClientSize = new Size(560, 400);
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f);

        status.Dock = DockStyle.Top;
        status.Height = 56;
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
        status.ForeColor = Color.White;
        status.BackColor = Color.Gray;
        status.Text = "Checking...";

        var buttons = new TableLayoutPanel();
        buttons.Dock = DockStyle.Top;
        buttons.Height = 52;
        buttons.ColumnCount = 2;
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));

        toggle.Dock = DockStyle.Fill;
        toggle.Click += async (s, e) => await ToggleAsync();
        test.Dock = DockStyle.Fill;
        test.Text = "Test DNS";
        test.Click += async (s, e) => await TestAsync();
        buttons.Controls.Add(toggle, 0, 0);
        buttons.Controls.Add(test, 1, 0);

        log.Dock = DockStyle.Fill;
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.Font = new Font("Consolas", 9.5f);

        Controls.Add(log);
        Controls.Add(buttons);
        Controls.Add(status);

        Shown += async (s, e) => await RefreshStatusAsync();
    }

    void SetBusy(bool busy)
    {
        toggle.Enabled = !busy;
        test.Enabled = !busy;
    }

    void Write(string text)
    {
        log.AppendText(text + Environment.NewLine + Environment.NewLine);
    }

    async Task RefreshStatusAsync()
    {
        isOn = (await Runner.RunAsync(Scripts.Status)).Trim() == "ON";
        status.Text = isOn ? "Encrypted DNS: ON" : "Encrypted DNS: OFF (using router)";
        status.BackColor = isOn ? Color.SeaGreen : Color.DimGray;
        toggle.Text = isOn ? "Turn OFF" : "Turn ON";
    }

    async Task ToggleAsync()
    {
        SetBusy(true);
        Write(await Runner.RunAsync(isOn ? Scripts.Off : Scripts.On));
        await RefreshStatusAsync();
        SetBusy(false);
    }

    async Task TestAsync()
    {
        SetBusy(true);
        Write("Testing, up to about 30 seconds if lookups are timing out...");
        Write(await Runner.RunAsync(Scripts.Test));
        SetBusy(false);
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            RunCommand(args[0]);
            return;
        }
        Application.EnableVisualStyles();
        Application.Run(new MainForm());
    }

    static void RunCommand(string command)
    {
        string script;
        switch (command)
        {
            case "--on": script = Scripts.On; break;
            case "--off": script = Scripts.Off; break;
            case "--test": script = Scripts.Test; break;
            case "--status": script = Scripts.Status; break;
            default: script = "'Unknown option: use --on, --off, --test or --status'"; break;
        }
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DnsToggle.log");
        File.AppendAllText(logPath, "[" + command + "]" + Environment.NewLine
            + Runner.RunAsync(script).Result + Environment.NewLine);
    }
}

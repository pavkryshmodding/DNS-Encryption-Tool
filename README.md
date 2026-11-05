# DnsToggle

A one-button Windows app that switches Windows' built-in **DNS over HTTPS (DoH)** on and off,
using Cloudflare's resolvers (1.1.1.1 / 1.0.0.1).

## Why

Normal DNS is plain text on UDP port 53, so your router (or anything between you and the
DNS server) can read the lookups and even answer them itself. DoH sends the same questions
inside HTTPS on TCP port 443. The router only sees an encrypted connection to 1.1.1.1 and
can't answer in Cloudflare's place.

## What it does

- **Turn ON:** registers Cloudflare's IPv4 and IPv6 servers as DoH servers with no fallback
  to plain DNS, points every physical network adapter at them, and clears the DNS cache.
- **Turn OFF:** puts the adapters back on the DNS servers the router hands out, turns DoH
  auto-upgrade off, and clears the cache.
- **Test DNS:** looks up a few domains, shows how long each lookup took, and checks for an
  open HTTPS connection to Cloudflare, which shows that DNS really is going over HTTPS.

The app is a thin wrapper around short PowerShell scripts (see the `Scripts` class in
`DnsToggle.cs`). You can run any of them by hand in an admin PowerShell window to see
exactly what they do.

## Build

A prebuilt `DnsToggle.exe` is included, along with `DnsToggle.log` from a sample run.
To build it yourself, no SDK is needed. It uses the C# compiler that ships with the .NET Framework on every
Windows install:

```
build.cmd
```

This produces `DnsToggle.exe` in the same folder.

## Usage

Double-click `DnsToggle.exe`. It asks for administrator rights, because changing DNS
settings needs them.

It can also run without a window. Results are appended to `DnsToggle.log` next to the exe:

```
DnsToggle.exe --on
DnsToggle.exe --off
DnsToggle.exe --test
DnsToggle.exe --status
```

## Requirements

Windows 11 (or Windows Server 2022+). Built-in DoH support isn't available on Windows 10.

## License

[MIT](LICENSE)

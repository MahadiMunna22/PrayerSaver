<h1 align="center">PrayerSaver</h1>

<p align="center"><b>Never miss a prayer again.</b><br/>
A Windows screensaver that turns idle screen time into a gentle prayer reminder.</p>

<p align="center">
  <img src="./screenshots/SS1.png" width="24%" alt="Screenshot 1"/>
  <img src="./screenshots/SS2.png" width="24%" alt="Screenshot 2"/>
  <img src="./screenshots/SS3.png" width="24%" alt="Screenshot 3"/>
  <img src="./screenshots/SS4.png" width="24%" alt="Screenshot 4"/>
</p>

## Why PrayerSaver?

Work, study and games make it easy to lose track of time. PrayerSaver uses the moment your PC goes idle to show you where you are in the day:

- **Which prayer is current**, and how long its window lasts
- **How much of the best time is left**, so you know whether to pray now or soon
- **A calm colour cue** from a glance: green means plenty of time, orange means do not delay, red means the window is closing
- **What is next**, with a countdown, when you are between prayers

Walk away from your desk, come back, and you already know what to do next.

## Features

- Live progress ring with green, orange and red zones
- Large clock and date, plus "best time" and window countdowns
- Prayer times from the [AlAdhan API](https://aladhan.com/prayer-times-api), cached daily for offline use
- Native Windows screensaver (`.scr`) built with C# and WPF
- Exits on any key, click or larger mouse movement

## How it reads your day

| Prayer | Window | Green (best time) | Red (last minutes) |
|---|---|---|---|
| Fajr | Fajr to sunrise | First half | Last 20 min |
| Dhuhr | Dhuhr to Asr | First half | Last 30 min |
| Asr | Asr to sunset | Until 40 min before sunset | Last 15 min |
| Maghrib | Maghrib to Isha | First 20 min | Last 20 min |
| Isha | Isha to Islamic midnight | Until first third of the night | Last 30 min |

Orange covers the time between green and red. Between windows the ring is grey and counts down to the next prayer. These are practical defaults, not a fatwa, and scholars differ. Adjust them in `Store.BuildWindows` (`Program.cs`), and always verify times with your local mosque.

## Get started

**Requirements:** Windows 10/11 (x64) with the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows, macOS or Linux.

**1. Build**

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
cp bin/Release/net10.0-windows/win-x64/publish/PrayerSaver.exe PrayerSaver.scr
```

On Windows, use `copy` instead of `cp`. For a build with no runtime dependency, use `--self-contained true -p:IncludeNativeLibrariesForSelfExtract=true`.

**2. Install**

1. Copy `PrayerSaver.scr` to a permanent folder, e.g. `C:\PrayerSaver\`.
2. Test it: `.\PrayerSaver.scr /s`
3. Right-click the file and choose **Install**, or pick it in **Settings > Personalization > Lock screen > Screen saver**.
4. Set **Wait** to the idle time you want.

**3. Set your city**

Run `PrayerSaver.scr` without arguments to open `%AppData%\PrayerSaver\config.json`:

```json
{ "City": "Bern", "Country": "CH", "Method": 3 }
```

`Method` is an [AlAdhan calculation method](https://aladhan.com/calculation-methods) ID (e.g. `2` ISNA, `3` MWL, `4` Umm al-Qura, `13` Diyanet). Keys are case-sensitive. Delete cached `*.json` files in the same folder after changing the method.

## Good to know

- It starts after an idle timeout on the unlocked desktop. It does not replace the lock screen and will not show after `Win + L`.
- The settings preview (`/p`) is blank, and the UI appears on the primary monitor only.

## Troubleshooting

- **Never starts from idle:** run `powercfg /requests` as admin. A process holding a `DISPLAY` or `SYSTEM` request (e.g. Intel `ipf_helper.exe`) blocks idle detection.
- **Screen turns off first:** set **Screen off** longer than the screensaver **Wait** in Power settings.
- **Nothing appears:** run `PrayerSaver.scr /s` in a terminal and confirm the .NET Desktop Runtime is installed.
- **`NETSDK1136`:** the target framework must end in `-windows`.

## Security

`.scr` files are executables. Only install ones you built or trust.

## License

Add a `LICENSE` file (e.g. MIT) and name it here.

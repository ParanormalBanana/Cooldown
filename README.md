# Cooldown

Stay uninstalled on purpose.

Cooldown is a Windows library that puts games on a cooldown: they uninstall at the next boot (or after 05:00 the next day if the PC stayed on). One action, a Win98-looking grid, and a rank for sticking with it.

Windows 10/11, 64-bit. No .NET install required.

## Download

Get the latest installer or zip from [Releases](https://github.com/ParanormalBanana/Cooldown/releases).

- **CooldownSetup-x.y.z.exe** — per-user install, no admin. Uninstall from Settings → Apps.
- **Cooldown-x.y.z-win-x64.zip** — portable. Run `Cooldown.exe`.

Windows SmartScreen may warn on the first run (the build is not code-signed yet). Choose **More info** → **Run anyway**.

Uninstalling Cooldown does not delete `%AppData%\Cooldown` (your library, cooldowns, and rank).

## Build from source

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Output is `dist\Cooldown\Cooldown.exe`. If [Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed, that also produces `dist\CooldownSetup-<version>.exe`.

## License

[MIT](LICENSE)

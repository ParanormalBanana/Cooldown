# Cooldown

I created this program for gaming addicts who want o stay off their games.

Basically, you put a game on cooldown, and it get auto-uninstalled from your PC right away. If you still want to play the game, you need to download it again, which makes it more difficult and adds time which you can use to think about doing something productive instead.

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

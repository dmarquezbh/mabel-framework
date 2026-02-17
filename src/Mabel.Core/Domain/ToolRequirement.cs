namespace Mabel.Core.Domain;

public record ToolRequirement(string Name, string Description, Platform Platforms, string InstallHint);

public static class KnownTools
{
    public static readonly ToolRequirement[] All =
    [
        // Shared
        new("dotnet",      ".NET SDK",          Platform.All,     "mabel setup"),
        new("git",         "Version Control",   Platform.All,     "sudo apt install git"),
        new("curl",        "HTTP Client",       Platform.All,     "sudo apt install curl"),
        new("wasmtime",    "WASM/WASI Runtime", Platform.All,     "curl https://wasmtime.dev/install.sh -sSf | bash"),

        // iOS
        new("swift",       "Swift Toolchain",   Platform.Ios,     "mabel setup"),
        new("xtool",       "iOS Build Tool",    Platform.Ios,     "mabel setup"),
        new("usbmuxd",     "iOS USB Daemon",    Platform.Ios,     "sudo apt install usbmuxd"),
        new("ideviceinfo", "libimobiledevice",  Platform.Ios,     "sudo apt install libimobiledevice-utils"),

        // Android
        new("adb",         "Android Debug Bridge", Platform.Android, "sudo apt install adb"),
    ];
}

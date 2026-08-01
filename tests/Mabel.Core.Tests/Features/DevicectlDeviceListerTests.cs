using Mabel.Core.Features.Devices;
using Xunit;

namespace Mabel.Core.Tests.Features;

public class DevicectlDeviceListerTests
{
    // Fixture reduzido do JSON real de `xcrun devicectl list devices --json-output`,
    // capturado contra um iPhone XS Max fisico (test-device-1, iOS 18.7.9) via USB —
    // mantem so os campos que ParseDevicesJson le.
    private const string RealDevicectlJson =
        """
        {
          "info": { "jsonVersion": 3, "outcome": "success" },
          "result": {
            "devices": [
              {
                "hardwareProperties": {
                  "udid": "[UDID-REDACTED]",
                  "platform": "iOS",
                  "marketingName": "iPhone XS Max",
                  "productType": "iPhone11,6"
                },
                "deviceProperties": {
                  "name": "test-device-1",
                  "osVersionNumber": "18.7.9"
                },
                "connectionProperties": {
                  "pairingState": "paired",
                  "tunnelState": "connected"
                }
              },
              {
                "hardwareProperties": {
                  "udid": "00008122-001C05903A40001C",
                  "platform": "macOS",
                  "marketingName": "MacBook Pro"
                },
                "deviceProperties": {
                  "name": "Daniel's MacBook Pro"
                }
              }
            ]
          }
        }
        """;

    [Fact]
    public void ParseDevicesJson_ExtractsIosDeviceFields()
    {
        var devices = DevicectlDeviceLister.ParseDevicesJson(RealDevicectlJson);

        var device = Assert.Single(devices);
        Assert.Equal("[UDID-REDACTED]", device.Id);
        Assert.Equal("test-device-1", device.Name);
        Assert.Equal("iPhone XS Max", device.Model);
        Assert.Equal("18.7.9", device.OsVersion);
        Assert.Equal("iOS", device.Platform);
    }

    [Fact]
    public void ParseDevicesJson_ExcludesNonIosDevices()
    {
        // O JSON de devicectl inclui "My Mac" (platform: macOS) — nao deve
        // aparecer numa listagem de devices iOS.
        var devices = DevicectlDeviceLister.ParseDevicesJson(RealDevicectlJson);

        Assert.DoesNotContain(devices, d => d.Model == "MacBook Pro");
    }

    [Fact]
    public void ParseDevicesJson_EmptyDeviceList_ReturnsEmpty()
    {
        var devices = DevicectlDeviceLister.ParseDevicesJson(
            """{ "result": { "devices": [] } }""");

        Assert.Empty(devices);
    }

    [Fact]
    public void ParseDevicesJson_MissingResultKey_ReturnsEmpty()
    {
        var devices = DevicectlDeviceLister.ParseDevicesJson("""{ "info": {} }""");

        Assert.Empty(devices);
    }
}

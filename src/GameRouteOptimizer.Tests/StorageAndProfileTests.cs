using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Platform;
using GameRouteOptimizer.Core.Profiles;
using GameRouteOptimizer.Core.Relay;
using GameRouteOptimizer.Core.Services;
using GameRouteOptimizer.Core.Storage;
using GameRouteOptimizer.Core.Tunneling;
using Xunit;

namespace GameRouteOptimizer.Tests;

public static class TestPaths
{
    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gameroute-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>ConfigStore guarda y carga configuración (round-trip + defaults).</summary>
public class ConfigStoreTests
{
    [Fact]
    public void Configuracion_GuardaYCarga()
    {
        var dir = TestPaths.TempDir();
        var store = new ConfigStore(dir);
        var settings = new AppSettings
        {
            ProbeTimeoutMs = 1234,
            SwitchCooldownSeconds = 777,
            KillSwitchEnabled = true,
            TunnelMtu = 1360,
            SafeMode = true,
            OnboardingCompleted = true
        };
        store.Save(settings);

        var loaded = new ConfigStore(dir).Load();
        Assert.Equal(1234, loaded.ProbeTimeoutMs);
        Assert.Equal(777, loaded.SwitchCooldownSeconds);
        Assert.True(loaded.KillSwitchEnabled);
        Assert.Equal(1360, loaded.TunnelMtu);
        Assert.True(loaded.SafeMode);
        Assert.True(loaded.OnboardingCompleted);
    }

    [Fact]
    public void Configuracion_DevuelveDefaultsSiNoExiste()
    {
        var store = new ConfigStore(TestPaths.TempDir());
        var settings = store.Load();
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal(2000, settings.ProbeTimeoutMs);
        Assert.False(settings.SafeMode);
    }

    [Fact]
    public void Configuracion_SobreviveArchivoCorrupto()
    {
        var dir = TestPaths.TempDir();
        var path = Path.Combine(dir, "settings.json");
        File.WriteAllText(path, "{ no es json válido");
        var settings = new ConfigStore(dir).Load();
        Assert.Equal(2000, settings.ProbeTimeoutMs);
    }
}

/// <summary>Perfiles: import/export JSON sin pérdida de datos.</summary>
public class ProfileImportExportTests
{
    [Fact]
    public void Perfil_ImportaYExportaCorrectamente()
    {
        var dir = TestPaths.TempDir();
        var manager = new ProfileManager(new DataStore(dir));

        var profile = new GameProfile
        {
            Name = "Juego de Prueba",
            Executables = ["juego.exe", "juego_dx12.exe"],
            ExecutablePath = @"C:\Games\Juego\juego.exe",
            LaunchArguments = "-fullscreen",
            PreferredProtocol = ProbeProtocol.Tcp,
            RoutingMode = RoutingMode.TunnelGameOnly,
            AutoStartOptimization = true,
            Servers =
            [
                new GameServer
                {
                    Name = "LATAM Sur",
                    Domain = "latam.juego.example",
                    Ports = [7000, 7777],
                    Region = "SA-East",
                    PreferredProtocol = ProbeProtocol.Tcp
                }
            ],
            Notes = "Perfil de ejemplo",
            BlockedRelayIds = ["abc123"]
        };

        manager.Add(profile);
        var json = manager.ExportProfileJson(profile);
        var imported = manager.ImportProfileJson(json);

        Assert.Equal(profile.Name, imported.Name);
        Assert.Equal(2, imported.Executables.Count);
        Assert.Equal(ProbeProtocol.Tcp, imported.PreferredProtocol);
        Assert.Equal(RoutingMode.TunnelGameOnly, imported.RoutingMode);
        Assert.Single(imported.Servers);
        Assert.Equal("latam.juego.example", imported.Servers[0].Domain);
        Assert.Contains(7000, imported.Servers[0].Ports);
        Assert.Equal("Perfil de ejemplo", imported.Notes);
        Assert.Contains("abc123", imported.BlockedRelayIds);
        Assert.NotEqual(profile.Id, imported.Id); // nuevo id al importar
    }

    [Fact]
    public void Perfil_RechazaInvalidos()
    {
        var manager = new ProfileManager(new DataStore(TestPaths.TempDir()));
        var invalid = new GameProfile { Name = "", Servers = [] };
        Assert.Throws<ArgumentException>(() => manager.Add(invalid));
    }

    [Fact]
    public void Perfil_PersisteEntreInstancias()
    {
        var dir = TestPaths.TempDir();
        var manager1 = new ProfileManager(new DataStore(dir));
        manager1.Add(new GameProfile { Name = "Persistente", Executables = ["x.exe"], Servers = [new GameServer { Name = "s", Ip = "1.2.3.4" }] });

        var manager2 = new ProfileManager(new DataStore(dir));
        Assert.Single(manager2.Profiles);
        Assert.Equal("Persistente", manager2.Profiles[0].Name);
    }
}

/// <summary>Relays: import/export JSON, importación WG y sin secretos en claro.</summary>
public class RelayImportExportTests
{
    private const string ValidConf = """
        [Interface]
        PrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=
        Address = 10.66.66.2/32
        DNS = 1.1.1.1
        MTU = 1380

        [Peer]
        PublicKey = 9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=
        AllowedIPs = 10.66.66.0/24, 203.0.113.10/32
        Endpoint = relay-ejemplo.example.com:51820
        PersistentKeepalive = 25
        """;

    [Fact]
    public void Relay_ImportaYExportaCorrectamente()
    {
        var dir = TestPaths.TempDir();
        var manager = new RelayManager(new DataStore(dir), new SecretProtector(dir));

        var relay = new RelayNode
        {
            Name = "Relay LATAM",
            Provider = "Mi infraestructura",
            Country = "CL",
            City = "Santiago",
            EndpointHost = "relay1.example.com",
            EndpointPort = 51820,
            PublicKey = "9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=",
            AllowedIps = ["10.66.66.0/24"],
            RelayToGameMs = 12,
            Priority = 50
        };
        manager.Add(relay);

        var json = manager.ExportRelaysJson();
        var count = manager.ImportRelaysJson(json);
        Assert.Equal(1, count);
        var imported = manager.Relays.Last();
        Assert.Equal("Relay LATAM", imported.Name);
        Assert.Equal("relay1.example.com", imported.EndpointHost);
        Assert.Equal(51820, imported.EndpointPort);
        Assert.Equal(12.0, (double)imported.RelayToGameMs!.Value);
        Assert.Equal("CL", imported.Country);
    }

    [Fact]
    public void WireGuardImport_CreaRelayYCifraLaClavePrivada()
    {
        var dir = TestPaths.TempDir();
        var manager = new RelayManager(new DataStore(dir), new SecretProtector(dir));

        var relay = manager.ImportWireGuardConfig(ValidConf, "Relay importado");

        Assert.Equal("Relay importado", relay.Name);
        Assert.Equal("relay-ejemplo.example.com", relay.EndpointHost);
        Assert.Equal(51820, relay.EndpointPort);
        Assert.Equal("9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=", relay.PublicKey);
        Assert.Contains("203.0.113.10/32", relay.AllowedIps);
        Assert.False(string.IsNullOrWhiteSpace(relay.PrivateKeyRef));

        // La clave privada NO debe aparecer en claro en ningún archivo del directorio de datos.
        foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=", content);
        }

        // Y debe poder recuperarse descifrada para construir el túnel.
        Assert.Equal("EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=", manager.GetDecryptedPrivateKey(relay));
    }

    [Fact]
    public void WireGuardImport_RechazaConfInvalida()
    {
        var manager = new RelayManager(new DataStore(TestPaths.TempDir()), new SecretProtector(TestPaths.TempDir()));
        Assert.ThrowsAny<Exception>(() => manager.ImportWireGuardConfig("[Interface]\nPrivateKey = no\n"));
    }

    [Fact]
    public void Relay_ExportSinSecretos()
    {
        var dir = TestPaths.TempDir();
        var manager = new RelayManager(new DataStore(dir), new SecretProtector(dir));
        var relay = manager.ImportWireGuardConfig(ValidConf);

        var export = manager.ExportRelaysJson(includeSecretRefs: false);
        Assert.DoesNotContain(relay.PrivateKeyRef, export);

        var exportWithSecrets = manager.ExportRelaysJson(includeSecretRefs: true);
        Assert.Contains(relay.PrivateKeyRef, exportWithSecrets);
    }
}

/// <summary>Claves WireGuard y validaciones base64.</summary>
public class WireGuardKeyTests
{
    [Fact]
    public void ClaveValida_AceptaBase64De32Bytes()
    {
        Assert.True(WireGuardKey.IsValidPublicKey("9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk="));
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk")]
    [InlineData("!!!!no-es-base64-de-32-bytes-ni-loco-aaaaa=")]
    public void ClaveInvalida_SeRechaza(string key)
    {
        Assert.False(WireGuardKey.IsValidPublicKey(key));
    }
}

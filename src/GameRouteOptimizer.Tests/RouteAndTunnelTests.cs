using GameRouteOptimizer.Core.Models;
using GameRouteOptimizer.Core.Routing;
using GameRouteOptimizer.Core.Tunneling;
using Xunit;

namespace GameRouteOptimizer.Tests;

/// <summary>Aplicador de rutas simulado (memoria) para verificar generación y restauración.</summary>
internal sealed class FakeRouteApplier : IRouteApplier
{
    public List<RouteEntry> Routes { get; } = new()
    {
        new RouteEntry("0.0.0.0/0", "192.168.1.1", 1, "Ethernet", 25),
        new RouteEntry("10.0.0.0/8", null, 1, "Ethernet", 10)
    };

    public List<RouteEntry> Added { get; } = new();
    public List<RouteEntry> Deleted { get; } = new();

    public Task<List<RouteEntry>> GetCurrentRoutesAsync(CancellationToken ct = default)
        => Task.FromResult(Routes.ToList());

    public Task<bool> AddRouteAsync(RouteEntry entry, CancellationToken ct = default)
    {
        Added.Add(entry);
        Routes.Add(entry);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteRouteAsync(RouteEntry entry, CancellationToken ct = default)
    {
        Deleted.Add(entry);
        Routes.RemoveAll(r => r.DestinationCidr == entry.DestinationCidr && r.Metric == entry.Metric);
        return Task.FromResult(true);
    }
}

/// <summary>RouteCalculator: generación de planes y restauración del estado original.</summary>
public class RouteCalculatorTests
{
    private static GameProfile Profile(params string[] ips) => new()
    {
        Name = "Test",
        Servers = ips.Select(ip => new GameServer { Name = ip, Ip = ip }).ToList()
    };

    private static TunnelProfile Tunnel() => new()
    {
        Name = "gameroute-test",
        PrivateKey = "EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=",
        Address = "10.66.66.2/32",
        PublicKey = "9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=",
        AllowedIps = ["10.66.66.0/24"],
        Endpoint = "relay.example.com:51820",
        Mtu = 1380
    };

    [Fact]
    public async Task RouteCalculator_GeneraRutasEsperadas()
    {
        var applier = new FakeRouteApplier();
        var calc = new RouteCalculator(applier);
        var profile = Profile("203.0.113.10", "203.0.113.11");

        var plan = await calc.BuildPlanAsync(profile, Tunnel(), RoutingMode.TunnelGameOnly);

        Assert.Contains("203.0.113.10/32", plan.AddRoutes);
        Assert.Contains("203.0.113.11/32", plan.AddRoutes);
        Assert.Equal("10.66.66.2", plan.TunnelAddress);
        Assert.Equal(2, plan.AddRoutes.Count);
    }

    [Fact]
    public async Task RouteCalculator_CapturaRutasOriginalesParaRestore()
    {
        var applier = new FakeRouteApplier();
        var calc = new RouteCalculator(applier);
        var profile = Profile("10.1.2.3"); // 10.1.2.3 está cubierto por 10.0.0.0/8 existente

        var plan = await calc.BuildPlanAsync(profile, Tunnel(), RoutingMode.TunnelGameOnly);

        Assert.Contains(plan.OriginalRoutes, r => r.DestinationCidr == "10.0.0.0/8");
    }

    [Fact]
    public async Task RouteCalculator_RestauraRutasOriginales()
    {
        var applier = new FakeRouteApplier();
        var calc = new RouteCalculator(applier);
        var profile = Profile("203.0.113.10");
        var plan = await calc.BuildPlanAsync(profile, Tunnel(), RoutingMode.TunnelGameOnly);

        var applied = await calc.ApplyPlanAsync(plan);
        Assert.Equal(1, applied);
        Assert.Contains(applier.Routes, r => r.DestinationCidr == "203.0.113.10/32");

        var restored = await calc.RestorePlanAsync(plan);
        Assert.Equal(1, restored); // la ruta original 0.0.0.0/0 se re-agrega
        Assert.DoesNotContain(applier.Routes, r => r.DestinationCidr == "203.0.113.10/32" && r.Metric == 1);
        Assert.Single(applier.Deleted);
        Assert.Contains(applier.Routes, r => r.DestinationCidr == "0.0.0.0/0" && r.Metric == 25);
    }

    [Fact]
    public async Task RouteCalculator_ModoDirecto_NoGeneraRutas()
    {
        var applier = new FakeRouteApplier();
        var calc = new RouteCalculator(applier);
        var plan = await calc.BuildPlanAsync(Profile("203.0.113.10"), Tunnel(), RoutingMode.Direct);
        Assert.Empty(plan.AddRoutes);
        Assert.Equal(0, await calc.ApplyPlanAsync(plan));
    }

    [Fact]
    public async Task RouteCalculator_TunelGlobal_UsaAllowedIpsDelTunel()
    {
        var applier = new FakeRouteApplier();
        var calc = new RouteCalculator(applier);
        var plan = await calc.BuildPlanAsync(Profile("203.0.113.10"), Tunnel(), RoutingMode.TunnelGlobal);
        Assert.True(plan.GlobalTunnel);
        Assert.Contains("10.66.66.0/24", plan.AddRoutes);
    }

    [Theory]
    [InlineData("203.0.113.10/32", "203.0.113.10/32", true)]
    [InlineData("0.0.0.0/0", "203.0.113.10/32", true)]
    [InlineData("10.0.0.0/8", "10.1.2.3/32", true)]
    [InlineData("10.0.0.0/8", "11.1.2.3/32", false)]
    [InlineData("10.0.0.0/32", "10.0.0.1/32", false)]
    public void Covers_CubrePrefijosCorrectamente(string route, string target, bool expected)
    {
        Assert.Equal(expected, RouteCalculator.Covers(route, target));
    }

    [Fact]
    public void ExtractTunnelAddress_ExtraeIpv4()
    {
        Assert.Equal("10.66.66.2", RouteCalculator.ExtractTunnelAddress("10.66.66.2/32, fd42:42:42::2/128"));
        Assert.Null(RouteCalculator.ExtractTunnelAddress(""));
    }
}

/// <summary>Parser/validador de configuraciones WireGuard.</summary>
public class WireGuardConfigParserTests
{
    private const string GoodConf = """
        [Interface]
        PrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=
        Address = 10.66.66.2/32
        DNS = 1.1.1.1
        MTU = 1380

        [Peer]
        PublicKey = 9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=
        AllowedIPs = 0.0.0.0/0
        Endpoint = relay.example.com:51820
        PersistentKeepalive = 25
        """;

    [Fact]
    public void Parser_ParseaConfValida()
    {
        var tunnel = WireGuardConfigParser.Parse(GoodConf);
        Assert.Equal("EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=", tunnel.PrivateKey);
        Assert.Equal("10.66.66.2/32", tunnel.Address);
        Assert.Equal(1380, tunnel.Mtu);
        Assert.Equal("1.1.1.1", tunnel.Dns);
        Assert.Equal("relay.example.com:51820", tunnel.Endpoint);
        Assert.Equal(25, tunnel.PersistentKeepalive);
        Assert.Equal(["0.0.0.0/0"], tunnel.AllowedIps);
    }

    [Fact]
    public void Parser_ValidaConfValida_SinErrores()
    {
        var tunnel = WireGuardConfigParser.Parse(GoodConf);
        Assert.Empty(WireGuardConfigParser.Validate(tunnel));
    }

    [Theory]
    [InlineData("")] // vacía
    [InlineData("[Interface]\nPrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=\n")] // sin Peer
    [InlineData("[Interface]\nPrivateKey = corta\n[Peer]\nPublicKey = 9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=\nAllowedIPs = 0.0.0.0/0\nEndpoint = x:1\n")] // clave corta
    [InlineData("[Interface]\nPrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=\nMTU = 900\n[Peer]\nPublicKey = 9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=\nAllowedIPs = 0.0.0.0/0\nEndpoint = x:1\n")] // MTU bajo
    [InlineData("[Interface]\nPrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=\n[Peer]\nPublicKey = 9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=\nAllowedIPs = 999.0.0.0/8\nEndpoint = x:1\n")] // CIDR inválido
    [InlineData("[Interface]\nPrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=\n[Peer]\nPublicKey = 9lRfF3H0vGhIkQ8WcD6sJzXhN2K1qYtO5pLwVbE4mCk=\nAllowedIPs = 0.0.0.0/0\nEndpoint = sinpuerto\n")] // endpoint sin puerto
    public void Parser_RechazaConfsInvalidas(string conf)
    {
        TunnelProfile? tunnel = null;
        try
        {
            tunnel = WireGuardConfigParser.Parse(conf);
        }
        catch (WireGuardConfigException)
        {
            return; // rechazada en parseo: correcto
        }
        Assert.NotEmpty(WireGuardConfigParser.Validate(tunnel!)); // o rechazada en validación
    }

    [Fact]
    public void ToConfString_EnmascaraLaClavePrivada()
    {
        var tunnel = WireGuardConfigParser.Parse(GoodConf);
        var text = tunnel.ToConfString(includePrivateKey: false);
        Assert.DoesNotContain("EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=", text);
        Assert.Contains("***", text);
    }

    [Fact]
    public void Parser_DetectaSeccionDesconocida()
    {
        Assert.Throws<WireGuardConfigException>(() =>
            WireGuardConfigParser.Parse("[Interface]\nPrivateKey = EAtQZ5wY5dC7x0KKkGzXTLpSfHIXaW0mFoLxKfLpeHs=\n\n[Foo]\nBar = 1"));
    }
}

/// <summary>WireGuardTunnelManager: validación antes de aplicar y parser de `wg show`.</summary>
public class TunnelManagerTests
{
    [Fact]
    public async Task TunnelManager_RechazaConfInvalidaAntesDeAplicar()
    {
        var manager = new WireGuardTunnelManager(confDirectory: TestPaths.TempDir());
        var bad = new TunnelStartRequest
        {
            Tunnel = new TunnelProfile { PrivateKey = "invalida", Address = "10.66.66.2/32" },
            TunnelName = "bad-tunnel"
        };
        var status = await manager.StartAsync(bad);
        Assert.Equal(TunnelState.Failed, status.State);
        Assert.Contains("inválida", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WgShowParser_ExtraeEstadoYBytes()
    {
        var output = """
            interface: gameroute-0
              public key: AbCdEf123456=
              endpoint: 203.0.113.7:51820
              allowed ips: 10.66.66.0/24
              latest handshake: 5 seconds ago
              transfer: 1.2 MiB received, 800 KiB sent
            """;
        var status = WireGuardTunnelManager.ParseWgShow(output, new TunnelStatus
        {
            State = TunnelState.Connecting,
            TunnelName = "gameroute-0"
        });

        Assert.Equal(TunnelState.Connected, status.State);
        Assert.Equal("203.0.113.7:51820", status.Endpoint);
        Assert.Equal(5, status.SecondsSinceHandshake);
        Assert.Equal((long)1258291, status.ReceivedBytes);
        Assert.Equal(800 * 1024, status.SentBytes);
    }

    [Fact]
    public void WgShowParser_HandshakeViejo_MarcaReconnecting()
    {
        var output = "interface: gameroute-0\n  latest handshake: 5 minutes, 2 seconds ago\n";
        var status = WireGuardTunnelManager.ParseWgShow(output, new TunnelStatus { State = TunnelState.Connected });
        Assert.Equal(TunnelState.Reconnecting, status.State);
    }
}

/// <summary>Kill switch con executor simulado: activar/desactivar limpio.</summary>
public class KillSwitchTests
{
    private sealed class FakeFirewall : IFirewallExecutor
    {
        public Dictionary<string, (string? Program, string Direction, string Action)> Rules { get; } = new();

        public Task<bool> AddRuleAsync(string name, string? programPath, string direction, string action, CancellationToken ct = default)
        {
            Rules[name] = (programPath, direction, action);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteRuleAsync(string name, CancellationToken ct = default)
        {
            Rules.Remove(name);
            return Task.FromResult(true);
        }

        public Task<bool> RuleExistsAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Rules.ContainsKey(name));
    }

    [Fact]
    public async Task KillSwitch_SeActivaYSeDesactiva()
    {
        var wgPath = Path.Combine(TestPaths.TempDir(), "wireguard.exe");
        var svcPath = Path.Combine(TestPaths.TempDir(), "GameRouteOptimizer.Service.exe");
        File.WriteAllBytes(wgPath, [1]);
        File.WriteAllBytes(svcPath, [1]);

        var firewall = new FakeFirewall();
        var ks = new KillSwitchManager(firewall, () => wgPath, () => svcPath);

        await ks.EnableAsync();
        Assert.True(firewall.Rules.ContainsKey(KillSwitchManager.BlockRuleName));
        Assert.Equal("block", firewall.Rules[KillSwitchManager.BlockRuleName].Action);
        Assert.True(firewall.Rules.ContainsKey(KillSwitchManager.AllowWireGuardRule));
        Assert.Equal("allow", firewall.Rules[KillSwitchManager.AllowWireGuardRule].Action);
        Assert.True(ks.IsActive);

        await ks.DisableAsync();
        Assert.False(firewall.Rules.ContainsKey(KillSwitchManager.BlockRuleName));
        Assert.False(firewall.Rules.ContainsKey(KillSwitchManager.AllowWireGuardRule));
        Assert.False(ks.IsActive);
    }
}

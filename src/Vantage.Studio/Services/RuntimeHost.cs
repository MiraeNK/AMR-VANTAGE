using System.Collections.Concurrent;
using Vantage.Core.Clearance;
using Vantage.Fleet;
using Vantage.Fleet.Contracts;
using Vantage.Fleet.Simulation;
using Vantage.Fleet.Transport;

namespace Vantage.Studio.Services;

public enum TransportKind { Mqtt, InProcess }

/// <summary>
/// Lifecycle of broker, FMR engine and the simulation loop. The loop runs on a
/// background task; the UI only reads snapshots and drains the event queues.
/// </summary>
public sealed class RuntimeHost : IAsyncDisposable
{
    private CancellationTokenSource? _loop;
    private readonly object _engineGate = new();

    public EmbeddedBroker Broker { get; } = new(1883);
    public string BrokerHost { get; } = "127.0.0.1";
    public string? BrokerFault { get; private set; }
    public SimulationEngine? Engine { get; private set; }
    public TransportKind Transport { get; private set; }
    public double Speed { get; set; } = 1.0;
    public bool IsRunning => _loop is not null;

    public ConcurrentQueue<JournalEntry> JournalQueue { get; } = new();
    public ConcurrentQueue<TraceEntry> TraceQueue { get; } = new();

    public async Task StartBrokerAsync()
    {
        try
        {
            await Broker.StartAsync();
            BrokerFault = null;
        }
        catch (Exception ex)
        {
            // Port taken: assume an external broker (e.g. Mosquitto) is already serving 1883.
            BrokerFault = ex.Message;
        }
    }

    public async Task LoadAsync(ScenarioDefinition? scenario, EngineOptions options, TransportKind transport)
    {
        Pause();
        var old = Engine;
        Engine = null;
        if (old is not null) await old.DisposeAsync();

        Transport = scenario is null ? TransportKind.Mqtt : transport;
        var hub = new InProcessHub();
        string run = Guid.NewGuid().ToString("N")[..6];

        Func<string, Task<IMessageBus>> connect = Transport == TransportKind.InProcess
            ? id => Task.FromResult(hub.Connect(id))
            : async id =>
            {
                var bus = new MqttBus($"{id}-{run}", BrokerHost, Broker.Port);
                await bus.StartAsync();
                for (int i = 0; i < 100 && !bus.IsConnected; i++) await Task.Delay(30);
                return bus;
            };

        var engine = new SimulationEngine(scenario, options, connect);
        engine.Manager.Journaled += e => JournalQueue.Enqueue(e);
        await engine.InitializeAsync();
        engine.Coordinator!.Traced += t => TraceQueue.Enqueue(t);
        if (Transport == TransportKind.Mqtt) await Task.Delay(250); // let SUBSCRIBE packets settle before the first mission
        lock (_engineGate) Engine = engine;
    }

    public void Play()
    {
        if (_loop is not null || Engine is null) return;
        var cts = new CancellationTokenSource();
        _loop = cts;
        _ = Task.Run(() => RunLoop(cts.Token));
    }

    public void Pause()
    {
        _loop?.Cancel();
        _loop = null;
    }

    public void StepOnce()
    {
        if (_loop is null) Engine?.Step();
    }

    private async Task RunLoop(CancellationToken ct)
    {
        const double tickMs = 25;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(tickMs));
        double budget = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var e = Engine;
                if (e is null) continue;
                if (e.IsLive) { e.Step(); continue; }
                budget += Speed * tickMs / 1000.0;
                while (budget >= e.Options.TimeStep && !ct.IsCancellationRequested)
                {
                    e.Step();
                    budget -= e.Options.TimeStep;
                }
                if (e.Finished) break;
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_loop?.Token == ct) _loop = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Pause();
        if (Engine is not null) await Engine.DisposeAsync();
        await Broker.DisposeAsync();
    }
}

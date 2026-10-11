using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using SpaceBattle;

namespace SpaceBattle.Web;

public static class BattleBridge
{
    private static Battle<SecsStore>? battle;
    private static readonly BattleFrame frame = new();
    private static readonly ArrayBufferWriter<byte> buffer = new(65536);
    private static string profile = "nominal";

    [JSInvokable]
    public static string Reset(string requestedProfile, uint seed)
    {
        float churn = requestedProfile switch
        {
            "low" => 0.5f,
            "nominal" => 1f,
            "high" => 2f,
            _ => throw new ArgumentOutOfRangeException(nameof(requestedProfile))
        };
        battle?.Dispose();
        battle = null;
        var store = new SecsStore();
        try
        {
            battle = new Battle<SecsStore>(store, new BattleConfig { Seed = seed, ChurnMultiplier = churn });
        }
        catch
        {
            store.Dispose();
            throw;
        }
        profile = requestedProfile;
        battle.Validate();
        return Snapshot();
    }

    [JSInvokable]
    public static string Advance(int ticks)
    {
        if (ticks is < 0 or > 600) throw new ArgumentOutOfRangeException(nameof(ticks));
        var current = Current;
        for (int i = 0; i < ticks; i++) current.Step();
        return Snapshot();
    }

    [JSInvokable]
    public static string Validate()
    {
        Current.Validate();
        return Current.Checksum().ToString("X16");
    }

    [JSInvokable]
    public static string Snapshot()
    {
        var current = Current;
        current.Capture(frame);
        buffer.Clear();
        using var json = new Utf8JsonWriter(buffer);
        json.WriteStartObject();
        json.WriteNumber("tick", frame.Tick);
        json.WriteNumber("arenaHalfSize", frame.ArenaHalfSize);
        json.WriteNumber("seed", current.Config.Seed);
        json.WriteString("profile", profile);
        json.WriteString("revision", "predictive-neutral-v1");
        json.WriteNumber("stepMs", frame.LastTickMs);
        json.WriteString("checksum", current.Checksum().ToString("X16"));
        json.WriteStartObject("counts");
        json.WriteNumber("ships", current.ShipCount);
        json.WriteNumber("projectiles", current.ProjectileCount);
        json.WriteNumber("debris", current.DebrisCount);
        json.WriteNumber("particles", current.ParticleCount);
        json.WriteEndObject();
        json.WriteStartObject("counters");
        var c = frame.Counters;
        json.WriteNumber("shots", c.Shots);
        json.WriteNumber("hits", c.Hits);
        json.WriteNumber("shipsCreated", c.ShipsCreated);
        json.WriteNumber("shipsDestroyed", c.ShipsDestroyed);
        json.WriteNumber("aimDecisions", c.AimDecisions);
        json.WriteNumber("impreciseAimDecisions", c.ImpreciseAimDecisions);
        json.WriteNumber("friendlyFireHits", c.FriendlyFireHits);
        json.WriteNumber("selfHits", c.SelfHits);
        json.WriteNumber("dockings", c.Dockings);
        json.WriteNumber("builds", c.Builds);
        json.WriteNumber("repairs", c.Repairs);
        json.WriteNumber("overheats", c.Overheats);
        json.WriteNumber("shieldBreaks", c.ShieldBreaks);
        json.WriteNumber("debrisHits", c.DebrisHits);
        json.WriteNumber("structuralAdds", c.StructuralAdds);
        json.WriteNumber("structuralRemoves", c.StructuralRemoves);
        json.WriteEndObject();
        WriteEntities(json, "ships", frame.Ships);
        WriteEntities(json, "projectiles", frame.Projectiles);
        WriteEntities(json, "debris", frame.Debris);
        WriteEntities(json, "particles", frame.Particles);
        json.WriteEndObject();
        json.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static Battle<SecsStore> Current => battle ?? throw new InvalidOperationException("Reset the battle first.");

    // Fixed rows avoid reflection/trimming and keep the per-frame WASM boundary compact.
    private static void WriteEntities(Utf8JsonWriter json, string name, List<RenderEntity> entities)
    {
        json.WriteStartArray(name);
        foreach (var e in entities)
        {
            json.WriteStartArray();
            json.WriteNumberValue(e.Serial);
            json.WriteNumberValue(e.Team);
            json.WriteNumberValue(e.Kind);
            json.WriteNumberValue(e.Position.X);
            json.WriteNumberValue(e.Position.Y);
            json.WriteNumberValue(e.Velocity.X);
            json.WriteNumberValue(e.Velocity.Y);
            json.WriteNumberValue(e.Angle);
            json.WriteNumberValue(e.Radius);
            json.WriteNumberValue(e.Hull);
            json.WriteNumberValue(e.Shield);
            json.WriteNumberValue(e.Heat);
            json.WriteNumberValue(e.Color.X);
            json.WriteNumberValue(e.Color.Y);
            json.WriteNumberValue(e.Color.Z);
            json.WriteNumberValue(e.Color.W);
            json.WriteEndArray();
        }
        json.WriteEndArray();
    }
}

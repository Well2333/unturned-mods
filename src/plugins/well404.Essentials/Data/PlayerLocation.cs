using System.Collections.Generic;

namespace well404.Essentials.Data
{
    /// <summary>A stored world position plus facing yaw. Floats so it round-trips cleanly through YAML.</summary>
    public class PlayerLocation
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Yaw { get; set; }

        /// <summary>The Unturned map this position was captured on. Empty for legacy records.</summary>
        public string Map { get; set; } = string.Empty;

        public PlayerLocation() { }

        public PlayerLocation(float x, float y, float z, float yaw, string? map = null)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Map = map?.Trim() ?? string.Empty;
        }
    }

    /// <summary>Per-player persisted state (home, last death point, gift claim times).</summary>
    public class PlayerRecord
    {
        public PlayerLocation? Home { get; set; }

        public PlayerLocation? LastDeath { get; set; }

        /// <summary>Server-persisted player map frame preference: compact or large.</summary>
        public string WarpMapSize { get; set; } = "compact";

        /// <summary>Gift id (lower-case) → Unix time (UTC seconds) of the last claim.</summary>
        public Dictionary<string, long> GiftClaims { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>Root document persisted under the <c>players</c> data-store key.</summary>
    public class PlayerDataDocument
    {
        /// <summary>Steam id → that player's record.</summary>
        public Dictionary<string, PlayerRecord> Players { get; set; } = new Dictionary<string, PlayerRecord>();
    }
}

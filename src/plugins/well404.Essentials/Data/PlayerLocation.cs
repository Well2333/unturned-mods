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

        public PlayerLocation() { }

        public PlayerLocation(float x, float y, float z, float yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }
    }

    /// <summary>Per-player persisted state (home, last death point, gift claim times).</summary>
    public class PlayerRecord
    {
        public PlayerLocation? Home { get; set; }

        public PlayerLocation? LastDeath { get; set; }

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

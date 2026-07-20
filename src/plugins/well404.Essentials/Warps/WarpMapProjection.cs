using System;

namespace well404.Essentials.Warps
{
    /// <summary>Game-runtime-free helpers for map identity and square Chart projection.</summary>
    internal static class WarpMapProjection
    {
        public static bool MatchesMap(string warpMap, string currentMap)
            => !string.IsNullOrWhiteSpace(warpMap) &&
               !string.IsNullOrWhiteSpace(currentMap) &&
               string.Equals(warpMap.Trim(), currentMap.Trim(), StringComparison.OrdinalIgnoreCase);

        public static bool TryProjectSquare(
            float worldX, float worldZ, float effectiveSize, out float horizontal, out float vertical)
        {
            horizontal = 0f;
            vertical = 0f;
            if (effectiveSize <= 0f || float.IsNaN(effectiveSize) || float.IsInfinity(effectiveSize))
            {
                return false;
            }

            horizontal = (worldX / effectiveSize) + 0.5f;
            vertical = 0.5f - (worldZ / effectiveSize);
            return IsNormalized(horizontal, vertical);
        }

        public static bool IsNormalized(float horizontal, float vertical)
            => !float.IsNaN(horizontal) && !float.IsInfinity(horizontal) &&
               !float.IsNaN(vertical) && !float.IsInfinity(vertical) &&
               horizontal >= 0f && horizontal <= 1f && vertical >= 0f && vertical <= 1f;
    }
}

// BoomNetwork TowerDefense Demo — Game State (Fixed-Point)
//
// 20×20 grid. Enemies pour in from all four edges. Defend the 2×2 center base.
// Pure FrameSync: only "place/sell tower" inputs are transmitted.
// 200 enemies on screen = zero extra bandwidth.

namespace BoomNetwork.Samples.TowerDefense
{
    public enum TowerType : byte { None = 0, Arrow = 1, Cannon = 2, Magic = 3 }
    public enum EnemyType : byte { Basic = 0, Fast = 1, Tank = 2 }

    public struct Tower
    {
        public TowerType Type;      // 0 = empty cell
        public int CooldownFrames;  // frames until next attack
    }

    public struct Enemy
    {
        public bool IsAlive;
        public EnemyType Type;
        public FInt PosX, PosZ;     // world coords (cell = 1 unit, top-left = (0,0))
        public int Hp;
        public int SlowFrames;      // Magic tower slow remaining
    }

    public struct WaveState
    {
        public int WaveNumber;          // 1-10
        public int SpawnRemaining;      // enemies left to spawn this wave
        public int InterWaveTimer;      // countdown to next wave (frames)
        public bool AllWavesDone;
    }

    public class GameState
    {
        // ==================== Map ====================
        public const int GridW = 20;
        public const int GridH = 20;
        public const int GridSize = GridW * GridH; // 400

        // Base occupies center 2×2: cells (9,9),(10,9),(9,10),(10,10)
        public const int BaseCX = 9; // top-left corner X of base
        public const int BaseCY = 9; // top-left corner Y of base

        // ==================== Capacities ====================
        public const int MaxPlayers = 4;
        public const int MaxEnemies = 512;
        public const int MaxWaves = 10;

        // ==================== Gold ====================
        public const int InitialGold = 200;

        // ==================== Tower params ====================
        public const int ArrowCost = 50;
        public const int CannonCost = 100;
        public const int MagicCost = 80;

        public const int ArrowCooldown = 15;    // frames between shots
        public const int CannonCooldown = 60;
        public const int MagicCooldown = 30;

        // Arrow/Magic range in grid units (FInt)
        public static readonly FInt ArrowRange  = FInt.FromInt(3);
        public static readonly FInt CannonRange = FInt.FromInt(2);
        public static readonly FInt MagicRange  = FInt.FromInt(4);

        // AoE blast radius for Cannon (grid units)
        public static readonly FInt CannonAoeRadius = FInt.FromFloat(1.2f); // ~1228

        public const int ArrowDamage  = 1;
        public const int CannonDamage = 3;
        public const int MagicDamage  = 1;
        public const int MagicSlowFrames = 60;

        public const int SellRefund = 25; // flat refund for any tower

        // ==================== Enemy params ====================
        // Speed in FInt units per frame (grid/frame)
        public static readonly FInt BasicSpeed = FInt.FromFloat(0.020f);  // 20
        public static readonly FInt FastSpeed  = FInt.FromFloat(0.040f);  // 40
        public static readonly FInt TankSpeed  = FInt.FromFloat(0.010f);  // 10

        public const int BasicHp    = 3;
        public const int FastHp     = 2;
        public const int TankHp     = 10;
        public const int BasicDamage = 1;  // damage to base on reach
        public const int FastDamage  = 1;
        public const int TankDamage  = 2;
        public const int BasicReward = 10;
        public const int FastReward  = 15;
        public const int TankReward  = 30;

        // ==================== Wave timing ====================
        public const int InterWaveFrames = 300; // ~10 sec @30fps before next wave starts

        // ==================== State ====================
        public uint FrameNumber;
        public uint RngState;
        public int BaseHp = 3;
        public int Gold;
        public int[] PlayerGoldContrib = new int[MaxPlayers]; // informational only (not synced in hash)

        public Tower[] Grid = new Tower[GridSize];
        public Enemy[] Enemies = new Enemy[MaxEnemies];
        public WaveState Wave;

        // ==================== Helpers ====================

        public static int CellIndex(int x, int y) => y * GridW + x;

        public static bool IsBase(int x, int y)
            => x >= BaseCX && x < BaseCX + 2 && y >= BaseCY && y < BaseCY + 2;

        public static bool IsInBounds(int x, int y)
            => x >= 0 && x < GridW && y >= 0 && y < GridH;

        public bool CanBuildAt(int x, int y)
        {
            if (!IsInBounds(x, y)) return false;
            if (IsBase(x, y)) return false;
            if (Grid[CellIndex(x, y)].Type != TowerType.None) return false;
            return true;
        }

        public int AllocEnemy()
        {
            for (int i = 0; i < MaxEnemies; i++)
                if (!Enemies[i].IsAlive) return i;
            return -1;
        }

        public static FInt GetEnemySpeed(EnemyType t)
        {
            switch (t) { case EnemyType.Fast: return FastSpeed; case EnemyType.Tank: return TankSpeed; default: return BasicSpeed; }
        }

        public static int GetEnemyHp(EnemyType t)
        {
            switch (t) { case EnemyType.Tank: return TankHp; case EnemyType.Fast: return FastHp; default: return BasicHp; }
        }

        public static int GetEnemyDamage(EnemyType t)
        {
            switch (t) { case EnemyType.Tank: return TankDamage; case EnemyType.Fast: return FastDamage; default: return BasicDamage; }
        }

        public static int GetEnemyReward(EnemyType t)
        {
            switch (t) { case EnemyType.Tank: return TankReward; case EnemyType.Fast: return FastReward; default: return BasicReward; }
        }

        public static int GetTowerCost(TowerType t)
        {
            switch (t) { case TowerType.Cannon: return CannonCost; case TowerType.Magic: return MagicCost; default: return ArrowCost; }
        }

        public static FInt GetTowerRange(TowerType t)
        {
            switch (t) { case TowerType.Cannon: return CannonRange; case TowerType.Magic: return MagicRange; default: return ArrowRange; }
        }

        public static int GetTowerCooldown(TowerType t)
        {
            switch (t) { case TowerType.Cannon: return CannonCooldown; case TowerType.Magic: return MagicCooldown; default: return ArrowCooldown; }
        }

        // Cell center in world coords (cells start at 0,0 top-left)
        public static FInt CellCenterX(int cx) => FInt.FromInt(cx) + FInt.Half;
        public static FInt CellCenterZ(int cy) => FInt.FromInt(cy) + FInt.Half;

        // ==================== ComputeHash ====================

        /// <summary>
        /// FNV-1a hash of all deterministic state fields.
        /// Used for desync detection.
        /// </summary>
        public uint ComputeHash()
        {
            uint h = 2166136261u;

            h = Fnv(h, FrameNumber);
            h = Fnv(h, RngState);
            h = Fnv(h, (uint)BaseHp);
            h = Fnv(h, (uint)Gold);
            h = Fnv(h, (uint)Wave.WaveNumber);
            h = Fnv(h, (uint)Wave.SpawnRemaining);
            h = Fnv(h, (uint)Wave.InterWaveTimer);
            h = Fnv(h, Wave.AllWavesDone ? 1u : 0u);

            for (int i = 0; i < GridSize; i++)
            {
                ref var t = ref Grid[i];
                h = Fnv(h, (uint)t.Type);
                h = Fnv(h, (uint)t.CooldownFrames);
            }

            for (int i = 0; i < MaxEnemies; i++)
            {
                ref var e = ref Enemies[i];
                if (!e.IsAlive) continue;
                h = Fnv(h, (uint)i);
                h = Fnv(h, (uint)e.Type);
                h = Fnv(h, (uint)e.PosX.Raw);
                h = Fnv(h, (uint)e.PosZ.Raw);
                h = Fnv(h, (uint)e.Hp);
                h = Fnv(h, (uint)e.SlowFrames);
            }

            return h;
        }

        static uint Fnv(uint hash, uint value)
        {
            hash ^= value & 0xFF;         hash *= 16777619u;
            hash ^= (value >> 8) & 0xFF;  hash *= 16777619u;
            hash ^= (value >> 16) & 0xFF; hash *= 16777619u;
            hash ^= (value >> 24) & 0xFF; hash *= 16777619u;
            return hash;
        }
    }
}

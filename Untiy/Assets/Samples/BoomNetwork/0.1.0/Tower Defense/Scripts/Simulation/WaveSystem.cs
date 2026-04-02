// BoomNetwork TowerDefense Demo — Wave Spawning System (Fixed-Point)
//
// 10 waves. Enemies spawn from random positions on all four edges.
// Spawn rate: 1 enemy every 10 frames within a wave.
// Wave 10 = Boss wave (Tank × 10 + Fast × 20 from all edges simultaneously).

namespace BoomNetwork.Samples.TowerDefense
{
    public static class WaveSystem
    {
        const int SpawnInterval = 10; // frames between individual spawns

        // Spawn timer is embedded in Wave.InterWaveTimer when negative (reused as spawn tick)
        // We use a separate static to avoid adding state. Simulation manages it via TDSimulation.
        public static int SpawnTickCounter; // reset on each wave start (non-snapshot field, rebuilt on load)

        public static void Tick(GameState state)
        {
            ref var wave = ref state.Wave;

            if (wave.AllWavesDone) return;

            // Inter-wave countdown
            if (wave.SpawnRemaining == 0)
            {
                if (wave.WaveNumber >= GameState.MaxWaves)
                {
                    wave.AllWavesDone = true;
                    return;
                }

                wave.InterWaveTimer--;
                if (wave.InterWaveTimer > 0) return;

                // Start next wave
                wave.WaveNumber++;
                wave.SpawnRemaining = GetWaveCount(wave.WaveNumber);
                wave.InterWaveTimer = GameState.InterWaveFrames;
                SpawnTickCounter = 0;
                return;
            }

            // Spawning
            SpawnTickCounter++;
            if (SpawnTickCounter < SpawnInterval) return;
            SpawnTickCounter = 0;

            SpawnEnemy(state);
            wave.SpawnRemaining--;
        }

        static void SpawnEnemy(GameState state)
        {
            int wn = state.Wave.WaveNumber;
            EnemyType type = PickEnemyType(state, wn);

            // Pick a random edge spawn position
            // Edges: 0=top(z=0), 1=bottom(z=GridH), 2=left(x=0), 3=right(x=GridW)
            int edge = DeterministicRng.RangeInt(ref state.RngState, 0, 4);
            FInt spawnX, spawnZ;
            switch (edge)
            {
                case 0: // top edge (z = -0.5, x = random)
                    spawnX = FInt.FromInt(DeterministicRng.RangeInt(ref state.RngState, 0, GameState.GridW));
                    spawnZ = FInt.FromFloat(-0.5f);
                    break;
                case 1: // bottom edge
                    spawnX = FInt.FromInt(DeterministicRng.RangeInt(ref state.RngState, 0, GameState.GridW));
                    spawnZ = FInt.FromInt(GameState.GridH);
                    break;
                case 2: // left edge
                    spawnX = FInt.FromFloat(-0.5f);
                    spawnZ = FInt.FromInt(DeterministicRng.RangeInt(ref state.RngState, 0, GameState.GridH));
                    break;
                default: // right edge
                    spawnX = FInt.FromInt(GameState.GridW);
                    spawnZ = FInt.FromInt(DeterministicRng.RangeInt(ref state.RngState, 0, GameState.GridH));
                    break;
            }

            int slot = state.AllocEnemy();
            if (slot < 0) return; // pool full

            ref var e = ref state.Enemies[slot];
            e.IsAlive = true;
            e.Type = type;
            e.PosX = spawnX;
            e.PosZ = spawnZ;
            e.Hp = GameState.GetEnemyHp(type);
            e.SlowFrames = 0;
        }

        static EnemyType PickEnemyType(GameState state, int waveNumber)
        {
            if (waveNumber <= 3)
                return EnemyType.Basic;

            if (waveNumber <= 6)
            {
                // 60% Basic, 40% Fast
                int r = DeterministicRng.RangeInt(ref state.RngState, 0, 10);
                return r < 6 ? EnemyType.Basic : EnemyType.Fast;
            }

            if (waveNumber <= 9)
            {
                // 50% Basic, 30% Fast, 20% Tank
                int r = DeterministicRng.RangeInt(ref state.RngState, 0, 10);
                if (r < 5) return EnemyType.Basic;
                if (r < 8) return EnemyType.Fast;
                return EnemyType.Tank;
            }

            // Wave 10: Boss wave — alternating Tank/Fast based on remaining count
            // SpawnRemaining starts at 30 (10 Tank + 20 Fast)
            int remaining = state.Wave.SpawnRemaining;
            return remaining > 20 ? EnemyType.Tank : EnemyType.Fast;
        }

        static int GetWaveCount(int waveNumber)
        {
            switch (waveNumber)
            {
                case 1:  return 10;
                case 2:  return 15;
                case 3:  return 20;
                case 4:  return 25;
                case 5:  return 30;
                case 6:  return 35;
                case 7:  return 40;
                case 8:  return 50;
                case 9:  return 60;
                default: return 30; // Wave 10: 10 Tank + 20 Fast
            }
        }

        /// <summary>Get wave count for display (before spawning starts).</summary>
        public static int GetWaveCountForDisplay(int waveNumber) => GetWaveCount(waveNumber);
    }
}

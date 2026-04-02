// BoomNetwork TowerDefense Demo — Renderer
//
// Top-down orthographic view. Color-coded Quads and Cubes, no external art assets.
// Grid: 20×20 cells. Each cell = 1 Unity unit.
// Camera is fixed top-down, shows the full map.
//
// TryGetGridCell(): converts screen mouse position to grid cell via ray cast against
// an invisible collider plane, enabling click-to-place without physics overhead.

using UnityEngine;

namespace BoomNetwork.Samples.TowerDefense
{
    public class TDRenderer : MonoBehaviour
    {
        GameState _state;
        bool _initialized;
        Camera _cam;

        // ==================== Materials ====================
        Material _matGround, _matGridLine, _matBase;
        Material _matArrow, _matCannon, _matMagic;
        Material _matBasic, _matFast, _matTank;
        Material _matBasicSlow; // slow-tinted enemy

        // ==================== Tower pool ====================
        // One GameObject per grid cell (inactive when no tower)
        GameObject[] _towerObjs = new GameObject[GameState.GridSize];
        Renderer[]   _towerRend = new Renderer[GameState.GridSize];

        // ==================== Enemy pool ====================
        GameObject[] _enemyObjs = new GameObject[GameState.MaxEnemies];
        Renderer[]   _enemyRend = new Renderer[GameState.MaxEnemies];

        // ==================== Attack flash (simple visual feedback) ====================
        // Pool of flash quads that appear briefly when a tower fires
        const int FlashPoolSize = 32;
        struct AttackFlash { public bool Active; public int FramesLeft; public GameObject Obj; }
        AttackFlash[] _flashes = new AttackFlash[FlashPoolSize];

        // Shadow copy of tower cooldowns to detect fire events
        int[] _prevCooldown = new int[GameState.GridSize];

        // ==================== Invisible click plane ====================
        // A large invisible quad at y=0 used for mouse raycast to grid cell.
        Collider _clickPlane;

        // ==================== Camera ====================
        // Cell (0,0) → world (0.5, 0, 0.5). Cell (19,19) → world (19.5, 0, 19.5).
        // Camera placed above center (9.5, h, 9.5), looking straight down.
        const float CamHeight = 24f;
        static readonly Vector3 MapCenter = new Vector3(GameState.GridW * 0.5f, 0f, GameState.GridH * 0.5f);

        // ==================== Init ====================

        public void Init(GameState state)
        {
            _state = state;
            if (_initialized) return;
            _initialized = true;

            CreateMaterials();
            CreateCamera();
            CreateGround();
            CreateGridLines();
            CreateBase();
            CreateTowerPool();
            CreateEnemyPool();
            CreateFlashPool();
            CreateClickPlane();

            for (int i = 0; i < GameState.GridSize; i++) _prevCooldown[i] = 0;
        }

        // ==================== SyncVisuals ====================

        public void SyncVisuals()
        {
            if (!_initialized || _state == null) return;
            SyncTowers();
            SyncEnemies();
            SyncFlashes();
            CaptureFrameShadow();
        }

        void SyncTowers()
        {
            for (int i = 0; i < GameState.GridSize; i++)
            {
                ref var t = ref _state.Grid[i];
                bool show = t.Type != TowerType.None;
                _towerObjs[i].SetActive(show);
                if (!show) continue;

                switch (t.Type)
                {
                    case TowerType.Arrow:  _towerRend[i].sharedMaterial = _matArrow;  break;
                    case TowerType.Cannon: _towerRend[i].sharedMaterial = _matCannon; break;
                    case TowerType.Magic:  _towerRend[i].sharedMaterial = _matMagic;  break;
                }

                // Detect fire: cooldown jumped to full value this frame
                int cooldown = GameState.GetTowerCooldown(t.Type);
                if (t.CooldownFrames == cooldown && _prevCooldown[i] < cooldown)
                    SpawnFlash(i);
            }
        }

        void SyncEnemies()
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                ref var e = ref _state.Enemies[i];
                bool show = e.IsAlive;
                _enemyObjs[i].SetActive(show);
                if (!show) continue;

                float x = e.PosX.ToFloat();
                float z = e.PosZ.ToFloat();
                _enemyObjs[i].transform.position = new Vector3(x, 0.3f, z);

                switch (e.Type)
                {
                    case EnemyType.Basic:
                        _enemyRend[i].sharedMaterial = e.SlowFrames > 0 ? _matBasicSlow : _matBasic;
                        _enemyObjs[i].transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                        break;
                    case EnemyType.Fast:
                        _enemyRend[i].sharedMaterial = e.SlowFrames > 0 ? _matBasicSlow : _matFast;
                        _enemyObjs[i].transform.localScale = new Vector3(0.35f, 0.45f, 0.35f);
                        break;
                    case EnemyType.Tank:
                        _enemyRend[i].sharedMaterial = e.SlowFrames > 0 ? _matBasicSlow : _matTank;
                        _enemyObjs[i].transform.localScale = new Vector3(0.75f, 0.85f, 0.75f);
                        break;
                }
            }
        }

        void SyncFlashes()
        {
            for (int i = 0; i < FlashPoolSize; i++)
            {
                ref var f = ref _flashes[i];
                if (!f.Active) continue;
                f.FramesLeft--;
                if (f.FramesLeft <= 0) { f.Active = false; f.Obj.SetActive(false); }
            }
        }

        void CaptureFrameShadow()
        {
            for (int i = 0; i < GameState.GridSize; i++)
                _prevCooldown[i] = _state.Grid[i].CooldownFrames;
        }

        // ==================== Mouse → Grid Cell ====================

        /// <summary>
        /// Converts screen position to grid cell (gx, gy).
        /// Returns false if click is outside the map.
        /// </summary>
        public bool TryGetGridCell(Vector2 screenPos, out int gx, out int gy)
        {
            gx = gy = -1;
            if (_cam == null) return false;

            Ray ray = _cam.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            // Intersect with y=0 plane
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            Vector3 hit = ray.origin + t * ray.direction;

            gx = Mathf.FloorToInt(hit.x);
            gy = Mathf.FloorToInt(hit.z);

            if (!GameState.IsInBounds(gx, gy)) { gx = gy = -1; return false; }
            return true;
        }

        // ==================== Object Creation ====================

        void CreateMaterials()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _matGround   = new Material(sh) { color = new Color(0.13f, 0.14f, 0.16f) };
            _matGridLine = new Material(sh) { color = new Color(0.22f, 0.24f, 0.28f) };
            _matBase     = new Material(sh) { color = new Color(0.9f, 0.7f, 0.1f) };
            _matArrow    = new Material(sh) { color = new Color(0.3f, 0.9f, 0.3f) };
            _matCannon   = new Material(sh) { color = new Color(0.9f, 0.5f, 0.1f) };
            _matMagic    = new Material(sh) { color = new Color(0.5f, 0.3f, 1.0f) };
            _matBasic    = new Material(sh) { color = new Color(0.9f, 0.2f, 0.2f) };
            _matFast     = new Material(sh) { color = new Color(1.0f, 0.6f, 0.1f) };
            _matTank     = new Material(sh) { color = new Color(0.5f, 0.1f, 0.1f) };
            _matBasicSlow= new Material(sh) { color = new Color(0.4f, 0.4f, 0.9f) };
        }

        void CreateCamera()
        {
            var camObj = new GameObject("TD_Camera");
            camObj.transform.SetParent(transform);
            _cam = camObj.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            _cam.orthographic = true;
            _cam.orthographicSize = 12f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 60f;
            _cam.transform.position = new Vector3(MapCenter.x, CamHeight, MapCenter.z);
            _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var mainCam = Camera.main;
            if (mainCam != null && mainCam != _cam) mainCam.gameObject.SetActive(false);
        }

        void CreateGround()
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            obj.name = "TD_Ground";
            obj.transform.SetParent(transform);
            obj.transform.position = new Vector3(MapCenter.x, -0.01f, MapCenter.z);
            obj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            obj.transform.localScale = new Vector3(GameState.GridW, GameState.GridH, 1f);
            obj.GetComponent<Renderer>().sharedMaterial = _matGround;
            DestroyCollider(obj);
        }

        void CreateGridLines()
        {
            // Vertical lines
            for (int x = 0; x <= GameState.GridW; x++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_VLine";
                obj.transform.SetParent(transform);
                obj.transform.position = new Vector3(x, 0f, MapCenter.z);
                obj.transform.localScale = new Vector3(0.02f, 0.01f, GameState.GridH);
                obj.GetComponent<Renderer>().sharedMaterial = _matGridLine;
                DestroyCollider(obj);
            }
            // Horizontal lines
            for (int z = 0; z <= GameState.GridH; z++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_HLine";
                obj.transform.SetParent(transform);
                obj.transform.position = new Vector3(MapCenter.x, 0f, z);
                obj.transform.localScale = new Vector3(GameState.GridW, 0.01f, 0.02f);
                obj.GetComponent<Renderer>().sharedMaterial = _matGridLine;
                DestroyCollider(obj);
            }
        }

        void CreateBase()
        {
            // 2×2 base highlight
            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    int cx = GameState.BaseCX + dx;
                    int cy = GameState.BaseCY + dy;
                    var obj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    obj.name = "TD_Base";
                    obj.transform.SetParent(transform);
                    obj.transform.position = new Vector3(cx + 0.5f, 0.02f, cy + 0.5f);
                    obj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    obj.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
                    obj.GetComponent<Renderer>().sharedMaterial = _matBase;
                    DestroyCollider(obj);
                }
            }
        }

        void CreateTowerPool()
        {
            for (int i = 0; i < GameState.GridSize; i++)
            {
                int cx = i % GameState.GridW;
                int cy = i / GameState.GridW;
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_Tower";
                obj.transform.SetParent(transform);
                obj.transform.position = new Vector3(cx + 0.5f, 0.4f, cy + 0.5f);
                obj.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                _towerRend[i] = obj.GetComponent<Renderer>();
                _towerRend[i].sharedMaterial = _matArrow;
                DestroyCollider(obj);
                obj.SetActive(false);
                _towerObjs[i] = obj;
            }
        }

        void CreateEnemyPool()
        {
            for (int i = 0; i < GameState.MaxEnemies; i++)
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = "TD_Enemy";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                _enemyRend[i] = obj.GetComponent<Renderer>();
                _enemyRend[i].sharedMaterial = _matBasic;
                DestroyCollider(obj);
                obj.SetActive(false);
                _enemyObjs[i] = obj;
            }
        }

        void CreateFlashPool()
        {
            for (int i = 0; i < FlashPoolSize; i++)
            {
                var matFlash = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
                    { color = new Color(1f, 1f, 0.5f, 0.8f) };
                var obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                obj.name = "TD_Flash";
                obj.transform.SetParent(transform);
                obj.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                obj.GetComponent<Renderer>().sharedMaterial = matFlash;
                DestroyCollider(obj);
                obj.SetActive(false);
                _flashes[i] = new AttackFlash { Obj = obj };
            }
        }

        void CreateClickPlane()
        {
            // Invisible large plane at y = 0 for ray intersection.
            // We do the ray/plane math ourselves (y=0 plane), so no actual collider needed.
            // Kept as a comment — TryGetGridCell uses analytic ray/plane intersection.
        }

        void SpawnFlash(int gridIdx)
        {
            int cx = gridIdx % GameState.GridW;
            int cy = gridIdx / GameState.GridW;
            for (int i = 0; i < FlashPoolSize; i++)
            {
                ref var f = ref _flashes[i];
                if (f.Active) continue;
                f.Active = true;
                f.FramesLeft = 4;
                f.Obj.transform.position = new Vector3(cx + 0.5f, 1.0f, cy + 0.5f);
                f.Obj.SetActive(true);
                return;
            }
        }

        static void DestroyCollider(GameObject obj)
        {
            var col = obj.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        void OnDestroy()
        {
            Destroy(_matGround); Destroy(_matGridLine); Destroy(_matBase);
            Destroy(_matArrow); Destroy(_matCannon); Destroy(_matMagic);
            Destroy(_matBasic); Destroy(_matFast); Destroy(_matTank); Destroy(_matBasicSlow);
        }
    }
}
